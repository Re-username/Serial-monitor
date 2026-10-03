namespace SerialPlotter;

// Менеджер входящих данных: какие каналы показывать, каким цветом
// и относительно какой переменной строить график (ось X — время или любой канал).
public sealed class ChannelsPanel : Panel
{
    private readonly Label _header;
    private readonly Label _xLabel;
    private readonly ComboBox _xBox;
    private bool _updating;

    // Индекс канала на оси X в DataHub.Channels; -1 — ось X это время.
    public int XIndex { get; private set; } = -1;

    public ChannelsPanel()
    {
        Dock = DockStyle.Right;
        Width = 230;
        AutoScroll = true;

        _header = new Label
        {
            Text = "Каналы", Left = 8, Top = 8, Width = 200, Height = 24,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        _xLabel = new Label { Text = "Ось X:", Left = 8, Top = 41, Width = 50 };
        _xBox = new ComboBox
        {
            Left = 60, Top = 37, Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _xBox.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            XIndex = _xBox.SelectedIndex - 1;
            RebuildRows();
        };

        Controls.Add(_header);
        Controls.Add(_xLabel);
        Controls.Add(_xBox);

        DataHub.ChannelsChanged += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        if (XIndex >= DataHub.Channels.Count)
            XIndex = -1;

        _updating = true;
        _xBox.Items.Clear();
        _xBox.Items.Add("Время");
        foreach (var ch in DataHub.Channels)
            _xBox.Items.Add(ch.Name);
        _xBox.SelectedIndex = XIndex + 1;
        _updating = false;

        RebuildRows();
    }

    private void RebuildRows()
    {
        // Удаляем все строки, оставляем шапку (заголовок + выбор оси X)
        for (int i = Controls.Count - 1; i >= 0; i--)
        {
            Control c = Controls[i];
            if (!ReferenceEquals(c, _header) && !ReferenceEquals(c, _xLabel) && !ReferenceEquals(c, _xBox))
            {
                Controls.RemoveAt(i);
                c.Dispose();
            }
        }

        int y = 66;
        if (DataHub.Channels.Count == 0)
        {
            Controls.Add(new Label
            {
                Text = "Нет данных — подключись к порту\r\nили запусти симулятор",
                Left = 8, Top = y, Width = 200, Height = 40,
                ForeColor = Theme.Text,
            });
            return;
        }

        Channel? xch = XIndex >= 0 && XIndex < DataHub.Channels.Count
            ? DataHub.Channels[XIndex]
            : null;

        foreach (var ch in DataHub.Channels)
        {
            var swatch = new Button
            {
                Left = 8, Top = y + 2, Width = 24, Height = 24,
                BackColor = ch.Color, FlatStyle = FlatStyle.Flat, Tag = ch,
            };
            swatch.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = ch.Color };
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    ch.Color = dlg.Color;
                    swatch.BackColor = dlg.Color;
                }
            };

            bool isX = ReferenceEquals(ch, xch);
            var check = new CheckBox
            {
                Left = 40, Top = y + 4, Width = 182, Height = 22,
                Checked = ch.Visible || isX,
                Enabled = !isX,
                Text = isX ? $"{ch.Name}  — ось X" : ch.Name,
                ForeColor = Theme.Text,
            };
            check.CheckedChanged += (_, _) => ch.Visible = check.Checked;

            Controls.Add(swatch);
            Controls.Add(check);
            y += 30;
        }
    }
}
