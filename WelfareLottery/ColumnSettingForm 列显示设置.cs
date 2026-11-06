using System.Collections.Generic;

namespace WelfareLottery
{
    /// <summary>
    /// 列显示设置对话框：勾选要显示的列
    /// </summary>
    public partial class ColumnSettingForm : Form
    {
        private sealed class ColumnItem
        {
            public string Key { get; init; } = string.Empty;
            public string Text { get; init; } = string.Empty;
            public bool DefaultVisible { get; init; } = true;
            public override string ToString() => Text;
        }

        private readonly CheckedListBox _list;

        /// <summary>点确定后返回：列标识 -> 是否显示</summary>
        public Dictionary<string, bool> Result { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ColumnSettingForm(string title, IEnumerable<(string key, string text, bool visible)> columns)
        {
            Text = title;
            Size = new Size(380, 500);
            MinimumSize = new Size(340, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            var lblTip = new Label
            {
                Text = "勾选需要显示的列（未设置时默认全部显示）：",
                Location = new Point(14, 12),
                AutoSize = true
            };

            _list = new CheckedListBox
            {
                Location = new Point(14, 38),
                Size = new Size(340, 340),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                CheckOnClick = true,
                IntegralHeight = false
            };

            foreach (var (key, text, visible) in columns)
            {
                _list.Items.Add(new ColumnItem { Key = key, Text = text, DefaultVisible = visible }, visible);
            }

            var btnAll = new Button
            {
                Text = "全选",
                Location = new Point(14, 388),
                Size = new Size(70, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnAll.Click += (_, __) => SetAll(true);

            var btnNone = new Button
            {
                Text = "全不选",
                Location = new Point(90, 388),
                Size = new Size(70, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnNone.Click += (_, __) => SetAll(false);

            var btnDefault = new Button
            {
                Text = "恢复默认",
                Location = new Point(166, 388),
                Size = new Size(90, 28),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            btnDefault.Click += (_, __) => RestoreDefault();

            var btnOk = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(196, 426),
                Size = new Size(76, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnOk.Click += (_, __) => CollectResult();

            var btnCancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(278, 426),
                Size = new Size(76, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lblTip, _list, btnAll, btnNone, btnDefault, btnOk, btnCancel });
        }

        private void SetAll(bool visible)
        {
            for (int i = 0; i < _list.Items.Count; i++)
                _list.SetItemChecked(i, visible);
        }

        private void RestoreDefault()
        {
            for (int i = 0; i < _list.Items.Count; i++)
                if (_list.Items[i] is ColumnItem item)
                    _list.SetItemChecked(i, item.DefaultVisible);
        }

        private void CollectResult()
        {
            Result.Clear();
            for (int i = 0; i < _list.Items.Count; i++)
                if (_list.Items[i] is ColumnItem item)
                    Result[item.Key] = _list.GetItemChecked(i);
        }
    }
}
