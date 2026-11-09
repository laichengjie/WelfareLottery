using System;
using System.Data;
using Microsoft.Data.Sqlite;

namespace WelfareLottery
{
    /// <summary>红球连续形态过滤（只作用于红区）</summary>
    [Flags]
    public enum RunFilter
    {
        /// <summary>不过滤</summary>
        None = 0,
        /// <summary>排除含 3 连号（如 1 2 3）的组合，含 4/5 连号的一并排除</summary>
        Exclude3 = 1,
        /// <summary>排除含 4 连号（如 1 2 3 4）的组合，含 5 连号的一并排除</summary>
        Exclude4 = 2,
        /// <summary>排除 5 连号（如 1 2 3 4 5）的组合</summary>
        Exclude5 = 4,
        /// <summary>只保留含 3 连号及以上（最长连号 ≥ 3）的组合</summary>
        Include3 = 8,
        /// <summary>只保留含 4 连号及以上（最长连号 ≥ 4）的组合</summary>
        Include4 = 16,
        /// <summary>只保留含 5 连号（最长连号 = 5）的组合</summary>
        Include5 = 32
    }

    /// <summary>
    /// 双色球全部组合服务：C(33,6) * 16 = 17,721,088 种组合。
    /// 组合数据只在内存中生成与查询，不写入数据库；
    /// 每行 7 字节（红球 1-6 + 蓝球），全量约 118 MB，查询时按需转成 DataTable。
    /// </summary>
    public static class CombinationService
    {
        /// <summary>红球选 6 的组数</summary>
        public const long RedCombinations = 1_107_568L;

        /// <summary>蓝球个数</summary>
        public const int BlueCount = 16;

        /// <summary>组合总数</summary>
        public const long TotalCombinations = RedCombinations * BlueCount;

        /// <summary>每行字节数：6 个红球 + 1 个蓝球</summary>
        private const int FieldsPerRow = 7;

        private const string LegacyTableName = "Combination";

        /// <summary>内存状态（整体替换，保证查询期间读到的是一致的快照）</summary>
        private sealed class State
        {
            public byte[]? Rows;              // 按 Red1..Red6, Blue 字典序排列
            public long Count;                // 总条数
            public int[]? FilterIndex;        // 过滤后的源行号；null 表示未过滤
            public long FilteredCount;        // 过滤后条数
            public RunFilter Filter;          // 当前过滤条件
            public int[]? Buffer;             // 复用的行号缓冲（避免每次过滤都分配 71 MB）
        }

        private static volatile State _state = new();
        private static readonly object _gate = new();

        /// <summary>内存中是否已有组合数据</summary>
        public static bool IsReady => _state.Count > 0;

        /// <summary>内存中当前组合条数（未过滤）</summary>
        public static long GetCount(bool forceRefresh = false) => _state.Count;

        /// <summary>当前可见条数（应用过滤后）</summary>
        public static long FilteredCount => _state.FilterIndex == null ? _state.Count : _state.FilteredCount;

        /// <summary>当前红球形态过滤条件</summary>
        public static RunFilter CurrentFilter => _state.Filter;

        /// <summary>兼容旧调用（内存数据无需失效缓存）</summary>
        public static void InvalidateCount()
        {
        }

        /// <summary>内存占用（字节）</summary>
        public static long MemoryBytes => _state.Count * FieldsPerRow;

