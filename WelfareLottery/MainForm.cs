namespace WelfareLottery
{
    /// <summary>
    /// 主界面：功能导航 + 数据概览
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly Panel _header;
        private readonly Label _lblTitle;
        private readonly Label _lblSubTitle;

        private readonly Button _btnCollect;
        private readonly Button _btnCombination;
        private readonly Button _btnHistory;
        private readonly Button _btnRefresh;
        private readonly Button _btnVacuum;

        private readonly Label _lblLotteryStats;
        private readonly Label _lblCombinationStats;
        private readonly Label _lblDbPath;
        private readonly Label _lblStatus;

        public MainForm()
        {
            Text = "双色球数据工具 - 主界面";
            Size = new Size(820, 560);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 250);

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = Color.FromArgb(32, 86, 168)
            };

            _lblTitle = new Label
            {
                Text = "双色球数据工具",
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 18),
                AutoSize = true
            };

            _lblSubTitle = new Label
            {
                Text = "开奖数据按年份采集 · 全部组合生成与查询",
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Color.FromArgb(210, 225, 245),
                Location = new Point(26, 58),
                AutoSize = true
            };

            _header.Controls.Add(_lblTitle);
            _header.Controls.Add(_lblSubTitle);

            _btnCollect = CreateNavButton("开奖数据采集", "按年份下载到内存（不写数据库）", 24, 120);
            _btnCollect.Click += (_, __) => OpenChild(new DrawDataCollection());

            _btnCombination = CreateNavButton("全部组合查询", "左：内存生成全部组合并排序查询；右：历史开奖数据采集", 24, 196);
            _btnCombination.Click += (_, __) => OpenChild(new CombinationForm());

            _btnHistory = CreateNavButton("历史数据分析", "查找重复开奖号码、按号码筛选历史记录", 24, 272);
            _btnHistory.Click += (_, __) => OpenChild(new HistoryDataAnalysis());

            var statsBox = new GroupBox
            {
                Text = " 数据概览 ",
                Location = new Point(384, 120),
                Size = new Size(400, 216),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White
            };

            _lblLotteryStats = new Label
            {
                Text = "开奖数据：读取中 ...",
                Location = new Point(18, 34),
                Size = new Size(360, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _lblCombinationStats = new Label
            {
                Text = "组合数据：读取中 ...",
                Location = new Point(18, 66),
                Size = new Size(360, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _lblDbPath = new Label
            {
                Text = "数据库：",
                Location = new Point(18, 102),
                Size = new Size(360, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.DimGray
            };

            _btnRefresh = new Button
            {
                Text = "刷新统计",
                Location = new Point(18, 170),
                Size = new Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(240, 244, 250)
            };
            _btnRefresh.Click += async (_, __) => await RefreshStatisticsAsync();

            _btnVacuum = new Button
            {
                Text = "压缩数据库",
                Location = new Point(126, 170),
                Size = new Size(100, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(240, 244, 250)
            };
            _btnVacuum.Click += async (_, __) => await VacuumDatabaseAsync();

            statsBox.Controls.Add(_lblLotteryStats);
            statsBox.Controls.Add(_lblCombinationStats);
            statsBox.Controls.Add(_lblDbPath);
            statsBox.Controls.Add(_btnRefresh);
            statsBox.Controls.Add(_btnVacuum);

            _lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(24, 356),
                Size = new Size(760, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.DimGray
            };

            var tip = new Label
            {
                Text = "提示：先执行\"开奖数据采集\"下载历年开奖记录（只在内存中，不写数据库），再在\"全部组合查询\"中初始化全部组合（C(33,6)×16 = 17,721,088 条，约 1 秒）。",
                Location = new Point(24, 392),
                Size = new Size(760, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.Gray
            };

            Controls.Add(_header);
            Controls.Add(_btnCollect);
            Controls.Add(_btnCombination);
            Controls.Add(_btnHistory);
            Controls.Add(statsBox);
            Controls.Add(_lblStatus);
            Controls.Add(tip);

            Load += async (_, __) => await RefreshStatisticsAsync();
        }

        private Button CreateNavButton(string text, string tooltip, int x, int y)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(340, 62),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(32, 86, 168),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(214, 222, 234);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 241, 253);

            var tip = new ToolTip();
            tip.SetToolTip(button, tooltip);
            return button;
        }

        private void OpenChild(Form child)
        {
            using (child)
            {
                Hide();
                child.ShowDialog();
                Show();
            }
            _ = RefreshStatisticsAsync();
        }

        private async Task RefreshStatisticsAsync()
        {
            _btnRefresh.Enabled = false;
            _lblStatus.Text = "正在统计 ...";

            try
            {
                var (lotteryCount, minYear, maxYear) = await Task.Run(() => LotteryService.GetStatistics());
                long comboCount = await Task.Run(() => CombinationService.GetCount());

                _lblLotteryStats.Text = lotteryCount == 0
                    ? "开奖数据：内存中暂无（请先执行开奖数据采集）"
                    : $"开奖数据：内存 {lotteryCount:N0} 期（{minYear} - {maxYear} 年）";

                double percent = comboCount * 100.0 / CombinationService.TotalCombinations;
                _lblCombinationStats.Text = comboCount == 0
                    ? $"组合数据：内存中暂无（共应 {CombinationService.TotalCombinations:N0} 条，打开组合查询后生成）"
                    : $"组合数据：内存 {comboCount:N0} 条（占全部 {CombinationService.TotalCombinations:N0} 条的 {percent:F2}%，约 {FormatSize(CombinationService.MemoryBytes)}）";

                long size = await Task.Run(() => CombinationService.GetDatabaseSize());
                _lblDbPath.Text = $"开奖/组合数据仅驻留内存（{FormatSize(LotteryService.MemoryBytes + CombinationService.MemoryBytes)}）；\n" +
                                  $"设置库：{LotteryService.DbPath}（{FormatSize(size)}）";
                _lblStatus.Text = $"统计完成 {DateTime.Now:HH:mm:ss}";
                _lblStatus.ForeColor = Color.DimGray;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "统计失败：" + ex.Message;
                _lblStatus.ForeColor = Color.Red;
            }
            finally
            {
                _btnRefresh.Enabled = true;
            }
        }

        /// <summary>回收磁盘空间（VACUUM），完成后刷新统计并显示释放量</summary>
        private async Task VacuumDatabaseAsync()
        {
            var confirm = MessageBox.Show(
                "压缩数据库会重建数据库文件（VACUUM），期间需要额外的临时磁盘空间，耗时取决于数据量。\n\n是否继续？",
                "压缩数据库", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            _btnVacuum.Enabled = false;
            _btnRefresh.Enabled = false;
            UseWaitCursor = true;
            _lblStatus.Text = "正在压缩数据库，请稍候 ...";
            _lblStatus.ForeColor = Color.DimGray;

            try
            {
                long before = await Task.Run(() => CombinationService.GetDatabaseSize());
                await Task.Run(() => CombinationService.Vacuum());
                long after = await Task.Run(() => CombinationService.GetDatabaseSize());
                CombinationService.InvalidateCount();

                await RefreshStatisticsAsync();
                _lblStatus.Text = $"压缩完成：{FormatSize(before)} → {FormatSize(after)}，释放 {FormatSize(Math.Max(0, before - after))}";
                _lblStatus.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "压缩失败：" + ex.Message;
                _lblStatus.ForeColor = Color.Red;
                MessageBox.Show(ex.ToString(), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnVacuum.Enabled = true;
                _btnRefresh.Enabled = true;
                UseWaitCursor = false;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return $"{value:0.##} {units[unit]}";
        }
    }
}
