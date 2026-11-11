namespace WelfareLottery
{
    /// <summary>
    /// 号码数组筛选设置对话框：
    /// 每行 = 一个号码数组（Red1..Red6 + Blue，按升序位置，0 = 不限），可添加多行；
    /// 每行可选"包含"（只保留完全匹配非零位置的记录）或"排除"（剔除匹配的记录）。
    /// </summary>
    public class NumberSpecForm : Form
    {
        private readonly DataGridView _grid;
        private readonly NumericUpDown _numRecent;

        /// <summary>点确定后返回：号码数组条件列表（空列表 = 清除数组筛选）</summary>
        public List<NumberSpec> Specs { get; } = new();

        public NumberSpecForm(IReadOnlyList<NumberSpec> current)
        {
            Text = "号码数组筛选";
            Size = new Size(760, 460);
            MinimumSize = new Size(700, 400);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 246, 248);
            ShowInTaskbar = false;

            var hint = new Label
            {
                Text = "每行为一个号码数组：Red1..Red6 为红球从小到大的第 1~6 个位置，Blue 为蓝球；0 = 不限。" +
                       "模式选\"包含\"只保留匹配记录，选\"排除\"剔除匹配记录；多行之间为\"且\"关系。",
                Location = new Point(12, 10),
                Size = new Size(720, 40),
                ForeColor = Color.DimGray
            };

            _grid = new DataGridView
            {
                Location = new Point(12, 56),
                Size = new Size(720, 300),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 6, 4, 6);
            _grid.ColumnHeadersHeight = 34;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.GridColor = Color.FromArgb(229, 231, 235);

            for (int i = 1; i <= 6; i++)
            {
                var col = new DataGridViewTextBoxColumn
                {
                    Name = "Red" + i,
                    HeaderText = "红" + i,
                    DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
                    FillWeight = 10,
                    ToolTipText = $"第 {i} 个红球（升序位置），0 = 不限"
                };
                _grid.Columns.Add(col);
            }

            var colBlue = new DataGridViewTextBoxColumn
            {
                Name = "Blue",
                HeaderText = "蓝球",
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
                FillWeight = 10,
                ToolTipText = "蓝球号码，0 = 不限"
            };
            _grid.Columns.Add(colBlue);

            var colMode = new DataGridViewComboBoxColumn
            {
                Name = "Mode",
                HeaderText = "模式",
                Items = { "包含", "排除" },
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
                FillWeight = 12
            };
            _grid.Columns.Add(colMode);

            // 载入已有条件
            foreach (var spec in current)
            {
                int idx = _grid.Rows.Add();
                var row = _grid.Rows[idx];
                for (int i = 0; i < 6; i++) row.Cells["Red" + (i + 1)].Value = spec.Reds[i].ToString();
                row.Cells["Blue"].Value = spec.Blue.ToString();
                row.Cells["Mode"].Value = spec.Exclude ? "排除" : "包含";
            }

            var btnAdd = new Button
            {
                Text = "添加数组",
                Location = new Point(12, 372),
                Size = new Size(96, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnAdd.Click += (_, __) => AddRow();

            var btnDelete = new Button
            {
                Text = "删除选中行",
                Location = new Point(116, 372),
                Size = new Size(104, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnDelete.Click += (_, __) =>
            {
                foreach (DataGridViewRow row in _grid.SelectedRows)
                {
                    if (!row.IsNewRow) _grid.Rows.Remove(row);
                }
                if (_grid.CurrentCell != null && !_grid.CurrentCell.OwningRow.IsNewRow)
                    _grid.Rows.Remove(_grid.CurrentCell.OwningRow);
            };

            var btnClear = new Button
            {
                Text = "清空",
                Location = new Point(228, 372),
                Size = new Size(72, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnClear.Click += (_, __) => _grid.Rows.Clear();

            // 从"历史开奖采集"内存数据载入最近 N 期开奖号码作为号码数组
            var lblRecent = new Label
            {
                Text = "取最近",
                Location = new Point(316, 378),
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            _numRecent = new NumericUpDown
            {
                Location = new Point(362, 374),
                Size = new Size(46, 26),
                Minimum = 1,
                Maximum = 100,
                Value = 1,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                TextAlign = HorizontalAlignment.Center
            };

            var lblRecent2 = new Label
            {
                Text = "期开奖",
                Location = new Point(412, 378),
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            var btnLoadRecent = new Button
            {
                Text = "载入",
                Location = new Point(462, 372),
                Size = new Size(64, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnLoadRecent.Click += (_, __) => LoadRecentDraws();

            var recentTip = new ToolTip();
            recentTip.SetToolTip(_numRecent, "载入历史开奖中最近 N 期（按年份、期号倒序）的号码作为号码数组");
            recentTip.SetToolTip(btnLoadRecent, "把最近 N 期开奖号码填入下方数组，模式默认为\"排除\"（可逐行修改）");

            var btnOk = new Button
            {
                Text = "确定",
                Location = new Point(548, 372),
                Size = new Size(88, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnOk.Click += (_, __) => OnOk();

            var btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(644, 372),
                Size = new Size(88, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel
            };

            Controls.AddRange(new Control[]
            {
                hint, _grid, btnAdd, btnDelete, btnClear,
                lblRecent, _numRecent, lblRecent2, btnLoadRecent,
                btnOk, btnCancel
            });
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            if (_grid.Rows.Count > 0) _grid.CurrentCell = _grid.Rows[0].Cells["Red1"];
        }

        private void AddRow()
        {
            int idx = _grid.Rows.Add();
            var row = _grid.Rows[idx];
            for (int i = 1; i <= 6; i++) row.Cells["Red" + i].Value = "0";
            row.Cells["Blue"].Value = "0";
            row.Cells["Mode"].Value = "包含";
            _grid.CurrentCell = row.Cells["Red1"];
        }

        /// <summary>载入历史开奖（内存）中最近 N 期（年份、期号倒序）的号码作为号码数组</summary>
        private void LoadRecentDraws()
        {
            var table = LotteryService.GetDataTable();
            if (table.Rows.Count == 0)
            {
                MessageBox.Show("内存中暂无开奖数据，请先在\"历史开奖采集\"中按年份获取到内存。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int n = (int)_numRecent.Value;
            int count = Math.Min(n, table.Rows.Count);

            for (int r = 0; r < count; r++)
            {
                var draw = table.Rows[r];
                int idx = _grid.Rows.Add();
                var row = _grid.Rows[idx];
                for (int i = 1; i <= 6; i++)
                    row.Cells["Red" + i].Value = (draw["Red" + i] == DBNull.Value ? 0 : Convert.ToInt32(draw["Red" + i])).ToString();
                row.Cells["Blue"].Value = (draw["Blue"] == DBNull.Value ? 0 : Convert.ToInt32(draw["Blue"])).ToString();
                row.Cells["Mode"].Value = "排除";
            }

            _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, _grid.Rows.Count - 1);
        }

        private void OnOk()
        {
            Specs.Clear();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;

                var reds = new int[6];
                for (int i = 0; i < 6; i++)
                {
                    if (!TryParseCell(row, "Red" + (i + 1), 0, 33, out reds[i])) return;
                }
                if (!TryParseCell(row, "Blue", 0, 16, out int blue)) return;

                bool exclude = string.Equals(row.Cells["Mode"].Value?.ToString(), "排除", StringComparison.Ordinal);
                Specs.Add(new NumberSpec(reds, blue, exclude));
            }

            DialogResult = DialogResult.OK;
        }

        private bool TryParseCell(DataGridViewRow row, string cellName, int min, int max, out int value)
        {
            string text = row.Cells[cellName].Value?.ToString()?.Trim() ?? "";
            if (!int.TryParse(text, out value) || value < min || value > max)
            {
                MessageBox.Show($"第 {row.Index + 1} 行的\"{cellName}\"列需为 {min}-{max} 的整数（0 = 不限）。",
                    "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                value = 0;
                return false;
            }
            return true;
        }
    }
}
