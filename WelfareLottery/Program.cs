using System.Data;

namespace WelfareLottery;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // 命令行测试模式：WelfareLottery --test  下载 -> 转DataTable -> 存SQLite -> 回读验证
        if (args.Contains("--test", StringComparer.OrdinalIgnoreCase))
        {
            return RunTestAsync().GetAwaiter().GetResult();
        }

        // 命令行测试模式：WelfareLottery --setting-test  列显示设置存取验证
        if (args.Contains("--setting-test", StringComparer.OrdinalIgnoreCase))
        {
            var sample = new Dictionary<string, bool> { ["Code"] = true, ["Sum"] = false, ["Blue"] = true };
            ColumnSettingService.Save("__test__", sample);
            var loaded = ColumnSettingService.Load("__test__");
            ColumnSettingService.Save("__test__", new Dictionary<string, bool>());   // 清理测试数据

            bool ok = loaded.Count == 3 && loaded.TryGetValue("Sum", out bool sum) && !sum
                      && loaded.TryGetValue("Code", out bool code) && code;
            Console.WriteLine($"写入 3 项 / 读取 {loaded.Count} 项，Sum={loaded.GetValueOrDefault("Sum")}，Code={loaded.GetValueOrDefault("Code")}");
            Console.WriteLine(ok ? "列设置存取测试通过" : "列设置存取测试失败");
            return ok ? 0 : 1;
        }

        // 命令行测试模式：WelfareLottery --grid-test  绑定各结果集，诊断列类型与单元格格式化
        if (args.Contains("--grid-test", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            using var host = new Form();
            var grid = new DataGridView { AutoGenerateColumns = true };
            host.Controls.Add(grid);

            string current = string.Empty;
            grid.DataError += (_, e) =>
            {
                Console.WriteLine($"  [DataError] {current}：{e.Exception?.GetType().Name}: {e.Exception?.Message}");
                e.ThrowException = false;
            };

            void Bind(DataTable table, string name)
            {
                current = name;
                grid.DataSource = table;
                Console.WriteLine($"[{name}] {table.Rows.Count} 行，列类型：" + string.Join(", ",
                    grid.Columns.Cast<DataGridViewColumn>().Select(c => $"{c.Name}={c.ValueType?.Name}")));

                int rows = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        try
                        {
                            _ = cell.FormattedValue;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  列 {cell.OwningColumn?.Name}（ValueType={cell.ValueType?.Name}，" +
                                              $"值类型={cell.Value?.GetType().Name}）格式化失败：{ex.GetType().Name}: {ex.Message}");
                        }
                    }
                    if (++rows >= 3) break;
                }
            }

            try
            {
                if (!EnsureLotteryDataAsync().GetAwaiter().GetResult()) return 1;

                var condition = new HistoryAnalysisService.Condition();
                Bind(HistoryAnalysisService.FindDuplicates(condition), "重复开奖号码");
                Bind(HistoryAnalysisService.FindDuplicates(new HistoryAnalysisService.Condition { SameRedOnly = true }), "红球重复");
                Bind(HistoryAnalysisService.FindSimilarPairs(4, condition), "相似组合");
                Bind(HistoryAnalysisService.FindBlueSamePairs(condition), "蓝球相同红球不同");
                Bind(LotteryService.GetDataTable(), "开奖数据");
                var (combo, _) = CombinationService.QueryPageData(0, 20);
                Bind(combo, "组合分页");
                Console.WriteLine("列类型与格式化诊断完成");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        // 命令行测试模式：WelfareLottery --ui-smoke  构造全部窗体，验证控件初始化无异常
        if (args.Contains("--ui-smoke", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();

            int exitCode = 0;
            var host = new Form
            {
                ShowInTaskbar = false,
                WindowState = FormWindowState.Minimized,
                Opacity = 0
            };
            host.Shown += (_, __) =>
            {
                try
                {
                    using var main = new MainForm();
                    using var combo = new CombinationForm();
                    using var settings = new ColumnSettingForm("测试", new List<(string key, string text, bool visible)>
                    {
                        ("Code", "期号", true), ("Blue", "蓝球", false)
                    });
                    Console.WriteLine("窗体构造测试通过：MainForm / CombinationForm / ColumnSettingForm");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    exitCode = 1;
                }
                finally
                {
                    host.Close();
                }
            };

            Application.Run(host);
            return exitCode;
        }

        // 命令行维护：WelfareLottery --drop-legacy  删除数据库里旧版本的开奖/组合表并压缩
        if (args.Contains("--drop-legacy", StringComparer.OrdinalIgnoreCase))
        {
            long before = CombinationService.GetDatabaseSize();
            bool droppedCombo = CombinationService.DropLegacyTable();
            bool droppedDraws = LotteryService.PurgeLegacyTable();
            Console.WriteLine(droppedCombo ? "已删除数据库中的旧组合表（组合数据现只存内存）" : "数据库中没有旧组合表");
            Console.WriteLine(droppedDraws ? "已删除数据库中的旧开奖表（开奖数据现只存内存）" : "数据库中没有旧开奖表");
            if (droppedCombo || droppedDraws)
            {
                CombinationService.Vacuum();
                long after = CombinationService.GetDatabaseSize();
                Console.WriteLine($"数据库：{before / 1048576.0:F1} MB -> {after / 1048576.0:F1} MB，释放 {(before - after) / 1048576.0:F1} MB");
            }
            return 0;
        }

        // 命令行测试模式：WelfareLottery --filter-test  红球连续形态过滤验证
        if (args.Contains("--filter-test", StringComparer.OrdinalIgnoreCase))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long total = CombinationService.GenerateAllAsync().GetAwaiter().GetResult();
            Console.WriteLine($"生成 {total:N0} 条，耗时 {sw.ElapsedMilliseconds} ms");

            bool ok = true;
            var cases = new (string name, RunFilter filter, int minRun)[]
            {
                ("排除三连号", RunFilter.Exclude3, 3),
                ("排除四连号", RunFilter.Exclude4, 4),
                ("排除五连号", RunFilter.Exclude5, 5),
                ("排除三连+四连", RunFilter.Exclude3 | RunFilter.Exclude4, 3),
                ("排除四连+五连", RunFilter.Exclude4 | RunFilter.Exclude5, 4)
            };

            foreach (var (name, filter, minRun) in cases)
            {
                sw.Restart();
                long kept = CombinationService.ApplyRunFilter(filter);
                long elapsed = sw.ElapsedMilliseconds;
                long expected = total - CountRedWithRun(minRun, 33) * CombinationService.BlueCount;
                bool match = kept == expected;
                ok &= match;
                Console.WriteLine($"{name,-12} 剩余 {kept,12:N0}（暴力统计 {expected,12:N0}），排除 {total - kept,10:N0}，耗时 {elapsed,4} ms，{(match ? "一致" : "不一致")}");
            }

            // 分页结果确实不含被排除形态
            long kept3 = CombinationService.ApplyRunFilter(RunFilter.Exclude3);
            var (page, pageTotal) = CombinationService.QueryPageData(0, 100);
            bool pageOk = pageTotal == kept3 && page.Rows.Count > 0 &&
                          page.Rows.Cast<DataRow>().All(r => MaxRunRow(r) < 3);
            ok &= pageOk;
            Console.WriteLine($"分页校验：共 {pageTotal:N0} 条，首页 {page.Rows.Count} 行，" +
                              (pageOk ? "均不含三连号" : "存在三连号（失败）"));

            // 精确查询受过滤影响
            var (blocked, exact, _) = CombinationService.QueryByNumbers(new[] { 1, 2, 3, 4, 5, 6 }, 1);
            CombinationService.ApplyRunFilter(RunFilter.None);
            var (allowed, _, _) = CombinationService.QueryByNumbers(new[] { 1, 2, 3, 4, 5, 6 }, 1);
            bool queryOk = exact && blocked.Rows.Count == 0 && allowed.Rows.Count == 1;
            ok &= queryOk;
            Console.WriteLine($"精确查询：过滤时 {blocked.Rows.Count} 条，清空过滤后 {allowed.Rows.Count} 条，" +
                              (queryOk ? "符合预期" : "不符合预期"));

            Console.WriteLine(ok ? "红球形态过滤校验通过" : "红球形态过滤校验失败");
            return ok ? 0 : 1;

            // 暴力统计：6 个红球中包含 k 连号的组合数（独立实现，用于对照）
            static long CountRedWithRun(int k, int redMax)
            {
                long count = 0;
                var reds = new int[6];
                for (int r1 = 1; r1 <= redMax - 5; r1++)
                for (int r2 = r1 + 1; r2 <= redMax - 4; r2++)
                for (int r3 = r2 + 1; r3 <= redMax - 3; r3++)
                for (int r4 = r3 + 1; r4 <= redMax - 2; r4++)
                for (int r5 = r4 + 1; r5 <= redMax - 1; r5++)
                for (int r6 = r5 + 1; r6 <= redMax; r6++)
                {
                    reds[0] = r1; reds[1] = r2; reds[2] = r3;
                    reds[3] = r4; reds[4] = r5; reds[5] = r6;
                    for (int start = 0; start + k <= 6; start++)
                    {
                        bool consecutive = true;
                        for (int i = 1; i < k; i++)
                        {
                            if (reds[start + i] != reds[start + i - 1] + 1) { consecutive = false; break; }
                        }
                        if (consecutive) { count++; break; }
                    }
                }
                return count;
            }

            static int MaxRunRow(DataRow r)
            {
                int max = 1, run = 1;
                for (int i = 1; i < 6; i++)
                {
                    run = Convert.ToInt32(r[i]) == Convert.ToInt32(r[i - 1]) + 1 ? run + 1 : 1;
                    if (run > max) max = run;
                }
                return max;
            }
        }

        // 命令行测试模式：WelfareLottery --query-test  号码查询验证（内存数据）
        if (args.Contains("--query-test", StringComparer.OrdinalIgnoreCase))
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long generated = CombinationService.GenerateAllAsync().GetAwaiter().GetResult();
            Console.WriteLine($"内存中生成 {generated:N0} 条，耗时 {sw.ElapsedMilliseconds} ms");

            // 1) 6 红 + 蓝球：精确一条
            sw.Restart();
            var (exactOne, exactFlag, _) = CombinationService.QueryByNumbers(new[] { 1, 2, 3, 4, 5, 6 }, 7);
            Console.WriteLine($"精确（1-6 + 蓝 7）：{exactOne.Rows.Count} 条，exact={exactFlag}，耗时 {sw.ElapsedMilliseconds} ms -> " +
                              (exactOne.Rows.Count == 1 ? RowText(exactOne.Rows[0]) : "无"));

            // 2) 6 红不限蓝球：16 条
            sw.Restart();
            var (exactAll, _, _) = CombinationService.QueryByNumbers(new[] { 1, 2, 3, 4, 5, 6 }, 0);
            Console.WriteLine($"精确（1-6，蓝不限）：{exactAll.Rows.Count} 条，耗时 {sw.ElapsedMilliseconds} ms");

            // 3) 3 个红球包含匹配
            sw.Restart();
            var (partial, _, truncated) = CombinationService.QueryByNumbers(new[] { 1, 2, 3 }, 0);
            Console.WriteLine($"包含（红 1,2,3）：{partial.Rows.Count} 条，truncated={truncated}，耗时 {sw.ElapsedMilliseconds} ms");

            // 4) 与 GetKeyAt 一致性
            var (k1, k2, k3, k4, k5, k6, kb) = CombinationService.GetKeyAt(17_000_000);
            var (byKey, _, _) = CombinationService.QueryByNumbers(new[] { k1, k2, k3, k4, k5, k6 }, kb);
            bool ok = exactOne.Rows.Count == 1
                      && RowText(exactOne.Rows[0]) == "1 2 3 4 5 6 | 7"
                      && exactAll.Rows.Count == 16
                      && byKey.Rows.Count == 1
                      && RowText(byKey.Rows[0]) == $"{k1} {k2} {k3} {k4} {k5} {k6} | {kb}";
            Console.WriteLine(ok ? "号码查询校验通过" : "号码查询校验失败");
            return ok ? 0 : 1;

            static string RowText(DataRow r) =>
                $"{r["Red1"]} {r["Red2"]} {r["Red3"]} {r["Red4"]} {r["Red5"]} {r["Red6"]} | {r["Blue"]}";
        }

        // 命令行测试模式：WelfareLottery --page-test  分页查询耗时验证（内存数据）
        if (args.Contains("--page-test", StringComparer.OrdinalIgnoreCase))
        {
            const int pageSize = 200;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long generated = CombinationService.GenerateAllAsync().GetAwaiter().GetResult();
            Console.WriteLine($"内存中生成 {generated:N0} 条组合，耗时 {sw.ElapsedMilliseconds} ms，占用 {CombinationService.MemoryBytes / 1024.0 / 1024.0:F0} MB");

            sw.Restart();
            var (first, total) = CombinationService.QueryPageData(0, pageSize);
            Console.WriteLine($"首页 {first.Rows.Count} 行，耗时 {sw.ElapsedMilliseconds} ms，总数 {total:N0}");
            if (total == 0)
            {
                Console.WriteLine("内存中没有组合数据");
                return 1;
            }

            int maxPage = (int)((total + pageSize - 1) / pageSize);
            int midPage = Math.Min(100, maxPage - 1);

            sw.Restart();
            var (mid, _) = CombinationService.QueryPageData(midPage, pageSize);
            Console.WriteLine($"第 {midPage + 1} 页 {mid.Rows.Count} 行，耗时 {sw.ElapsedMilliseconds} ms，首行 {RowText(mid.Rows[0])}，期望 {Expected(midPage * pageSize)}");

            sw.Restart();
            var (deep, _) = CombinationService.QueryPageData(maxPage - 1, pageSize);
            Console.WriteLine($"第 {maxPage} 页 {deep.Rows.Count} 行，耗时 {sw.ElapsedMilliseconds} ms，首行 {RowText(deep.Rows[0])}，期望 {Expected((long)(maxPage - 1) * pageSize)}");

            bool ok = RowText(mid.Rows[0]) == Expected(midPage * pageSize)
                      && deep.Rows.Count > 0
                      && RowText(deep.Rows[0]) == Expected((long)(maxPage - 1) * pageSize);
            Console.WriteLine(ok ? "任意页码定位校验通过" : "定位校验失败");
            return ok ? 0 : 1;

            static string RowText(DataRow r) =>
                $"{r["Red1"]} {r["Red2"]} {r["Red3"]} {r["Red4"]} {r["Red5"]} {r["Red6"]} | {r["Blue"]}";

            static string Expected(long rowRank)
            {
                var (k1, k2, k3, k4, k5, k6, kb) = CombinationService.GetKeyAt(rowRank);
                return $"{k1} {k2} {k3} {k4} {k5} {k6} | {kb}";
            }
        }

        // 命令行测试模式：WelfareLottery --history-test  历史数据重复号码分析验证（内存数据）
        if (args.Contains("--history-test", StringComparer.OrdinalIgnoreCase))
        {
            if (!EnsureLotteryDataAsync().GetAwaiter().GetResult()) return 1;

            var groups = HistoryAnalysisService.FindDuplicates(new HistoryAnalysisService.Condition());
            var (groupCount, drawCount) = HistoryAnalysisService.Summarize(groups);
            Console.WriteLine($"重复开奖号码（红球+蓝球完全相同）：{groupCount} 组，涉及 {drawCount} 期");
            foreach (DataRow r in groups.Rows.Cast<DataRow>().Take(5))
            {
                Console.WriteLine($"  {r["红1"]} {r["红2"]} {r["红3"]} {r["红4"]} {r["红5"]} {r["红6"]} | {r["蓝球"]}" +
                                  $"  出现 {r["出现次数"]} 次：{r["期号"]}");
            }

            var redOnly = HistoryAnalysisService.FindDuplicates(new HistoryAnalysisService.Condition { SameRedOnly = true });
            var (redGroups, redDraws) = HistoryAnalysisService.Summarize(redOnly);
            Console.WriteLine($"红球重复（蓝球不限）：{redGroups} 组，涉及 {redDraws} 期");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var similar = HistoryAnalysisService.FindSimilarPairs(4, new HistoryAnalysisService.Condition());
            Console.WriteLine($"相似组合（红球相同 ≥ 4）：{similar.Rows.Count} 对，耗时 {sw.ElapsedMilliseconds} ms");
            if (similar.Rows.Count > 0)
            {
                var r = similar.Rows[0];
                Console.WriteLine($"  例：{r["期号1"]}（{r["红球1"]}）↔ {r["期号2"]}（{r["红球2"]}）命中 {r["命中红球"]} 个");
            }

            sw.Restart();
            var blueSame = HistoryAnalysisService.FindBlueSamePairs(new HistoryAnalysisService.Condition());
            Console.WriteLine($"蓝球相同、红球完全不同：{blueSame.Rows.Count} 对，耗时 {sw.ElapsedMilliseconds} ms");
            if (blueSame.Rows.Count > 0)
            {
                var r = blueSame.Rows[0];
                Console.WriteLine($"  例：{r["期号1"]}（{r["红球1"]} | {r["蓝球"]}）↔ {r["期号2"]}（{r["红球2"]}）");
            }
            return 0;
        }

        // 命令行测试模式：WelfareLottery --combo-test [redMax]  内存生成组合 -> 分页排序查询验证
        if (args.Contains("--combo-test", StringComparer.OrdinalIgnoreCase))
        {
            int redMax = 33;
            var arg = args.FirstOrDefault(a => int.TryParse(a, out _));
            if (arg != null) redMax = int.Parse(arg);
            return RunComboTestAsync(redMax).GetAwaiter().GetResult();
        }

        // 开奖/组合数据已改为只存内存：启动时清理旧版本遗留在数据库里的表（DROP 很快，空间可用「压缩数据库」回收）
        try
        {
            CombinationService.DropLegacyTable();
            LotteryService.PurgeLegacyTable();
            LotteryService.Clear();
        }
        catch { /* 数据库未就绪时忽略 */ }

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    static async Task<int> RunComboTestAsync(int redMax)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var progress = new Progress<string>(msg => Console.Write("\r  " + msg));
            long generated = await CombinationService.GenerateAllAsync(progress, redMax: redMax);
            sw.Stop();
            Console.WriteLine();
            Console.WriteLine($"生成 {redMax} 红球组合：内存 {generated:N0} 条，耗时 {sw.Elapsed.TotalSeconds:F2} 秒，占用 {CombinationService.MemoryBytes / 1024.0 / 1024.0:F0} MB（未写数据库）");

            long total = CombinationService.GetCount();
            Console.WriteLine($"内存组合总数：{total:N0}");

            var page = CombinationService.QueryPage(0, 5, out long rows);
            Console.WriteLine($"排序查询前 5 条（共 {rows:N0} 条）：");
            foreach (DataRow r in page.Rows)
                Console.WriteLine($"  {r["Red1"],2} {r["Red2"],2} {r["Red3"],2} {r["Red4"],2} {r["Red5"],2} {r["Red6"],2} | {r["Blue"],2}");

            long expected = redMax == 33 ? CombinationService.TotalCombinations : -1;
            bool ok = expected < 0 || total >= expected;
            Console.WriteLine(ok ? "测试通过" : $"测试失败：期望至少 {expected:N0} 条");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>确保内存中已有开奖数据（命令行自检用：没有则下载全部年份）</summary>
    static async Task<bool> EnsureLotteryDataAsync()
    {
        if (LotteryService.HasData) return true;

        Console.WriteLine("内存中暂无开奖数据，正在下载历年数据 ...");
        var progress = new Progress<string>(msg => Console.WriteLine("  " + msg));
        var (table, skipped) = await LotteryService.DownloadAllYearsAsync(progress: progress);
        if (table.Rows.Count == 0)
        {
            Console.WriteLine("下载失败：没有获取到任何一期数据（请检查网络）。");
            return false;
        }

        var (added, updated) = LotteryService.Merge(table);
        Console.WriteLine($"已加载 {LotteryService.Count:N0} 期到内存（新增 {added}，更新 {updated}，跳过 {skipped.Count} 个年份）");
        return true;
    }

    static async Task<int> RunTestAsync()
    {
        try
        {
            int startYear = LotteryService.StartYear;
            int endYear = LotteryService.EndYear;
            Console.WriteLine($"按年份循环获取：{startYear} - {endYear}");

            var progress = new Progress<string>(msg => Console.WriteLine("  " + msg));
            var (table, skipped) = await LotteryService.DownloadAllYearsAsync(startYear, endYear, progress);
            Console.WriteLine($"DataTable 列数 {table.Columns.Count}，行数 {table.Rows.Count}");
            if (table.Rows.Count == 0) return 1;

            var first = table.Rows[0];
            Console.WriteLine($"首行：{first["Year"]} 年 {first["Code"]}，红球 {first["Red1"]}-{first["Blue"]}，和值 {first["Sum"]}");
            Console.WriteLine($"跳过年份：{(skipped.Count == 0 ? "无" : string.Join(",", skipped))}");

            var (inserted, updated) = LotteryService.Merge(table);
            Console.WriteLine($"合并到内存：新增 {inserted}，更新 {updated}，内存共 {LotteryService.Count:N0} 期");

            var stats = LotteryService.GetStatistics();
            var loaded = LotteryService.GetDataTable();
            Console.WriteLine($"内存回读：{loaded.Rows.Count} 行，{stats.minYear}-{stats.maxYear} 年，路径无关（不写数据库）");

            var lastRow = loaded.Rows[loaded.Rows.Count - 1];
            bool firstRowOk = loaded.Rows.Count > 0
                              && Convert.ToInt32(loaded.Rows[0]["Year"]) >= stats.minYear
                              && string.CompareOrdinal(Convert.ToString(loaded.Rows[0]["Code"]),
                                                       Convert.ToString(lastRow["Code"])) > 0;
            bool ok = loaded.Rows.Count == LotteryService.Count && firstRowOk;
            Console.WriteLine(ok ? "测试通过：采集数据已在内存中，未写数据库" : "测试失败：内存数据异常");
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}