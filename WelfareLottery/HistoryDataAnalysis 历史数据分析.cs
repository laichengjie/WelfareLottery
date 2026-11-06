using System.Data;

namespace WelfareLottery
{
    /// <summary>
    /// 历史数据分析：勾选条件后分析 DoubleColorBall 中的重复号码与相似组合。
    /// </summary>
    public partial class HistoryDataAnalysis : Form
    {
        private const string ColumnSettingKey = nameof(HistoryDataAnalysis);

        private enum AnalysisMode
        {
            DuplicateAll = 0,     // 重复开奖号码（红球 + 蓝球完全相同）
            DuplicateRed = 1,     // 红球重复（蓝球可不同）
            SimilarPairs = 2,     // 相似组合（红球命中 >= N）
            BlueSameRedDiff = 3   // 蓝球相同、红球完全不同
        }

        private readonly CheckedListBox _conditions;
        private readonly NumericUpDown _numYearFrom;
        private readonly NumericUpDown _numYearTo;
        private readonly NumericUpDown _numSumMin;
        private readonly NumericUpDown _numSumMax;
        private readonly NumericUpDown _numBlue;
        private readonly NumericUpDown _numMinHit;
        private readonly Button _btnAnalyze;
        private readonly Button _btnClear;
        private readonly Button _btnSettings;
        private readonly Label _status;
        private readonly DataGridView _grid;
        private readonly DataGridView _detailGrid;
        private readonly Label _detailTitle;

        public HistoryDataAnalysis()
        {
            Text = "历史数据分析";
            Size = new Size(1000, 740);
            MinimumSize = new Size(900, 640);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);

            var conditionBox = new GroupBox
            {
                Text = " 分析条件 ",
                Location = new Point(12, 12),
                Size = new Size(440, 268),
                BackColor = Color.White
            };

            var lblTip = new Label
            {
                Text = "勾选一种分析类型（单选）：",
                Location = new Point(14, 24),
                AutoSize = true
            };

            _conditions = new CheckedListBox
            {
                Location = new Point(14, 48),
                Size = new Size(410, 106),
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle
            };
            _conditions.Items.Add("重复开奖号码（6 个红球 + 蓝球完全相同）", true);
            _conditions.Items.Add("红球重复（6 个红球相同，蓝球可不同）", false);
            _conditions.Items.Add("相似组合（两期红球相同个数 ≥ N）", false);
            _conditions.Items.Add("蓝球相同、红球完全不同", false);
            _conditions.ItemCheck += OnItemCheckSingleSelect;

            var lblYear = new Label { Text = "年份", Location = new Point(14, 164), AutoSize = true };
            _numYearFrom = CreateNumber(60, 160, 70, 0, 2100, "起始年份（0 = 不限）");
            var lblYearTo = new Label { Text = "至", Location = new Point(136, 164), AutoSize = true };
            _numYearTo = CreateNumber(156, 160, 70, 0, 2100, "结束年份（0 = 不限）");

            var lblSum = new Label { Text = "和值", Location = new Point(14, 198), AutoSize = true };
            _numSumMin = CreateNumber(60, 194, 70, 0, 200, "和值下限（0 = 不限）");
            var lblSumTo = new Label { Text = "至", Location = new Point(136, 198), AutoSize = true };
            _numSumMax = CreateNumber(156, 194, 70, 0, 200, "和值上限（0 = 不限）");

            var lblBlue = new Label { Text = "蓝球", Location = new Point(246, 164), AutoSize = true };
            _numBlue = CreateNumber(292, 160, 60, 0, 16, "蓝球（0 = 不限）");

            var lblHit = new Label { Text = "相似≥", Location = new Point(246, 198), AutoSize = true };
            _numMinHit = CreateNumber(292, 194, 56, 3, 6, "相似组合中要求相同的红球个数（3-6）");
            _numMinHit.Value = 4;

            _btnAnalyze = new Button
            {
                Text = "开始分析",
                Location = new Point(246, 228),
                Size = new Size(96, 30)
            };
            _btnAnalyze.Click += async (_, __) => await AnalyzeAsync();

            _btnClear = new Button
            {
                Text = "清空",
                Location = new Point(350, 228),
                Size = new Size(74, 30)
            };
            _btnClear.Click += (_, __) => ClearResult();

