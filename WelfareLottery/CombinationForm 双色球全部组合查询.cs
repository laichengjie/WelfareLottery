using System.Data;

namespace WelfareLottery
{
    /// <summary>
    /// 组合查询界面（左右分布）：
    /// 左侧 - 双色球全部组合查询（内存生成、排序分页、号码查询、红球形态过滤，不写数据库）；
    /// 右侧 - 历史开奖数据采集（按年份下载到内存 DataTable，不写数据库）。
    /// </summary>
    public partial class CombinationForm : Form
    {
        // 控件在 BuildLeftPanel / BuildRightPanel 中创建（构造函数内调用，运行时不可能为空）
        // 说明：这些字段在辅助方法中赋值，故不能声明为 readonly（CS0191）
        private SplitContainer _split = null!;

        // ===== 左侧：双色球全部组合查询 =====
        private Button _btnInit = null!;
        private Button _btnCancel = null!;
        private ProgressBar _progress = null!;
        private Label _status = null!;

        private Button _btnSettings = null!;

        private Button _btnPrev = null!;
        private Button _btnNext = null!;
        private NumericUpDown _numPage = null!;
        private ComboBox _cboPageSize = null!;
        private Label _pageInfo = null!;

        private NumericUpDown[] _numReds = null!;
        private NumericUpDown _numBlue = null!;
        private Button _btnSearch = null!;
        private Button _btnBackToPages = null!;

        private CheckBox _chkRun3 = null!;
        private CheckBox _chkRun4 = null!;
        private CheckBox _chkRun5 = null!;
        private Label _filterInfo = null!;

        private DataGridView _grid = null!;

        // ===== 右侧：历史开奖数据采集 =====
        private Button _btnFetch = null!;
        private NumericUpDown _numStartYear = null!;
        private NumericUpDown _numEndYear = null!;
        private Button _btnShowDraw = null!;
        private Button _btnDrawSettings = null!;
        private Label _drawStatus = null!;
        private DataGridView _gridDraw = null!;
        private CancellationTokenSource? _drawCts;

        private CancellationTokenSource? _cts;
        private long _totalRows;
        private int _pageIndex;
        private bool _busy;                 // 初始化生成中
        private bool _loadingPage;          // 正在查询某一页
        private bool _filtering;            // 正在应用红球形态过滤
        private bool _suppressFilterEvent;  // 代码内改勾选状态时不触发过滤
        private bool _suppressPageEvent;    // 代码内改页码时不触发查询
        private int? _pendingPage;          // 加载期间的新请求

        public CombinationForm()
        {
            Text = "双色球组合查询 + 历史开奖采集";
            Size = new Size(1560, 780);
            MinimumSize = new Size(1240, 620);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };

            BuildLeftPanel();
            BuildRightPanel();

            Controls.Add(_split);

            _grid.DataBindingComplete += (_, __) => ApplyColumnSettings();
            _grid.DataError += (_, e) => e.ThrowException = false;   // 不弹默认错误框
            _gridDraw.DataBindingComplete += (_, __) => ApplyDrawColumnSettings();
            _gridDraw.DataError += (_, e) => e.ThrowException = false;

            Load += (_, __) =>
            {
                try
                {
                    _split.SplitterDistance = _split.Width / 2;
                    LayoutRightAnchors();
                }
                catch
                {
                    // SplitterDistance 受 MinimumSize 约束失败时保持默认
                }
                ShowDrawData();
            };
            Load += async (_, __) => await LoadPageAsync(0);
        }

        /// <summary>初始化左右两侧控件的坐标（首次布局后按面板实际宽度对齐右侧锚定控件）</summary>
        private void BuildLeftPanel()
        {
            _btnInit = new Button
            {
                Text = "初始化组合数据",
                Location = new Point(12, 12),
                Size = new Size(130, 32)
            };
            _btnInit.Click += async (_, __) => await InitCombinationsAsync();

            _btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(148, 12),
                Size = new Size(56, 32),
                Enabled = false
            };
            _btnCancel.Click += (_, __) => _cts?.Cancel();

            _progress = new ProgressBar
            {
                Location = new Point(212, 18),
                Size = new Size(200, 20),
                Visible = false
            };

