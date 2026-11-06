using System;
using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WelfareLottery
{
    /// <summary>
    /// 双色球开奖数据服务：按年份下载 JSON -> 转 DataTable -> 常驻内存。
    /// 开奖数据不再写入数据库，所有查询（采集界面、历史分析、主界面统计）都直接读内存 DataTable。
    /// </summary>
    public class LotteryService
    {
        /// <summary>起始年份（默认从 2000 年开始，双色球 2003 年才开售，无数据的年份会自动跳过）</summary>
        public const int StartYear = 2000;

        /// <summary>结束年份，默认取当前年份</summary>
        public static int EndYear => DateTime.Now.Year;

        private const string JsonUrlTemplate =
            "https://raw.githubusercontent.com/wangdaodao/DoubleColorBall/main/data/{0}.json";

        private const string DbFileName = "WelfareLottery.db";
        public const string TableName = "DoubleColorBall";

        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // 内存中的开奖数据（整体替换，读取方拿到的是不可变快照）
        private static DataTable _data = CreateEmptyTable();
        private static readonly object _gate = new();

        /// <summary>
        /// 本地 SQLite 仅用于保存界面设置（列显示等），不再存放开奖/组合数据。
        /// 支持用环境变量指定位置（自动化测试时使用），默认在 exe 同级目录。
        /// </summary>
        public static string DbPath =>
            Environment.GetEnvironmentVariable("WELFARE_LOTTERY_DB") is { Length: > 0 } custom
                ? custom
                : System.IO.Path.Combine(AppContext.BaseDirectory, DbFileName);

        /// <summary>内存中是否已有开奖数据</summary>
        public static bool HasData
        {
            get { lock (_gate) return _data.Rows.Count > 0; }
        }

        /// <summary>内存中的开奖期数</summary>
        public static int Count
        {
            get { lock (_gate) return _data.Rows.Count; }
        }

        /// <summary>内存占用估算（每期约 200 字节，含字符串与行开销）</summary>
        public static long MemoryBytes
        {
            get { lock (_gate) return _data.Rows.Count * 200L; }
        }

        /// <summary>取内存数据快照（只读使用，调用方不要修改行）</summary>
        public static DataTable Snapshot()
        {
            lock (_gate) return _data;
        }

        /// <summary>按年份生成 JSON 地址</summary>
        public static string GetJsonUrl(int year) => string.Format(JsonUrlTemplate, year);

        /// <summary>获取指定年份的 JSON 字符串</summary>
        public static async Task<string> DownloadJsonAsync(int year)
        {
            using var response = await Http.GetAsync(GetJsonUrl(year));
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        /// <summary>创建空 DataTable（表结构定义）</summary>
        public static DataTable CreateEmptyTable()
        {
            var table = new DataTable(TableName);
            table.Columns.Add("Year", typeof(int));      // 年份
            table.Columns.Add("Code", typeof(string));   // 期号
            table.Columns.Add("Date", typeof(string));   // 开奖日期
            table.Columns.Add("Week", typeof(string));   // 星期
            for (int i = 1; i <= 6; i++)
                table.Columns.Add("Red" + i, typeof(int)); // 红球1-6
            table.Columns.Add("Sum", typeof(int));       // 红球和
            table.Columns.Add("Blue", typeof(int));      // 蓝球
            return table;
        }

        /// <summary>解析单年 JSON 并转换成 DataTable</summary>
        public static DataTable JsonToDataTable(string json, int year)
        {
            var table = CreateEmptyTable();
            FillFromJson(json, year, table);
            table.AcceptChanges();
            return table;
        }

        /// <summary>把一年的 JSON 数据追加到目标 DataTable</summary>
        private static void FillFromJson(string json, int year, DataTable table)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var items))
                throw new InvalidDataException("JSON 中未找到 data 节点。");

            // 优先使用 JSON 自带的 year 字段
            if (root.TryGetProperty("year", out var yearElem) && yearElem.TryGetInt32(out int jsonYear))
                year = jsonYear;

            foreach (var item in items.EnumerateArray())
            {
                var row = table.NewRow();
                row["Year"] = year;
                row["Code"] = item.GetProperty("code").GetString();
                row["Date"] = item.GetProperty("date").GetString();
                row["Week"] = item.GetProperty("week").GetString();

                var reds = item.GetProperty("red");
                int idx = 1;
                foreach (var red in reds.EnumerateArray())
                {
                    if (idx > 6) break;
                    row["Red" + idx] = red.GetInt32();
                    idx++;
                }

                row["Sum"] = item.TryGetProperty("sum", out var sum) ? sum.GetInt32() : 0;
                row["Blue"] = item.GetProperty("blue").GetInt32();
                table.Rows.Add(row);
            }
        }

        /// <summary>
        /// 从 startYear 到 endYear 按年份循环下载，合并成一个 DataTable。
        /// 某年没有数据（如 404）则跳过，并记录在 skippedYears 中。
        /// </summary>
        public static async Task<(DataTable table, List<int> skippedYears)> DownloadAllYearsAsync(
            int startYear = StartYear,
            int? endYear = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int lastYear = endYear ?? EndYear;
            var table = CreateEmptyTable();
            var skipped = new List<int>();

            for (int year = startYear; year <= lastYear; year++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"正在获取 {year} 年数据 ...");

                try
                {
                    string json = await DownloadJsonAsync(year);
                    int before = table.Rows.Count;
                    FillFromJson(json, year, table);
                    progress?.Report($"{year} 年：{table.Rows.Count - before} 期");
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
                {
                    skipped.Add(year);
                    progress?.Report($"{year} 年：无数据，已跳过（{ex.Message}）");
                }
            }

            table.AcceptChanges();
            return (table, skipped);
        }

        /// <summary>
        /// 把采集结果合并到内存（按期号去重，同期号覆盖为新值），
        /// 结束后内存数据统一按 Year DESC, Code DESC 排列。返回新增/更新条数。
        /// </summary>
        public static (int added, int updated) Merge(DataTable table)
        {
            int added = 0, updated = 0;

            lock (_gate)
            {
                var index = new Dictionary<string, DataRow>(StringComparer.Ordinal);
                foreach (DataRow row in _data.Rows)
                    index[Convert.ToString(row["Code"]) ?? string.Empty] = row;

                foreach (DataRow source in table.Rows)
                {
                    string code = Convert.ToString(source["Code"]) ?? string.Empty;
                    if (index.TryGetValue(code, out var existing))
                    {
                        CopyRow(source, existing);
                        updated++;
                    }
                    else
                    {
                        var row = _data.NewRow();
                        CopyRow(source, row);
                        _data.Rows.Add(row);
                        index[code] = row;
                        added++;
                    }
                }

                _data.AcceptChanges();
                _data = SortDesc(_data);
            }

            return (added, updated);
        }

        /// <summary>把采集结果作为全新数据集替换内存（不做合并）</summary>
        public static int Replace(DataTable table)
        {
            var sorted = SortDesc(table);
            lock (_gate) _data = sorted;
            return sorted.Rows.Count;
        }

        /// <summary>清空内存数据</summary>
        public static void Clear()
        {
            lock (_gate) _data = CreateEmptyTable();
        }

        private static void CopyRow(DataRow source, DataRow target)
        {
            foreach (DataColumn column in target.Table!.Columns)
                target[column.ColumnName] = source[column.ColumnName];
        }

        /// <summary>按 Year DESC, Code DESC 重排（重建表，行数很少，开销可忽略）</summary>
        private static DataTable SortDesc(DataTable table)
        {
            var view = table.DefaultView;
            view.Sort = "Year DESC, Code DESC";
            var sorted = view.ToTable(TableName);
            sorted.AcceptChanges();
            return sorted;
        }

        /// <summary>开奖数据统计：总期数、最早与最晚年份</summary>
        public static (long count, int minYear, int maxYear) GetStatistics()
        {
            lock (_gate)
            {
                long count = _data.Rows.Count;
                if (count == 0) return (0, 0, 0);

                int minYear = int.MaxValue, maxYear = int.MinValue;
                foreach (DataRow row in _data.Rows)
                {
                    int year = Convert.ToInt32(row["Year"]);
                    if (year < minYear) minYear = year;
                    if (year > maxYear) maxYear = year;
                }
                return (count, minYear, maxYear);
            }
        }

        /// <summary>取内存数据的副本（已按 Year DESC, Code DESC 排序，供表格绑定）</summary>
        public static DataTable GetDataTable()
        {
            lock (_gate) return _data.Copy();
        }

        /// <summary>
        /// 删除历史版本写入数据库的开奖表（开奖数据已改为只存内存）。
        /// 返回是否真的删除了表；删除后可用「压缩数据库」回收磁盘空间。
        /// </summary>
        public static bool PurgeLegacyTable()
        {
            string path = DbPath;
            if (!System.IO.File.Exists(path)) return false;

            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
                check.Parameters.AddWithValue("$name", TableName);
                if (check.ExecuteScalar() == null) return false;
            }

            using (var drop = conn.CreateCommand())
            {
                drop.CommandText = $"DROP TABLE IF EXISTS {TableName};";
                drop.CommandTimeout = 600;
                drop.ExecuteNonQuery();
            }
            return true;
        }
    }
}