            _btnSettings = new Button
            {
                Text = "设置",
                Location = new Point(904, 12),
                Size = new Size(80, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnSettings.Click += (_, __) => OpenColumnSettings();

            conditionBox.Controls.AddRange(new Control[]
            {
                lblTip, _conditions, lblYear, _numYearFrom, lblYearTo, _numYearTo,
                lblSum, _numSumMin, lblSumTo, _numSumMax, lblBlue, _numBlue,
                lblHit, _numMinHit, _btnAnalyze, _btnClear
            });

            _status = new Label
            {
                Text = "勾选分析类型后点击\"开始分析\"：完全重复通常为 0 组，可试试\"红球重复\"或\"相似组合\"。",
                Location = new Point(12, 290),
                Size = new Size(960, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.DimGray
            };

            _grid = new DataGridView
            {
                Location = new Point(12, 318),
                Size = new Size(976, 206),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidth = 40,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };
            _grid.CellDoubleClick += (_, e) => ShowDetail(e.RowIndex);
            _grid.DataBindingComplete += (_, __) =>
            {
                ApplyColumnSettings();
                ApplyColumnWidths();
            };
            // 兜底：不同结果集列类型不一致时不要弹默认错误框
            _grid.DataError += (_, e) => e.ThrowException = false;

            _detailTitle = new Label
            {
                Text = "明细：双击上方任意一行查看相关开奖记录",
                Location = new Point(12, 532),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _detailGrid = new DataGridView
            {
                Location = new Point(12, 558),
                Size = new Size(976, 140),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidth = 40,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _detailGrid.DataError += (_, e) => e.ThrowException = false;

            Controls.AddRange(new Control[]
            {
                conditionBox, _btnSettings, _status, _grid, _detailTitle, _detailGrid
            });
        }

        private NumericUpDown CreateNumber(int x, int y, int width, int min, int max, string tip)
        {
            var num = new NumericUpDown
            {
                Location = new Point(x, y),
                Size = new Size(width, 26),
                Minimum = min,
                Maximum = max,
                // 初值必须落在 [Minimum, Maximum] 内（如“相似≥”的 Minimum 为 3，不能赋 0）
                Value = Math.Clamp(0, min, max)
            };
            new ToolTip().SetToolTip(num, tip);
            return num;
        }

        /// <summary>勾选时自动取消其他项（单选语义）</summary>
        private void OnItemCheckSingleSelect(object? sender, ItemCheckEventArgs e)
        {
            if (e.NewValue != CheckState.Checked) return;

            for (int i = 0; i < _conditions.Items.Count; i++)
            {
                if (i == e.Index) continue;
                if (_conditions.GetItemChecked(i)) _conditions.SetItemChecked(i, false);
            }
        }

        private AnalysisMode? GetCheckedMode()
        {
            for (int i = 0; i < _conditions.Items.Count; i++)
            {
                if (_conditions.GetItemChecked(i)) return (AnalysisMode)i;
            }
            return null;
        }

        /// <summary>执行分析（后台线程查询，避免界面卡顿）</summary>
        private async Task AnalyzeAsync()
        {
            var mode = GetCheckedMode();
            if (mode == null)
            {
                MessageBox.Show("请先勾选一种分析类型。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool sameRedOnly = mode == AnalysisMode.DuplicateRed;
            var condition = new HistoryAnalysisService.Condition
            {
                SameRedOnly = sameRedOnly,
                YearFrom = (int)_numYearFrom.Value,
                YearTo = (int)_numYearTo.Value,
                SumMin = (int)_numSumMin.Value,
                SumMax = (int)_numSumMax.Value,
                Blue = (int)_numBlue.Value
            };

            _btnAnalyze.Enabled = false;
            _btnClear.Enabled = false;
            UseWaitCursor = true;
            _status.Text = "正在分析历史开奖数据 ...";
            _status.ForeColor = Color.DimGray;

            try
            {
                var selected = mode.Value;
                int minHit = (int)_numMinHit.Value;
                var (table, caption) = await Task.Run(() =>
                {
                    switch (selected)
                    {
                        case AnalysisMode.DuplicateAll:
                            return (HistoryAnalysisService.FindDuplicates(condition), "重复开奖号码（红球+蓝球完全相同）");
                        case AnalysisMode.DuplicateRed:
                            return (HistoryAnalysisService.FindDuplicates(new HistoryAnalysisService.Condition
                            {
                                SameRedOnly = true,
                                YearFrom = condition.YearFrom,
                                YearTo = condition.YearTo,
                                SumMin = condition.SumMin,
                                SumMax = condition.SumMax,
                                Blue = condition.Blue
                            }), "红球重复（蓝球不限）");
                        case AnalysisMode.SimilarPairs:
                            return (HistoryAnalysisService.FindSimilarPairs(minHit, condition), $"相似组合（红球相同 ≥ {minHit}）");
                        default:
                            return (HistoryAnalysisService.FindBlueSamePairs(condition), "蓝球相同、红球完全不同");
                    }
                });

                BindTable(_grid, table);
                BindTable(_detailGrid, null);

                bool isPairMode = selected == AnalysisMode.SimilarPairs || selected == AnalysisMode.BlueSameRedDiff;
                if (isPairMode)
                {
                    _status.Text = table.Rows.Count == 0
                        ? $"{caption}：未找到符合条件的组合。"
                        : $"{caption}：显示 {table.Rows.Count:N0} 对（最多 500 对，可按年份/和值/蓝球缩小范围）。双击某行查看明细。";
                }
                else
                {
                    var (groups, draws) = HistoryAnalysisService.Summarize(table);
                    _status.Text = groups == 0
                        ? $"{caption}：未找到出现 2 次及以上的组合。"
                        : $"{caption}：共 {groups:N0} 组，涉及 {draws:N0} 期（按出现次数倒序）。双击某行查看明细。";
                }

                _status.ForeColor = table.Rows.Count == 0 ? Color.Red : Color.DimGray;
            }
            catch (Exception ex)
            {
                _status.Text = "分析失败：" + ex.Message;
                _status.ForeColor = Color.Red;
            }
            finally
            {
                _btnAnalyze.Enabled = true;
                _btnClear.Enabled = true;
                UseWaitCursor = false;
            }
        }

        /// <summary>双击结果行，加载相关开奖记录</summary>
        private void ShowDetail(int rowIndex)
        {
            if (rowIndex < 0 || _grid.Rows.Count <= rowIndex) return;
            var row = _grid.Rows[rowIndex];

            try
            {
                if (_grid.Columns.Contains("期号1"))
                {
                    // 相似组合 / 蓝球相同：展示两期明细
                    var code1 = Convert.ToString(row.Cells["期号1"].Value);
                    var code2 = Convert.ToString(row.Cells["期号2"].Value);
                    var codes = new[] { code1, code2 }
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => c!)
                        .ToArray();
                    var detail = HistoryAnalysisService.LoadDrawsByCodes(codes);
                    BindTable(_detailGrid, detail);
                    _detailTitle.Text = $"明细：{code1} 与 {code2} 的开奖记录（共 {detail.Rows.Count:N0} 期）";
                    return;
                }

                // 重复号码：展示该组合的全部开奖记录
                var reds = new List<int>();
                for (int i = 1; i <= 6; i++)
                {
                    var value = row.Cells[$"红{i}"].Value;
                    if (value != null && int.TryParse(Convert.ToString(value), out int red) && red > 0)
                        reds.Add(red);
                }

                int blue = 0;
                var blueText = Convert.ToString(row.Cells["蓝球"].Value);
                if (int.TryParse(blueText, out int parsedBlue)) blue = parsedBlue;

                var draws = HistoryAnalysisService.LoadDraws(reds.ToArray(), blue);
                BindTable(_detailGrid, draws);
                _detailTitle.Text = $"明细：红球 [{string.Join(",", reds)}]" +
                                    (blue > 0 ? $" 蓝球 [{blue}]" : " 蓝球不限") +
                                    $" —— 共 {draws.Rows.Count:N0} 期";
            }
            catch (Exception ex)
            {
                _status.Text = "加载明细失败：" + ex.Message;
                _status.ForeColor = Color.Red;
            }
        }

        private void ClearResult()
        {
            BindTable(_grid, null);
            BindTable(_detailGrid, null);
            _detailTitle.Text = "明细：双击上方任意一行查看相关开奖记录";
            _status.Text = "已清空结果。";
            _status.ForeColor = Color.DimGray;
        }

        /// <summary>
        /// 绑定结果集：先清空旧列再绑定。
        /// 不同查询的列类型可能不同，复用旧列会导致 DataGridView 用上一次的列类型格式化本次数据
        /// （如空结果被推断成 BLOB → 图片列 → Int64 转 Image 异常）。
        /// </summary>
        private static void BindTable(DataGridView grid, DataTable? table)
        {
            grid.DataSource = null;
            grid.Columns.Clear();
            if (table == null) return;

            grid.AutoGenerateColumns = true;
            grid.DataSource = table;
        }

        /// <summary>为不同结果集设置合适的列宽权重</summary>
        private void ApplyColumnWidths()
        {
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                string name = GetColumnKey(column);
                column.FillWeight = name switch
                {
                    "期号" => 260,
                    "出现次数" => 60,
                    "命中红球" => 70,
                    "红球1" or "红球2" => 150,
                    "期号1" or "期号2" => 100,
                    "蓝球1" or "蓝球2" or "蓝球" => 50,
                    _ => column.FillWeight
                };
            }
        }

        /// <summary>打开列显示设置，保存到 SQLite 并立即应用</summary>
        private void OpenColumnSettings()
        {
            var items = _grid.Columns.Cast<DataGridViewColumn>()
                .OrderBy(c => c.DisplayIndex)
                .Select(c => (key: GetColumnKey(c), text: string.IsNullOrEmpty(c.HeaderText) ? c.Name : c.HeaderText, visible: c.Visible))
                .ToList();
            if (items.Count == 0)
            {
                MessageBox.Show("当前没有可设置的列，请先执行分析。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                return;
            }

            if (settings.Count == 0) return;

            foreach (DataGridViewColumn column in _grid.Columns)
            {
                if (settings.TryGetValue(GetColumnKey(column), out bool visible))
                    column.Visible = visible;
            }
        }

        private static string GetColumnKey(DataGridViewColumn column) =>
            string.IsNullOrEmpty(column.DataPropertyName) ? column.Name : column.DataPropertyName;
    }
}
