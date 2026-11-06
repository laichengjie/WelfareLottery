using System.Data;

namespace WelfareLottery
{
    /// <summary>
    /// 历史开奖数据分析：按号码分组查找重复出现的组合。
    /// 数据源为内存中的开奖 DataTable（3523 期左右），不访问数据库。
    /// </summary>
    public static class HistoryAnalysisService
    {
        /// <summary>分析条件</summary>
        public sealed class Condition
        {
            /// <summary>true：只看“6 个红球相同”（蓝球可不同）；false：红球 + 蓝球完全相同</summary>
            public bool SameRedOnly { get; init; }

            /// <summary>年份下限（0 = 不限）</summary>
            public int YearFrom { get; init; }

            /// <summary>年份上限（0 = 不限）</summary>
            public int YearTo { get; init; }

            /// <summary>和值下限（0 = 不限）</summary>
            public int SumMin { get; init; }

            /// <summary>和值上限（0 = 不限）</summary>
            public int SumMax { get; init; }

            /// <summary>蓝球（0 = 不限）</summary>
            public int Blue { get; init; }
        }

        /// <summary>按号码分组查找重复组合（出现 2 次及以上）</summary>
        public static DataTable FindDuplicates(Condition condition)
        {
            var table = CreateDuplicateTable();
            var source = LotteryService.Snapshot();

            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            foreach (DataRow row in source.Rows)
            {
                if (!Match(row, condition)) continue;

                var reds = ReadReds(row);
                int blue = Convert.ToInt32(row["Blue"]);
                string key = string.Join(",", reds) + (condition.SameRedOnly ? string.Empty : "|" + blue);

                if (!groups.TryGetValue(key, out var group))
                {
                    group = new Group(reds, blue, condition.SameRedOnly);
                    groups[key] = group;
                }
                group.Add(blue, Convert.ToString(row["Code"]) ?? string.Empty);
            }

            var ordered = groups.Values
                .Where(g => g.Count > 1)
                .OrderByDescending(g => g.Count)
                .ThenByDescending(g => g.MaxCode, StringComparer.Ordinal);

            foreach (var group in ordered)
            {
                table.Rows.Add(
                    group.Reds[0], group.Reds[1], group.Reds[2],
                    group.Reds[3], group.Reds[4], group.Reds[5],
                    group.BlueText, group.Count, string.Join(",", group.Codes));
            }
            return table;
        }

        /// <summary>查询某一组号码的全部开奖记录（blue = 0 表示不限蓝球）</summary>
        public static DataTable LoadDraws(int[] reds, int blue)
        {
            var ordered = reds.Where(r => r >= 1 && r <= 33).Distinct().OrderBy(r => r).ToArray();
            var table = CreateDrawTable();

            foreach (DataRow row in LotteryService.Snapshot().Rows)
            {
                bool hit = true;
                for (int i = 0; i < ordered.Length && i < 6; i++)
                {
                    if (Convert.ToInt32(row["Red" + (i + 1)]) != ordered[i]) { hit = false; break; }
                }
                if (!hit) continue;
                if (blue > 0 && Convert.ToInt32(row["Blue"]) != blue) continue;

                AddDrawRow(table, row);
            }
            return table;
        }

        /// <summary>统计重复情况：重复组数、涉及期数</summary>
        public static (int groupCount, int drawCount) Summarize(DataTable groups)
        {
            int draws = 0;
            foreach (DataRow row in groups.Rows)
            {
                if (row["出现次数"] is int count) draws += count;
                else if (int.TryParse(Convert.ToString(row["出现次数"]), out int parsed)) draws += parsed;
            }
            return (groups.Rows.Count, draws);
        }

        /// <summary>参与两两比较的开奖记录</summary>
        private sealed class DrawItem
        {
            public string Code { get; init; } = string.Empty;
            public int[] Reds { get; init; } = Array.Empty<int>();
            public int Blue { get; init; }
            public int Mask { get; init; }        // 红球位掩码，用于快速求交集个数
            public string RedText => string.Join(" ", Reds);
        }

        /// <summary>
        /// 相似组合：两期红球相同个数 >= minHit（minHit = 4 表示至少 4 个红球相同）。
        /// 3523 期两两比较只有 620 万次，在内存中用位运算完成，毫秒级。
        /// </summary>
        public static DataTable FindSimilarPairs(int minHit, Condition condition, int limit = 500)
        {
            var draws = LoadDrawItems(condition);

            var results = new List<(DrawItem a, DrawItem b, int hits)>();
            for (int i = 0; i < draws.Count; i++)
            {
                for (int j = i + 1; j < draws.Count; j++)
                {
                    int hits = PopCount(draws[i].Mask & draws[j].Mask);
                    if (hits >= minHit) results.Add((draws[i], draws[j], hits));
                }
            }

            results.Sort((x, y) => x.hits != y.hits
                ? y.hits.CompareTo(x.hits)
                : string.CompareOrdinal(y.a.Code, x.a.Code));

            var table = new DataTable("SimilarPairs");
            table.Columns.Add("期号1", typeof(string));
            table.Columns.Add("红球1", typeof(string));
            table.Columns.Add("蓝球1", typeof(int));
            table.Columns.Add("期号2", typeof(string));
            table.Columns.Add("红球2", typeof(string));
            table.Columns.Add("蓝球2", typeof(int));
            table.Columns.Add("命中红球", typeof(int));

            foreach (var (a, b, hits) in results.Take(limit))
                table.Rows.Add(a.Code, a.RedText, a.Blue, b.Code, b.RedText, b.Blue, hits);

            return table;
        }