        /// <summary>释放内存中的组合数据</summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _state = new State();
            }
        }

        /// <summary>
        /// 在内存中生成全部组合（红球升序 + 蓝球 1-16，顺序即字典序）。
        /// 生成完成才替换掉旧数据，中途取消不会影响已有结果。
        /// redMax 仅用于测试（默认 33，即全部组合）。
        /// </summary>
        public static Task<long> GenerateAllAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            int redMax = 33)
        {
            return Task.Run(() => Generate(progress, cancellationToken, redMax), cancellationToken);
        }

        private static long Generate(IProgress<string>? progress, CancellationToken token, int redMax)
        {
            long expected = Binomial(redMax, 6) * BlueCount;
            if (expected <= 0) throw new InvalidOperationException("组合数量计算失败。");
            if (expected > int.MaxValue / FieldsPerRow)
                throw new InvalidOperationException($"组合数 {expected:N0} 超出内存容量，无法一次生成。");

            int totalBytes = checked((int)(expected * FieldsPerRow));
            progress?.Report($"正在内存中生成 {expected:N0} 条组合（约 {totalBytes / 1024.0 / 1024.0:F0} MB，不写数据库）...");

            var rows = new byte[totalBytes];
            long count = 0;
            int offset = 0;

            for (int r1 = 1; r1 <= redMax - 5; r1++)
            for (int r2 = r1 + 1; r2 <= redMax - 4; r2++)
            for (int r3 = r2 + 1; r3 <= redMax - 3; r3++)
            for (int r4 = r3 + 1; r4 <= redMax - 2; r4++)
            for (int r5 = r4 + 1; r5 <= redMax - 1; r5++)
            for (int r6 = r5 + 1; r6 <= redMax; r6++)
            {
                for (int blue = 1; blue <= BlueCount; blue++)
                {
                    rows[offset++] = (byte)r1;
                    rows[offset++] = (byte)r2;
                    rows[offset++] = (byte)r3;
                    rows[offset++] = (byte)r4;
                    rows[offset++] = (byte)r5;
                    rows[offset++] = (byte)r6;
                    rows[offset++] = (byte)blue;
                    count++;
                }

                if ((count & 0x3FFF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report($"已生成 {count:N0} / {expected:N0}（{count * 100.0 / expected:F1}%）");
                }
            }

            token.ThrowIfCancellationRequested();

            lock (_gate)
            {
                // 新数据 => 过滤条件失效
                _state = new State { Rows = rows, Count = count, FilteredCount = count, Filter = RunFilter.None };
            }

            progress?.Report($"生成完成：{count:N0} 条组合已驻留内存（{totalBytes / 1024.0 / 1024.0:F0} MB），未写入数据库");
            return count;
        }

        /// <summary>组合数 C(n, k)</summary>
        private static long Binomial(int n, int k)
        {
            if (k < 0 || k > n) return 0;
            long result = 1;
            for (int i = 1; i <= k; i++)
                result = result * (n - k + i) / i;
            return result;
        }

        /// <summary>
        /// 应用红球连续形态过滤（只作用于红区，蓝球不受影响）。
        /// 返回过滤后剩余条数；条件为 None 时清除过滤。
        /// 1772 万条全量扫描一次约 100 ms，界面应在后台线程调用。
        /// </summary>
        public static long ApplyRunFilter(RunFilter filter)
        {
            var state = _state;
            var rows = state.Rows;
            long count = state.Count;

            if (rows == null || count == 0)
            {
                lock (_gate)
                {
                    _state = new State { Rows = rows, Count = count, Filter = filter, FilteredCount = count };
                }
                return 0;
            }

            if (filter == RunFilter.None)
            {
                lock (_gate)
                {
                    _state = new State { Rows = rows, Count = count, FilteredCount = count, Filter = RunFilter.None };
                }
                return count;
            }

            // 复用同一块缓冲：默认全过滤时 kept == count，不需要额外索引数组
            var index = state.Buffer != null && state.Buffer.Length >= count ? state.Buffer : new int[count];

            // 同一红球组合的 16 个蓝球行红区完全相同，只需判断一次再批量展开，扫描量降到 1/16
            long combos = count / BlueCount;
            long kept = 0;
            for (long c = 0; c < combos; c++)
            {
                int offset = (int)(c * BlueCount * FieldsPerRow);
                if (!MatchesRunFilter(rows, offset, filter)) continue;

                long source = c * BlueCount;
                for (int b = 0; b < BlueCount && kept < count; b++)
                    index[kept++] = (int)(source + b);
            }

            lock (_gate)
            {
                _state = new State
                {
                    Rows = rows,
                    Count = count,
                    Filter = filter,
                    FilterIndex = kept == count ? null : index,   // index 长度可能大于 kept，用 FilteredCount 限制读取
                    FilteredCount = kept,
                    Buffer = index
                };
            }
            return kept;
        }

        /// <summary>
        /// 判断该行红球（最长连续链 maxRun）是否满足所有勾选条件：
        /// Exclude* 要求不含对应及以上连号；Include* 要求含对应及以上连号。
        /// 多个条件同时勾选时为"且"关系。
        /// </summary>
        private static bool MatchesRunFilter(byte[] rows, int offset, RunFilter filter)
        {
            int maxRun = 1;
            int run = 1;
            for (int i = 1; i < 6; i++)
            {
                run = rows[offset + i] == rows[offset + i - 1] + 1 ? run + 1 : 1;
                if (run > maxRun) maxRun = run;
            }

            if ((filter & RunFilter.Exclude3) != 0 && maxRun >= 3) return false;
            if ((filter & RunFilter.Exclude4) != 0 && maxRun >= 4) return false;
            if ((filter & RunFilter.Exclude5) != 0 && maxRun >= 5) return false;
            if ((filter & RunFilter.Include3) != 0 && maxRun < 3) return false;
            if ((filter & RunFilter.Include4) != 0 && maxRun < 4) return false;
            if ((filter & RunFilter.Include5) != 0 && maxRun < 5) return false;
            return true;
        }

        /// <summary>
        /// 分页查询（直接从内存取，构造当前页的 DataTable）。
        /// 数据按 Red1..Red6, Blue 字典序存放；应用过滤后按过滤结果的行号映射。
        /// </summary>
        public static (DataTable table, long totalRows) QueryPageData(int pageIndex, int pageSize)
        {
            var state = _state;
            var rows = state.Rows;
            var filterIndex = state.FilterIndex;
            long total = filterIndex == null ? state.Count : state.FilteredCount;

            var table = CreateTable();

            long start = Math.Clamp((long)pageIndex * pageSize, 0, total);
            long end = Math.Min(total, start + pageSize);

            if (rows != null && end > start)
            {
                table.BeginLoadData();
                for (long i = start; i < end; i++)
                {
                    long source = filterIndex == null ? i : filterIndex[i];
                    int o = (int)(source * FieldsPerRow);
                    table.Rows.Add(
                        (int)rows[o], (int)rows[o + 1], (int)rows[o + 2],
                        (int)rows[o + 3], (int)rows[o + 4], (int)rows[o + 5],
                        (int)rows[o + 6]);
                }
                table.EndLoadData();
            }

            return (table, total);
        }

        /// <summary>分页查询（同步版本，兼容旧调用）</summary>
        public static DataTable QueryPage(int pageIndex, int pageSize, out long totalRows)
        {
            var (table, total) = QueryPageData(pageIndex, pageSize);
            totalRows = total;
            return table;
        }

        /// <summary>
        /// 按号码查询组合（全部在内存中完成，不访问数据库；受红球形态过滤影响）。
        /// reds：指定红球（1-33，可不全）；blue：蓝球（0 表示不限）。
        /// 指定满 6 个红球时用组合排名反算行号，O(1) 精确定位；
        /// 否则按“包含指定号码”扫描内存，最多返回 limit 条。
        /// </summary>
        public static (DataTable table, bool exact, bool truncated) QueryByNumbers(int[] reds, int blue, int limit = 500)
        {
            var ordered = reds.Where(r => r >= 1 && r <= 33).Distinct().OrderBy(r => r).ToArray();
            bool exact = ordered.Length == 6;
            var table = CreateTable();

            var state = _state;
            var rows = state.Rows;
            var filterIndex = state.FilterIndex;
            var filter = state.Filter;

            if (rows == null || state.Count == 0) return (table, exact, false);

            if (exact)
            {
                long redRank = RankRedCombination(ordered, 33);
                table.BeginLoadData();
                for (int b = 1; b <= BlueCount; b++)
                {
                    if (blue > 0 && b != blue) continue;

                    long index = redRank * BlueCount + (b - 1);
                    if (index < 0 || index >= state.Count) continue;

                    int o = (int)(index * FieldsPerRow);
                    if (rows[o] != ordered[0] || rows[o + 1] != ordered[1] || rows[o + 2] != ordered[2] ||
                        rows[o + 3] != ordered[3] || rows[o + 4] != ordered[4] || rows[o + 5] != ordered[5] ||
                        rows[o + 6] != b)
                    {
                        continue;   // 防御：与内存数据不一致时跳过
                    }

                    if (filter != RunFilter.None && !MatchesRunFilter(rows, o, filter)) continue;

                    table.Rows.Add(ordered[0], ordered[1], ordered[2], ordered[3], ordered[4], ordered[5], b);
                    if (table.Rows.Count >= limit) break;
                }
                table.EndLoadData();
                return (table, true, false);
            }

            long visible = filterIndex == null ? state.Count : state.FilteredCount;
            table.BeginLoadData();
            for (long i = 0; i < visible; i++)
            {
                long source = filterIndex == null ? i : filterIndex[i];
                int o = (int)(source * FieldsPerRow);
                if (blue > 0 && rows[o + 6] != blue) continue;

                bool allHit = true;
                foreach (int red in ordered)
                {
                    if (rows[o] != red && rows[o + 1] != red && rows[o + 2] != red &&
                        rows[o + 3] != red && rows[o + 4] != red && rows[o + 5] != red)
                    {
                        allHit = false;
                        break;
                    }
                }
                if (!allHit) continue;

                table.Rows.Add(
                    (int)rows[o], (int)rows[o + 1], (int)rows[o + 2],
                    (int)rows[o + 3], (int)rows[o + 4], (int)rows[o + 5],
                    (int)rows[o + 6]);

                if (table.Rows.Count >= limit) break;
            }
            table.EndLoadData();

            return (table, false, table.Rows.Count >= limit);
        }

        private static DataTable CreateTable()
        {
            var table = new DataTable(LegacyTableName);
            table.Columns.Add("Red1", typeof(int));
            table.Columns.Add("Red2", typeof(int));
            table.Columns.Add("Red3", typeof(int));
            table.Columns.Add("Red4", typeof(int));
            table.Columns.Add("Red5", typeof(int));
            table.Columns.Add("Red6", typeof(int));
            table.Columns.Add("Blue", typeof(int));
            return table;
        }

        /// <summary>取指定序号（字典序排名）的组合号码，用于校验/定位</summary>
        public static (int r1, int r2, int r3, int r4, int r5, int r6, int blue) GetKeyAt(long rowRank)
        {
            long redRank = rowRank / BlueCount;
            int blue = (int)(rowRank % BlueCount) + 1;
            var reds = UnrankRedCombination(redRank, 33);
            return (reds[0], reds[1], reds[2], reds[3], reds[4], reds[5], blue);
        }

        /// <summary>红球组合的字典序排名（ranking）</summary>
        private static long RankRedCombination(int[] reds, int redMax)
        {
            long rank = 0;
            int prev = 0;
            for (int i = 0; i < 6 && i < reds.Length; i++)
            {
                int remain = 5 - i;
                for (int v = prev + 1; v < reds[i]; v++)
                    rank += Binomial(redMax - v, remain);
                prev = reds[i];
            }
            return rank;
        }

        /// <summary>红球组合的字典序反排名（unranking），返回升序 6 个红球</summary>
        private static int[] UnrankRedCombination(long rank, int redMax)
        {
            var result = new int[6];
            int prev = 0;
            for (int i = 0; i < 6; i++)
            {
                int remain = 5 - i;
                for (int v = prev + 1; v <= redMax; v++)
                {
                    long count = Binomial(redMax - v, remain);
                    if (rank < count)
                    {
                        result[i] = v;
                        prev = v;
                        break;
                    }
                    rank -= count;
                }
            }
            return result;
        }

        /// <summary>
        /// 删除历史版本写入数据库的组合表（组合数据已改为只存内存）。
        /// 返回是否真的删除了表；删除后可用「压缩数据库」回收磁盘空间。
        /// </summary>
        public static bool DropLegacyTable()
        {
            string path = LotteryService.DbPath;
            if (!System.IO.File.Exists(path)) return false;

            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
                check.Parameters.AddWithValue("$name", LegacyTableName);
                if (check.ExecuteScalar() == null) return false;
            }

            using (var drop = conn.CreateCommand())
            {
                drop.CommandText = $"DROP TABLE IF EXISTS {LegacyTableName};";
                drop.CommandTimeout = 600;
                drop.ExecuteNonQuery();
            }
            return true;
        }

        /// <summary>
        /// 回收磁盘空间：先截断 WAL，再执行 VACUUM 重排数据库文件。
        /// 数据量越大耗时越长，期间会占用额外临时磁盘空间。
        /// </summary>
        public static void Vacuum()
        {
            using var conn = new SqliteConnection($"Data Source={LotteryService.DbPath}");
            conn.Open();
            VacuumCore(conn);
        }

        private static void VacuumCore(SqliteConnection conn)
        {
            using (var checkpoint = conn.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }

            using (var vacuum = conn.CreateCommand())
            {
                vacuum.CommandText = "VACUUM;";
                vacuum.CommandTimeout = 3600;
                vacuum.ExecuteNonQuery();
            }

            using (var checkpoint = conn.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
            }
        }

        /// <summary>数据库文件大小（字节）</summary>
        public static long GetDatabaseSize()
        {
            var path = LotteryService.DbPath;
            return System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length : 0;
        }
    }
}