            _status = new Label
            {
                Text = "就绪",
                Location = new Point(420, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = Color.DimGray
            };

            _btnSettings = new Button
            {
                Text = "设置",
                Size = new Size(64, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnSettings.Click += (_, __) => OpenColumnSettings();

            _btnPrev = new Button
            {
                Text = "上一页",
                Location = new Point(12, 52),
                Size = new Size(72, 28)
            };
            _btnPrev.Click += (_, __) => _ = LoadPageAsync(_pageIndex - 1);

            _btnNext = new Button
            {
                Text = "下一页",
                Location = new Point(90, 52),
                Size = new Size(72, 28)
            };
            _btnNext.Click += (_, __) => _ = LoadPageAsync(_pageIndex + 1);

            _numPage = new NumericUpDown
            {
                Location = new Point(170, 54),
                Size = new Size(70, 26),
                Minimum = 1,
                Maximum = 1,
                Value = 1
            };
            _numPage.ValueChanged += (_, __) =>
            {
                if (_suppressPageEvent) return;
                _ = LoadPageAsync((int)_numPage.Value - 1);
            };

            var lblPageSize = new Label
            {
                Text = "每页",
                Location = new Point(248, 58),
                AutoSize = true
            };

            _cboPageSize = new ComboBox
            {
                Location = new Point(282, 54),
                Size = new Size(64, 26),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cboPageSize.Items.AddRange(new object[] { 50, 100, 200, 500, 1000, 5000 });
            _cboPageSize.SelectedIndex = 2;
            _cboPageSize.SelectedIndexChanged += (_, __) =>
            {
                if (_suppressPageEvent) return;
                _ = LoadPageAsync(0);
            };

            _pageInfo = new Label
            {
                Location = new Point(354, 58),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.DimGray,
                AutoEllipsis = true
            };

            // 号码查询行
            var lblSearch = new Label
            {
                Text = "号码查询",
                Location = new Point(12, 96),
                AutoSize = true
            };

            _numReds = new NumericUpDown[6];
            for (int i = 0; i < _numReds.Length; i++)
            {
                _numReds[i] = new NumericUpDown
                {
                    Location = new Point(74 + i * 46, 92),
                    Size = new Size(42, 26),
                    Minimum = 0,
                    Maximum = 33,
                    Value = 0
                };
                var tipText = $"红球 {i + 1}（0 = 不限）";
                var tip = new ToolTip();
                tip.SetToolTip(_numReds[i], tipText);
            }

            var lblBlue = new Label
            {
                Text = "蓝球",
                Location = new Point(356, 96),
                AutoSize = true
            };

            _numBlue = new NumericUpDown
            {
                Location = new Point(394, 92),
                Size = new Size(48, 26),
                Minimum = 0,
                Maximum = 16,
                Value = 0
            };
            new ToolTip().SetToolTip(_numBlue, "蓝球（0 = 不限）");

            _btnSearch = new Button
            {
                Text = "按号码查询",
                Location = new Point(450, 90),
                Size = new Size(96, 30)
            };
            _btnSearch.Click += async (_, __) => await SearchByNumbersAsync();

            _btnBackToPages = new Button
            {
                Text = "返回分页",
                Location = new Point(554, 90),
                Size = new Size(84, 30)
            };
            _btnBackToPages.Click += async (_, __) => await LoadPageAsync(_pageIndex);

            // 红球形态过滤行（只作用于红区）
            var lblFilter = new Label
            {
                Text = "红球形态过滤",
                Location = new Point(12, 134),
                AutoSize = true
            };

            _chkRun3 = new CheckBox
            {
                Text = "排除三连号",
                Location = new Point(100, 130),
                Size = new Size(96, 24)
            };
            _chkRun4 = new CheckBox
            {
                Text = "排除四连号",
                Location = new Point(200, 130),
                Size = new Size(96, 24)
            };
            _chkRun5 = new CheckBox
            {
                Text = "排除五连号",
                Location = new Point(300, 130),
                Size = new Size(96, 24)
            };

            var filterTip = new ToolTip();
            filterTip.SetToolTip(_chkRun3, "红球中出现 3 个连续号码（如 1 2 3、2 3 4）的组合将被排除，4/5 连号也一并排除；只影响红区");
            filterTip.SetToolTip(_chkRun4, "红球中出现 4 个连续号码（如 1 2 3 4）的组合将被排除，5 连号也一并排除；只影响红区");
            filterTip.SetToolTip(_chkRun5, "红球中出现 5 个连续号码（如 1 2 3 4 5）的组合将被排除；只影响红区");

            _chkRun3.CheckedChanged += (_, __) => { if (!_suppressFilterEvent) _ = ApplyRunFilterAsync(); };
            _chkRun4.CheckedChanged += (_, __) => { if (!_suppressFilterEvent) _ = ApplyRunFilterAsync(); };
            _chkRun5.CheckedChanged += (_, __) => { if (!_suppressFilterEvent) _ = ApplyRunFilterAsync(); };

            _filterInfo = new Label
            {
                Text = "未过滤",
                Location = new Point(404, 134),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.DimGray,
                AutoEllipsis = true
            };

            _grid = new DataGridView
            {
                Location = new Point(12, 164),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidth = 56,
                RowTemplate = { Height = 45 },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.RowPostPaint += (_, e) =>
            {
                var text = (_pageIndex * PageSize + e.RowIndex + 1).ToString("N0");
                var bounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top,
                    _grid.RowHeadersWidth - 8, e.RowBounds.Height);
                TextRenderer.DrawText(e.Graphics, text, _grid.Font, bounds,
                    Color.DimGray, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            };

            var leftControls = new Control[]
            {
                _btnInit, _btnCancel, _progress, _status, _btnSettings,
                _btnPrev, _btnNext, _numPage, lblPageSize, _cboPageSize, _pageInfo,
                lblSearch, lblBlue, _numBlue, _btnSearch, _btnBackToPages,
                lblFilter, _chkRun3, _chkRun4, _chkRun5, _filterInfo
            };
            _split.Panel1.Controls.AddRange(leftControls);
            _split.Panel1.Controls.AddRange(_numReds);
            _split.Panel1.Controls.Add(_grid);
        }

        /// <summary>右侧面板：历史开奖数据采集</summary>
        private void BuildRightPanel()
        {
            _btnFetch = new Button
            {
                Text = "按年份获取到内存",
                Location = new Point(12, 12),
                Size = new Size(150, 32)
            };
            _btnFetch.Click += async (_, __) => await FetchDrawDataAsync();

            var lblStart = new Label
            {
                Text = "起始年",
                Location = new Point(172, 18),
                AutoSize = true
            };

            _numStartYear = new NumericUpDown
            {
                Location = new Point(224, 14),
                Size = new Size(64, 26),
                Minimum = 2000,
                Maximum = DateTime.Now.Year,
                Value = LotteryService.StartYear
            };

            var lblEnd = new Label
            {
                Text = "结束年",
                Location = new Point(296, 18),
                AutoSize = true
            };

            _numEndYear = new NumericUpDown
            {
                Location = new Point(344, 14),
                Size = new Size(64, 26),
                Minimum = 2000,
                Maximum = DateTime.Now.Year,
                Value = LotteryService.EndYear
            };

            _btnShowDraw = new Button
            {
                Text = "显示内存数据",
                Location = new Point(420, 12),
                Size = new Size(110, 32)
            };
            _btnShowDraw.Click += (_, __) => ShowDrawData();

            _btnDrawSettings = new Button
            {
                Text = "设置",
                Size = new Size(64, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnDrawSettings.Click += (_, __) => OpenDrawColumnSettings();

            _drawStatus = new Label
            {
                Text = "就绪",
                Location = new Point(12, 54),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true,
                ForeColor = Color.DimGray
            };

            _gridDraw = new DataGridView
            {
                Location = new Point(12, 84),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersWidth = 40,
                RowTemplate = { Height = 45 },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            _split.Panel2.Controls.AddRange(new Control[]
            {
                _btnFetch, lblStart, _numStartYear, lblEnd, _numEndYear,
                _btnShowDraw, _btnDrawSettings, _drawStatus, _gridDraw
            });
        }

        /// <summary>首次布局后按面板实际宽度对齐两侧的右锚定控件</summary>
        private void LayoutRightAnchors()
        {
            int leftWidth = _split.Panel1.ClientSize.Width;
            _btnSettings.Left = leftWidth - _btnSettings.Width - 12;
            _status.Width = Math.Max(120, leftWidth - _status.Left - 12);
            _pageInfo.Width = Math.Max(160, leftWidth - _pageInfo.Left - 8);
            _filterInfo.Width = Math.Max(160, leftWidth - _filterInfo.Left - 8);

            int rightWidth = _split.Panel2.ClientSize.Width;
            _btnDrawSettings.Left = rightWidth - _btnDrawSettings.Width - 12;
            _drawStatus.Width = Math.Max(160, rightWidth - _drawStatus.Left - 12);
        }

        // ============================ 左侧：组合查询逻辑 ============================

        /// <summary>列设置在本界面的存储键</summary>
        private const string ColumnSettingKey = nameof(CombinationForm);

        /// <summary>打开列显示设置，保存到 SQLite 并立即应用</summary>
        private void OpenColumnSettings()
        {
            var items = _grid.Columns.Cast<DataGridViewColumn>()
                .OrderBy(c => c.DisplayIndex)
                .Select(c => (key: GetColumnKey(c), text: string.IsNullOrEmpty(c.HeaderText) ? c.Name : c.HeaderText, visible: c.Visible))
                .ToList();
            if (items.Count == 0)
            {
                MessageBox.Show("当前没有可设置的列，请先加载数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new ColumnSettingForm("全部组合查询 - 列显示设置", items);
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

        private int PageSize => (int)(_cboPageSize.SelectedItem ?? 200);

        /// <summary>异步加载指定页（只取一页数据，界面不卡顿）</summary>
        private async Task LoadPageAsync(int pageIndex)
        {
            if (_busy) return;
            if (_loadingPage)          // 正在加载时记住最后一次请求
            {
                _pendingPage = pageIndex;
                return;
            }

            int pageSize = PageSize;
            if (_totalRows > 0)
            {
                int knownMax = (int)((_totalRows + pageSize - 1) / pageSize);
                pageIndex = Math.Clamp(pageIndex, 0, knownMax - 1);
            }
            pageIndex = Math.Max(0, pageIndex);

            _loadingPage = true;
            UpdateUiState();
            string previousStatus = _status.Text;
            _status.Text = $"正在加载第 {pageIndex + 1:N0} 页 ...";
            _status.ForeColor = Color.DimGray;

            try
            {
                var (table, total) = await Task.Run(() => CombinationService.QueryPageData(pageIndex, pageSize));
                _totalRows = total;
                UpdateFilterInfo(total);

                int maxPage = total == 0 ? 1 : (int)((total + pageSize - 1) / pageSize);
                if (pageIndex > maxPage - 1)      // 数据变少时回退到最后一页
                {
                    pageIndex = maxPage - 1;
                    (table, total) = await Task.Run(() => CombinationService.QueryPageData(pageIndex, pageSize));
                    _totalRows = total;
                }

                _pageIndex = pageIndex;
                _suppressPageEvent = true;
                try
                {
                    _numPage.Maximum = Math.Max(1, maxPage);
                    _numPage.Value = pageIndex + 1;
                }
                finally
                {
                    _suppressPageEvent = false;
                }

                _grid.DataSource = table;
                _pageInfo.Text = $"第 {pageIndex + 1:N0} / {maxPage:N0} 页，共 {total:N0} 条组合";
                bool filtered = CombinationService.CurrentFilter != RunFilter.None;
                _status.Text = total == 0
                    ? (filtered
                        ? "按当前红球形态过滤后没有剩余组合，请取消部分过滤条件。"
                        : "内存中还没有组合数据，请点击\"初始化组合数据\"（生成后不写数据库）。")
                    : $"已加载第 {pageIndex + 1:N0} 页（每页 {pageSize:N0} 条），共 {total:N0} 条组合（内存）"
                      + (filtered ? "，已应用红球形态过滤" : string.Empty);
                _status.ForeColor = total == 0 ? Color.Red : Color.DimGray;
            }
            catch (Exception ex)
            {
                _status.Text = "查询失败：" + ex.Message + "（上一状态：" + previousStatus + "）";
                _status.ForeColor = Color.Red;
            }
            finally
            {
                _loadingPage = false;
                UpdateUiState();

                int? next = _pendingPage;
                _pendingPage = null;
                if (next.HasValue && next.Value != _pageIndex)
                    await LoadPageAsync(next.Value);
            }
        }

        /// <summary>统一刷新按钮/控件可用状态</summary>
        private void UpdateUiState()
        {
            bool idle = !_busy && !_loadingPage && !_filtering;
            int pageSize = PageSize;
            int maxPage = _totalRows == 0 ? 1 : (int)((_totalRows + pageSize - 1) / pageSize);

            _btnInit.Enabled = idle;
            _btnInit.Text = _busy ? "正在初始化 ..." : "初始化组合数据";
            _btnSettings.Enabled = idle;
            _btnCancel.Enabled = _busy;
            _progress.Visible = _busy;
            _numPage.Enabled = idle;
            _cboPageSize.Enabled = idle;
            _btnPrev.Enabled = idle && _pageIndex > 0;
            _btnNext.Enabled = idle && _pageIndex < maxPage - 1;

            _btnSearch.Enabled = idle;
            _btnBackToPages.Enabled = idle;
            _numBlue.Enabled = idle;
            foreach (var num in _numReds) num.Enabled = idle;

            _chkRun3.Enabled = idle;
            _chkRun4.Enabled = idle;
            _chkRun5.Enabled = idle;

            UseWaitCursor = _busy || _loadingPage || _filtering;
        }

        /// <summary>当前勾选的红球形态过滤条件</summary>
        private RunFilter CurrentRunFilter()
        {
            var filter = RunFilter.None;
            if (_chkRun3.Checked) filter |= RunFilter.Exclude3;
            if (_chkRun4.Checked) filter |= RunFilter.Exclude4;
            if (_chkRun5.Checked) filter |= RunFilter.Exclude5;
            return filter;
        }

        /// <summary>应用红球连续形态过滤（只作用于红区），完成后刷新第一页</summary>
        private async Task ApplyRunFilterAsync()
        {
            if (_busy || _loadingPage || _filtering) return;

            if (!CombinationService.IsReady)
            {
                _filterInfo.Text = "尚未生成组合数据";
                return;
            }

            var filter = CurrentRunFilter();
            _filtering = true;
            UpdateUiState();
            _status.Text = filter == RunFilter.None ? "正在清除红球形态过滤 ..." : "正在按红球连续形态过滤 ...";
            _status.ForeColor = Color.DimGray;

            try
            {
                long kept = await Task.Run(() => CombinationService.ApplyRunFilter(filter));
                _totalRows = kept;
                UpdateFilterInfo(kept);

                _status.Text = filter == RunFilter.None
                    ? $"已清除过滤：共 {kept:N0} 条组合"
                    : $"红球形态过滤已生效：剩余 {kept:N0} / {CombinationService.GetCount():N0} 条组合";
                _status.ForeColor = Color.DimGray;
            }
            catch (Exception ex)
            {
                _status.Text = "过滤失败：" + ex.Message;
                _status.ForeColor = Color.Red;
            }
            finally
            {
                _filtering = false;
                UpdateUiState();
            }

            await LoadPageAsync(0);
        }

        /// <summary>刷新过滤结果提示</summary>
        private void UpdateFilterInfo(long kept)
        {
            long total = CombinationService.GetCount();
            if (total == 0)
            {
                _filterInfo.Text = "尚未生成组合数据";
                return;
            }

            _filterInfo.Text = kept >= total
                ? $"未过滤：{total:N0} 条"
                : $"过滤后：{kept:N0} / {total:N0} 条（已排除 {total - kept:N0} 条）";
        }

        /// <summary>按号码查询（满 6 红走组合排名精确定位，否则在内存中按包含匹配扫描）</summary>
        private async Task SearchByNumbersAsync()
        {
            if (_busy || _loadingPage || _filtering) return;

            if (!CombinationService.IsReady)
            {
                MessageBox.Show("内存中还没有组合数据，请先点击\"初始化组合数据\"。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var reds = _numReds.Select(n => (int)n.Value).Where(v => v > 0).ToArray();
            int blue = (int)_numBlue.Value;
            if (reds.Length == 0 && blue <= 0)
            {
                MessageBox.Show("请至少输入一个号码（红球或蓝球），0 表示该位置不限。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _loadingPage = true;
            UpdateUiState();
            _status.Text = "正在按号码查询 ...";
            _status.ForeColor = Color.DimGray;

            try
            {
                var (table, exact, truncated) = await Task.Run(() => CombinationService.QueryByNumbers(reds, blue));
                _grid.DataSource = table;

                string condition = $"红球 [{string.Join(",", reds.OrderBy(r => r))}]" +
                                   (blue > 0 ? $" 蓝球 [{blue}]" : string.Empty) +
                                   (CombinationService.CurrentFilter != RunFilter.None ? "（已应用红球形态过滤）" : string.Empty);
                if (exact)
                {
                    bool found = table.Rows.Count > 0;
                    _status.Text = found
                        ? $"精确匹配：该组合存在（{condition}）"
                        : $"精确匹配：未找到该组合（{condition}）";
                    _status.ForeColor = found ? Color.Green : Color.Red;
                }
                else
                {
                    _status.Text = $"包含 {condition} 的组合：显示 {table.Rows.Count:N0} 条" +
                                   (truncated ? "（已达上限 500 条，请多输入几个号码缩小范围）" : string.Empty);
                    _status.ForeColor = table.Rows.Count > 0 ? Color.DimGray : Color.Red;
                }

                _pageInfo.Text = $"号码查询：{table.Rows.Count:N0} 条结果";
                _grid.RowHeadersVisible = true;
            }
            catch (Exception ex)
            {
                _status.Text = "号码查询失败：" + ex.Message;
                _status.ForeColor = Color.Red;
            }
            finally
            {
                _loadingPage = false;
                UpdateUiState();
            }
        }

        private async Task InitCombinationsAsync()
        {
            long existing = 0;
            try
            {
                existing = await Task.Run(() => CombinationService.GetCount());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (existing > 0)
            {
                var choice = MessageBox.Show(
                    $"内存中已有 {existing:N0} 条组合，初始化会【替换】为全部 {CombinationService.TotalCombinations:N0} 条组合。\n\n" +
                    "组合数据只保存在内存中，不会写入数据库。是否继续？",
                    "初始化确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (choice != DialogResult.OK) return;
            }
            else
            {
                var choice = MessageBox.Show(
                    $"将在内存中生成全部双色球组合：C(33,6) × 16 = {CombinationService.TotalCombinations:N0} 条。\n" +
                    "占用内存约 124 MB，不写数据库（磁盘不会增大），约需 1~3 秒。是否继续？",
                    "初始化确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (choice != DialogResult.OK) return;
            }

            _cts = new CancellationTokenSource();
            _busy = true;
            UpdateUiState();
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = 0;
            _status.ForeColor = Color.DimGray;

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    _status.Text = msg;
                    int idx = msg.IndexOf('%');
                    int startIdx = msg.LastIndexOf('（') + 1;
                    if (idx > startIdx && startIdx > 0 &&
                        double.TryParse(msg.Substring(startIdx, idx - startIdx), out double percent))
                    {
                        _progress.Value = Math.Min(100, Math.Max(0, (int)percent));
                    }
                });

                long generated = await CombinationService.GenerateAllAsync(progress, _cts.Token);
                long total = CombinationService.GetCount();

                ResetRunFilterUi();      // 重新生成后过滤条件失效，勾选状态同步清空

                _busy = false;
                UpdateUiState();
                _status.Text = $"初始化完成：内存中共 {total:N0} 条组合（约 {CombinationService.MemoryBytes / 1024.0 / 1024.0:F0} MB，未写数据库）。";
                _status.ForeColor = Color.Green;
                _progress.Value = 100;
                await LoadPageAsync(0);
            }
            catch (OperationCanceledException)
            {
                _status.Text = "已取消初始化。";
                _status.ForeColor = Color.DimGray;
            }
            catch (Exception ex)
            {
                _status.Text = "初始化失败：" + ex.Message;
                _status.ForeColor = Color.Red;
                MessageBox.Show(ex.ToString(), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _busy = false;
                UpdateUiState();
            }
        }

        /// <summary>清空过滤勾选（生成新数据后调用，不触发过滤计算）</summary>
        private void ResetRunFilterUi()
        {
            _suppressFilterEvent = true;
            try
            {
                _chkRun3.Checked = false;
                _chkRun4.Checked = false;
                _chkRun5.Checked = false;
            }
            finally
            {
                _suppressFilterEvent = false;
            }
            UpdateFilterInfo(CombinationService.GetCount());
        }

        // ============================ 右侧：历史开奖采集逻辑 ============================

        /// <summary>右侧列设置的存储键（沿用独立采集界面的键，设置可互通）</summary>
        private const string DrawColumnSettingKey = nameof(DrawDataCollection);

        /// <summary>打开右侧列显示设置，保存到 SQLite 并立即应用</summary>
        private void OpenDrawColumnSettings()
        {
            var items = _gridDraw.Columns.Cast<DataGridViewColumn>()
                .OrderBy(c => c.DisplayIndex)
                .Select(c => (key: GetColumnKey(c), text: string.IsNullOrEmpty(c.HeaderText) ? c.Name : c.HeaderText, visible: c.Visible))
                .ToList();
            if (items.Count == 0)
            {
                MessageBox.Show("当前没有可设置的列，请先获取或显示数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dialog = new ColumnSettingForm("历史开奖数据采集 - 列显示设置", items);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                ColumnSettingService.Save(DrawColumnSettingKey, dialog.Result);
                ApplyDrawColumnSettings();
                _drawStatus.Text = "列显示设置已保存。";
                _drawStatus.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存列设置失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>按数据库中保存的设置应用右侧列显示（无设置时全部显示）</summary>
        private void ApplyDrawColumnSettings()
        {
            if (_gridDraw.Columns.Count == 0) return;

            Dictionary<string, bool> settings;
            try
            {
                settings = ColumnSettingService.Load(DrawColumnSettingKey);
            }
            catch
            {
                return;
            }

            if (settings.Count == 0) return;

            foreach (DataGridViewColumn column in _gridDraw.Columns)
            {
                if (settings.TryGetValue(GetColumnKey(column), out bool visible))
                    column.Visible = visible;
            }
        }

        /// <summary>按年份下载开奖数据并合并到内存（不写数据库）</summary>
        private async Task FetchDrawDataAsync()
        {
            int startYear = (int)_numStartYear.Value;
            int endYear = (int)_numEndYear.Value;
            if (startYear > endYear)
            {
                MessageBox.Show("起始年份不能大于结束年份。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _drawCts = new CancellationTokenSource();
            SetDrawBusy(true, $"正在按年份获取 {startYear} - {endYear} 年数据 ...");
            try
            {
                var progress = new Progress<string>(msg =>
                {
                    _drawStatus.Text = msg;
                    _drawStatus.ForeColor = Color.DimGray;
                });

                var (table, skipped) = await LotteryService.DownloadAllYearsAsync(
                    startYear, endYear, progress, _drawCts.Token);

                if (table.Rows.Count == 0)
                {
                    _drawStatus.Text = "所有年份均未获取到数据。";
                    _drawStatus.ForeColor = Color.Red;
                    return;
                }

                SetDrawBusy(true, $"正在把 {table.Rows.Count} 期数据合并到内存 ...");
                var (inserted, updated) = await Task.Run(() => LotteryService.Merge(table));

                _gridDraw.DataSource = LotteryService.GetDataTable();   // 已按 Year DESC, Code DESC 排序
                string skipInfo = skipped.Count == 0 ? string.Empty : $"，跳过无数据年份：{string.Join(",", skipped)}";
                _drawStatus.Text = $"{startYear}-{endYear} 年共 {table.Rows.Count} 期：新增 {inserted}，更新 {updated}{skipInfo}；" +
                                   $"内存现有 {LotteryService.Count:N0} 期（未写数据库，关闭程序后需重新采集）";
                _drawStatus.ForeColor = Color.Green;
            }
            catch (OperationCanceledException)
            {
                _drawStatus.Text = "已取消。";
                _drawStatus.ForeColor = Color.DimGray;
            }
            catch (Exception ex)
            {
                _drawStatus.Text = "失败：" + ex.Message;
                _drawStatus.ForeColor = Color.Red;
                MessageBox.Show(ex.ToString(), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _drawCts.Dispose();
                _drawCts = null;
                SetDrawBusy(false, null);
            }
        }

        /// <summary>显示内存中的开奖数据（按年份、期号倒序）</summary>
        private void ShowDrawData()
        {
            try
            {
                var table = LotteryService.GetDataTable();
                _gridDraw.DataSource = table;
                _drawStatus.Text = table.Rows.Count == 0
                    ? "内存中暂无开奖数据，请先点击\"按年份获取到内存\"。"
                    : $"已加载内存中的开奖数据，共 {table.Rows.Count:N0} 期（按年份、期号倒序）。";
                _drawStatus.ForeColor = Color.DimGray;
            }
            catch (Exception ex)
            {
                _drawStatus.Text = "读取失败：" + ex.Message;
                _drawStatus.ForeColor = Color.Red;
            }
        }

        /// <summary>右侧控件忙碌状态</summary>
        private void SetDrawBusy(bool busy, string? message)
        {
            _btnFetch.Enabled = _btnShowDraw.Enabled = _btnDrawSettings.Enabled =
                _numStartYear.Enabled = _numEndYear.Enabled = !busy;
            _btnFetch.Text = busy ? "正在获取 ..." : "按年份获取到内存";
            if (message != null)
                _drawStatus.Text = message;
        }
    }
}
