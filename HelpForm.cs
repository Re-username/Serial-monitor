namespace SerialPlotter;

// Справка по навигации (F1). Немодальная — не мешает работе с графиком.
public sealed class HelpForm : Form
{
    private static HelpForm? _open;

    public static void Toggle(Form owner)
    {
        if (_open is { IsDisposed: false })
        {
            _open.Close();
            return;
        }
        _open = new HelpForm();
        _open.Show(owner);
    }

    private HelpForm()
    {
        Text = "Справка — навигация и горячие клавиши";
        ClientSize = new Size(430, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var text = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9.5f),
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            Text =
                "РЕЖИМ «ВРЕМЯ» — МЫШЬ\r\n" +
                "\r\n" +
                "  Клик левой кнопкой      маркер A, затем B, затем\r\n" +
                "                          цикл заново (клик в любом\r\n" +
                "                          месте графика)\r\n" +
                "  Протяжка левой кнопкой  рамка зума (обе оси)\r\n" +
                "  Средняя кнопка          панорама (левая — тоже, при зуме)\r\n" +
                "  Двойной клик            сброс вида и маркеров\r\n" +
                "\r\n" +
                "КОЛЕСО МЫШИ\r\n" +
                "\r\n" +
                "  Ctrl+колесо             зум обеих осей к курсору\r\n" +
                "  Alt+колесо              растяжение/сжатие по горизонтали\r\n" +
                "  Alt+Shift+колесо        растяжение/сжатие по вертикали\r\n" +
                "  Shift+колесо            навигация влево/вправо\r\n" +
                "  Колесо                  навигация вверх/вниз\r\n" +
                "\r\n" +
                "ПРОЧЕЕ\r\n" +
                "\r\n" +
                "  F1                      эта справка\r\n" +
                "  Правый клик по графику  экспорт PNG / SVG / CSV\r\n" +
                "  Enter в строке отправки  отправить в порт\r\n" +
                "\r\n" +
                "РЕЖИМ «3D»\r\n" +
                "\r\n" +
                "  Перетаскивание левой    вращение сцены\r\n" +
                "\r\n" +
                "МАРКЕРЫ\r\n" +
                "\r\n" +
                "  Между A и B: Δt в точках и миллисекундах\r\n" +
                "  (частота из FFT-настроек) и ΔV по каналам.\r\n",
        };

        Controls.Add(text);
        HandleCreated += (_, _) => Theme.ApplyTitleBar(this);
        Theme.Apply(this);

        FormClosed += (_, _) => _open = null;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.F1 || keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessDialogKey(keyData);
    }
}