        /// <summary>
        /// 蓝球相同、红球完全不同的两期组合。
        /// 结果按期号倒序，最多 limit 条。
        /// </summary>
        public static DataTable FindBlueSamePairs(Condition condition, int limit = 500)
        {
            var draws = LoadDrawItems(condition);

            var results = new List<(DrawItem a, DrawItem b)>();
            for (int i = 0; i < draws.Count; i++)
            {
                for (int j = i + 1; j < draws.Count; j++)
                {
                    if (draws[i].Blue == draws[j].Blue && (draws[i].Mask & draws[j].Mask) == 0)
                        results.Add((draws[i], draws[j]));
                }
            }

            results.Sort((x, y) => string.CompareOrdinal(y.a.Code, x.a.Code));

            var table = new DataTable("BlueSamePairs");
            table.Columns.Add("期号1", typeof(string));
            table.Columns.Add("红球1", typeof(string));
            table.Columns.Add("蓝球", typeof(int));
            table.Columns.Add("期号2", typeof(string));
            table.Columns.Add("红球2", typeof(string));

            foreach (var (a, b) in results.Take(limit))
                table.Rows.Add(a.Code, a.RedText, a.Blue, b.Code, b.RedText);

            return table;
        }

        /// <summary>按条件读取开奖记录（不含 Date/Week 等展示字段）</summary>
        private static List<DrawItem> LoadDrawItems(Condition condition)
        {
            var list = new List<DrawItem>();
            foreach (DataRow row in LotteryService.Snapshot().Rows)
            {
                if (!Match(row, condition)) continue;

                var reds = ReadReds(row);
                int mask = 0;
                foreach (int red in reds) mask |= 1 << red;
                list.Add(new DrawItem
                {
                    Code = Convert.ToString(row["Code"]) ?? string.Empty,
                    Reds = reds,
                    Blue = Convert.ToInt32(row["Blue"]),
                    Mask = mask
                });
            }

            // 与原 SQL 的 a.Code < b.Code 口径一致：期号 1 为较早一期
            list.Sort((x, y) => string.CompareOrdinal(x.Code, y.Code));
            return list;
        }

        private static int PopCount(int value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }

        /// <summary>按期号批量取开奖记录（用于相似组合的明细）</summary>
        public static DataTable LoadDrawsByCodes(IEnumerable<string> codes)
        {
            var table = CreateDrawTable();
            var wanted = codes.Where(c => !string.IsNullOrWhiteSpace(c)).ToHashSet(StringComparer.Ordinal);
            if (wanted.Count == 0) return table;

            foreach (DataRow row in LotteryService.Snapshot().Rows)
            {
                var code = Convert.ToString(row["Code"]);
                if (code != null && wanted.Contains(code)) AddDrawRow(table, row);
            }
            return table;
        }

        private static bool Match(DataRow row, Condition condition)
        {
            if (condition.YearFrom > 0 && Convert.ToInt32(row["Year"]) < condition.YearFrom) return false;
            if (condition.YearTo > 0 && Convert.ToInt32(row["Year"]) > condition.YearTo) return false;
            if (condition.SumMin > 0 && Convert.ToInt32(row["Sum"]) < condition.SumMin) return false;
            if (condition.SumMax > 0 && Convert.ToInt32(row["Sum"]) > condition.SumMax) return false;
            if (condition.Blue > 0 && Convert.ToInt32(row["Blue"]) != condition.Blue) return false;
            return true;
        }

        private static int[] ReadReds(DataRow row) => new[]
        {
            Convert.ToInt32(row["Red1"]), Convert.ToInt32(row["Red2"]), Convert.ToInt32(row["Red3"]),
            Convert.ToInt32(row["Red4"]), Convert.ToInt32(row["Red5"]), Convert.ToInt32(row["Red6"])
        };

        private static DataTable CreateDuplicateTable()
        {
            var table = new DataTable("DuplicateGroups");
            for (int i = 1; i <= 6; i++) table.Columns.Add($"红{i}", typeof(int));
            table.Columns.Add("蓝球", typeof(string));
            table.Columns.Add("出现次数", typeof(int));
            table.Columns.Add("期号", typeof(string));
            return table;
        }

        private static DataTable CreateDrawTable()
        {
            var table = new DataTable("DrawDetail");
            table.Columns.Add("Year", typeof(int));
            table.Columns.Add("Code", typeof(string));
            table.Columns.Add("Date", typeof(string));
            table.Columns.Add("Week", typeof(string));
            for (int i = 1; i <= 6; i++) table.Columns.Add("Red" + i, typeof(int));
            table.Columns.Add("Sum", typeof(int));
            table.Columns.Add("Blue", typeof(int));
            return table;
        }

        private static void AddDrawRow(DataTable table, DataRow source)
        {
            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
                row[column.ColumnName] = source[column.ColumnName];
            table.Rows.Add(row);
        }

        /// <summary>分组累加器</summary>
        private sealed class Group
        {
            public Group(int[] reds, int blue, bool sameRedOnly)
            {
                Reds = reds;
                Blue = blue;
                SameRedOnly = sameRedOnly;
            }

            public int[] Reds { get; }
            public int Blue { get; }
            public bool SameRedOnly { get; }
            public int Count { get; private set; }
            public string MaxCode { get; private set; } = string.Empty;
            public SortedSet<string> Codes { get; } = new(StringComparer.Ordinal);
            public SortedSet<int> Blues { get; } = new();

            public string BlueText => SameRedOnly ? string.Join(",", Blues) : Blue.ToString();

            public void Add(int blue, string code)
            {
                Count++;
                Blues.Add(blue);
                if (string.IsNullOrEmpty(code)) return;
                Codes.Add(code);
                if (string.CompareOrdinal(code, MaxCode) > 0) MaxCode = code;
            }
        }
    }
}
