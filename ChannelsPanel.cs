namespace SerialPlotter;

// Менеджер входящих данных: какие каналы показывать, каким цветом
// и относительно какой переменной строить график (ось X — время или любой канал).
public sealed class ChannelsPanel : Panel
{
    private readonly Label _header;
    private readonly Button _rangeBtn;
    private readonly Label _xLabel;
    private readonly ComboBox _xBox;
    private readonly ToolTip _tip = new();
    private bool _updating;

    // Индекс канала на оси X в DataHub.Channels; -1 — ось X это время.
    public int XIndex { get; private set; } = -1;

    public ChannelsPanel()
    {
        Dock = DockStyle.Right;
        Width = 250;
        AutoScroll = true;

        _header = new Label
        {
            Text = "Каналы", Left = 8, Top = 8, Width = 130, Height = 24,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        _xLabel = new Label { Text = "Ось X:", Left = 8, Top = 41, Width = 50 };
        _xBox = new ComboBox
        {
            Left = 60, Top = 37, Width = 180,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _xBox.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            XIndex = _xBox.SelectedIndex - 1;
            RebuildRows();
        };

        _rangeBtn = new Button { Text = "Диапазоны…", Left = 140, Top = 6, Width = 102, Height = 24 };
        _rangeBtn.Click += (_, _) =>
        {
            using var dlg = new RangeForm();
            dlg.ShowDialog(FindForm());
        };

        Controls.Add(_header);
        Controls.Add(_xLabel);
        Controls.Add(_xBox);
        Controls.Add(_rangeBtn);

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
            if (!ReferenceEquals(c, _header) && !ReferenceEquals(c, _rangeBtn) &&
                !ReferenceEquals(c, _xLabel) && !ReferenceEquals(c, _xBox))
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
                Left = 8, Top = y, Width = 220, Height = 40,
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
                Left = 40, Top = y + 4, Width = 170, Height = 22,
                Checked = ch.Visible || isX,
                Enabled = !isX,
                Text = isX ? $"{ch.Name}  — ось X" : ch.Name,
                ForeColor = Theme.Text,
            };
            check.CheckedChanged += (_, _) => ch.Visible = check.Checked;

            var editBtn = new Button
            {
                Text = "…", Left = 214, Top = y + 2, Width = 26, Height = 24,
            };
            _tip.SetToolTip(editBtn, "Настройки канала: имя, цвет, толщина и тип линии");
            editBtn.Click += (_, _) =>
            {
                using var dlg = new ChannelEditForm(ch);
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                    DataHub.NotifyChannelsChanged(); // обновит подписи строк и список оси X
            };

            Controls.Add(swatch);
            Controls.Add(check);
            Controls.Add(editBtn);
            y += 30;
        }
    }
}

// Диалог правки канала: имя, цвет, толщина и тип линии.
internal sealed class ChannelEditForm : Form
{
    public ChannelEditForm(Channel ch)
    {
        Text = $"Канал: {ch.Name}";
        ClientSize = new Size(280, 252);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var colorLabel = new Label { Text = "Цвет линии:", Left = 12, Top = 14, Width = 120 };
        var colorBtn = new Button
        {
            Left = 12, Top = 34, Width = 120, Height = 30,
            BackColor = ch.Color, Text = "Изменить…", FlatStyle = FlatStyle.Flat,
            Tag = ch, // чтобы тема не перекрасила кнопку-образец
        };
        FixTextColor(colorBtn);
        colorBtn.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, Color = colorBtn.BackColor };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                colorBtn.BackColor = dlg.Color;
                FixTextColor(colorBtn);
            }
        };

        var nameLabel = new Label { Text = "Имя:", Left = 12, Top = 78, Width = 120 };
        var nameBox = new TextBox { Left = 12, Top = 98, Width = 256, Text = ch.Name };

        var widthLabel = new Label { Text = "Толщина линии:", Left = 12, Top = 134, Width = 120 };
        var widthNum = new NumericUpDown
        {
            Left = 12, Top = 154, Width = 120,
            Minimum = 1, Maximum = 10, DecimalPlaces = 1, Increment = 0.5m,
            Value = (decimal)Math.Clamp(ch.LineWidth, 1, 10),
        };

        var styleLabel = new Label { Text = "Тип линии:", Left = 148, Top = 134, Width = 120 };
        var styleBox = new ComboBox
        {
            Left = 148, Top = 152, Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        styleBox.Items.AddRange(new object[] { "Сплошная", "Штриховая", "Пунктир", "Штрих-пунктир" });
        styleBox.SelectedIndex = (int)ch.LineStyle;

        var okBtn = new Button { Text = "OK", Left = 102, Top = 208, Width = 80, DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = "Отмена", Left = 190, Top = 208, Width = 80, DialogResult = DialogResult.Cancel };
        okBtn.Click += (_, _) =>
        {
            if (nameBox.Text.Trim().Length > 0) ch.Name = nameBox.Text.Trim();
            ch.Color = colorBtn.BackColor;
            ch.LineWidth = (float)widthNum.Value;
            ch.LineStyle = (LineKind)styleBox.SelectedIndex;
        };

        Controls.AddRange(new Control[]
            { colorLabel, colorBtn, nameLabel, nameBox,
              widthLabel, widthNum, styleLabel, styleBox, okBtn, cancelBtn });

        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        HandleCreated += (_, _) => Theme.ApplyTitleBar(this);
        Theme.Apply(this);
        FixTextColor(colorBtn); // тема могла сбросить цвет текста образца
    }

    // Цвет текста на кнопке-образце: чёрный на светлом, белый на тёмном.
    private static void FixTextColor(Button b) =>
        b.ForeColor = b.BackColor.GetBrightness() > 0.5f ? Color.Black : Color.White;
}
