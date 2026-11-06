using System.Data;

namespace WelfareLottery
{
    public partial class DrawDataCollection : Form
    {
        private readonly Button _btnFetch;
        private readonly Button _btnLoad;
        private readonly Button _btnCombo;
        private readonly Button _btnSettings;
        private readonly NumericUpDown _numStartYear;
        private readonly NumericUpDown _numEndYear;
        private readonly DataGridView _grid;
        private readonly Label _status;
        private CancellationTokenSource? _cts;

        public DrawDataCollection()
        {
            Text = "开奖数据采集";
            Size = new Size(1000, 660);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);

            _btnFetch = new Button
            {
                Text = "按年份获取到内存",
                Location = new Point(12, 12),
                Size = new Size(220, 32)
            };
            _btnFetch.Click += async (_, __) => await FetchAndSaveAsync();

            var lblStart = new Label
            {
                Text = "起始年",
                Location = new Point(248, 18),
                AutoSize = true
            };

            _numStartYear = new NumericUpDown
            {
                Location = new Point(302, 14),
                Size = new Size(80, 28),
                Minimum = 2000,
                Maximum = DateTime.Now.Year,
                Value = LotteryService.StartYear
            };

            var lblEnd = new Label
            {
                Text = "结束年",
                Location = new Point(396, 18),
                AutoSize = true
            };

            _numEndYear = new NumericUpDown
            {
                Location = new Point(450, 14),
                Size = new Size(80, 28),
                Minimum = 2000,
                Maximum = DateTime.Now.Year,
                Value = LotteryService.EndYear
            };

            _btnLoad = new Button
            {
                Text = "显示内存数据",
                Location = new Point(548, 12),
                Size = new Size(180, 32)
            };
            _btnLoad.Click += (_, __) => LoadLocalData();

            _btnCombo = new Button
            {
                Text = "全部组合查询",
                Location = new Point(740, 12),
                Size = new Size(140, 32)
            };
            _btnCombo.Click += (_, __) =>
            {
                using var form = new CombinationForm();
                form.ShowDialog(this);
            };

            _btnSettings = new Button
            {
                Text = "设置",
                Location = new Point(892, 12),
                Size = new Size(80, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnSettings.Click += (_, __) => OpenColumnSettings();

            _status = new Label
            {
                Text = "就绪",
                Location = new Point(12, 52),
                Size = new Size(960, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.DimGray
            };

            _grid = new DataGridView
            {
                Location = new Point(12, 82),
                Size = new Size(964, 528),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidth = 40,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            Controls.AddRange(new Control[] { _btnFetch, lblStart, _numStartYear, lblEnd, _numEndYear, _btnLoad, _btnCombo, _btnSettings, _status, _grid });

            _grid.DataBindingComplete += (_, __) => ApplyColumnSettings();
            _grid.DataError += (_, e) => e.ThrowException = false;   // 不弹默认错误框

            Load += (_, __) => LoadLocalData();
        }

        /// <summary>列设置在本界面的存储键</summary>
        private const string ColumnSettingKey = nameof(DrawDataCollection);

        /// <summary>打开列显示设置，保存到 SQLite 并立即应用</summary>
        private void OpenColumnSettings()
        {
            var columns = _grid.Columns.Cast<DataGridViewColumn>()
                .OrderBy(c => c.DisplayIndex)
                .Select(c => (key: GetColumnKey(c), text: string.IsNullOrEmpty(c.HeaderText) ? c.Name : c.HeaderText, visible: c.Visible));
            var items = columns.ToList();
            if (items.Count == 0)
            {
                MessageBox.Show("当前没有可设置的列，请先加载数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new ColumnSettingForm(Text + " - 列显示设置", items);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                ColumnSettingService.Save(ColumnSettingKey, dialog.Result);
                ApplyColumnSettings();
                _status.Text = "列显示设置已保存。";
                _status.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存列设置失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>按数据库中保存的设置应用列显示（无设置时全部显示）</summary>
        private void ApplyColumnSettings()
        {
            if (_grid.Columns.Count == 0) return;

            Dictionary<string, bool> settings;
            try
            {
                settings = ColumnSettingService.Load(ColumnSettingKey);
            }
            catch
            {
                return;   // 读取失败时保持默认（全部显示）
            }

            if (settings.Count == 0) return;   // 未设置过 -> 默认全部显示

            foreach (DataGridViewColumn column in _grid.Columns)
            {
                if (settings.TryGetValue(GetColumnKey(column), out bool visible))
                    column.Visible = visible;
            }
        }

        /// <summary>列的唯一标识：优先数据属性名（DataTable 列名）</summary>
        private static string GetColumnKey(DataGridViewColumn column) =>
            string.IsNullOrEmpty(column.DataPropertyName) ? column.Name : column.DataPropertyName;

        private async Task FetchAndSaveAsync()
        {
            int startYear = (int)_numStartYear.Value;
            int endYear = (int)_numEndYear.Value;
            if (startYear > endYear)
            {
                MessageBox.Show("起始年份不能大于结束年份。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            SetBusy(true, $"正在按年份获取 {startYear} - {endYear} 年数据 ...");
            try
            {
                var progress = new Progress<string>(msg =>
                {
                    _status.Text = msg;
                    _status.ForeColor = Color.DimGray;
                });

                var (table, skipped) = await LotteryService.DownloadAllYearsAsync(
                    startYear, endYear, progress, _cts.Token);

                if (table.Rows.Count == 0)
                {
                    _status.Text = "所有年份均未获取到数据。";
                    _status.ForeColor = Color.Red;
                    return;
                }

                SetBusy(true, $"正在把 {table.Rows.Count} 期数据合并到内存 ...");
                var (inserted, updated) = await Task.Run(() => LotteryService.Merge(table));

                _grid.DataSource = LotteryService.GetDataTable();   // 已按 Year DESC, Code DESC 排序
                string skipInfo = skipped.Count == 0 ? string.Empty : $"，跳过无数据年份：{string.Join(",", skipped)}";
                _status.Text = $"{startYear}-{endYear} 年共 {table.Rows.Count} 期：新增 {inserted}，更新 {updated}{skipInfo}；" +
                               $"内存现有 {LotteryService.Count:N0} 期（未写数据库，关闭程序后需重新采集）";
                _status.ForeColor = Color.Green;
            }
            catch (OperationCanceledException)
            {
                _status.Text = "已取消。";
                _status.ForeColor = Color.DimGray;
            }
            catch (Exception ex)
            {
                _status.Text = "失败：" + ex.Message;
                _status.ForeColor = Color.Red;
                MessageBox.Show(ex.ToString(), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false, null);
            }
        }

        private void LoadLocalData()
        {
            try
            {
                var table = LotteryService.GetDataTable();
                _grid.DataSource = table;
                _status.Text = table.Rows.Count == 0
                    ? "内存中暂无开奖数据，请先点击\"按年份获取到内存\"。"
                    : $"已加载内存中的开奖数据，共 {table.Rows.Count:N0} 期（按年份、期号倒序）。";
                _status.ForeColor = Color.DimGray;
            }
            catch (Exception ex)
            {
                _status.Text = "读取失败：" + ex.Message;
                _status.ForeColor = Color.Red;
            }
        }

        private void SetBusy(bool busy, string? message)
        {
            _btnFetch.Enabled = _btnLoad.Enabled = _btnCombo.Enabled = _btnSettings.Enabled = _numStartYear.Enabled = _numEndYear.Enabled = !busy;
            _btnFetch.Text = busy ? "正在获取 ..." : "按年份获取到内存";
            if (message != null)
                _status.Text = message;
        }
    }
}
