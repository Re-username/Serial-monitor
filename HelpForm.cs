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
        Text = Lang.T("Справка — навигация и горячие клавиши", "Help — navigation and shortcuts");
        ClientSize = new Size(430, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        string textRu =
            "РЕЖИМ «ВРЕМЯ» — МЫШЬ\r\n" +
            "\r\n" +
            "  Клик левой кнопкой      маркер A, затем B, затем\r\n" +
            "                          цикл заново (клик в любом\r\n" +
            "                          месте графика)\r\n" +
            "  Протяжка левой кнопкой  рамка зума (обе оси)\r\n" +
            "  Средняя кнопка          панорама\r\n" +
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
            "  (частота из FFT-настроек) и ΔV по каналам.\r\n";

        string textEn =
            "TIME MODE — MOUSE\r\n" +
            "\r\n" +
            "  Left click              marker A, then B, then\r\n" +
            "                          the cycle restarts (click\r\n" +
            "                          anywhere on the plot)\r\n" +
            "  Left drag               zoom rectangle (both axes)\r\n" +
            "  Middle button           pan\r\n" +
            "  Double click            reset view and markers\r\n" +
            "\r\n" +
            "MOUSE WHEEL\r\n" +
            "\r\n" +
            "  Ctrl+wheel              zoom both axes to cursor\r\n" +
            "  Alt+wheel               stretch/compress horizontally\r\n" +
            "  Alt+Shift+wheel         stretch/compress vertically\r\n" +
            "  Shift+wheel             navigate left/right\r\n" +
            "  Wheel                   navigate up/down\r\n" +
            "\r\n" +
            "OTHER\r\n" +
            "\r\n" +
            "  F1                      this help\r\n" +
            "  Right-click on plot     export PNG / SVG / CSV\r\n" +
            "  Enter in the send box   send to port\r\n" +
            "\r\n" +
            "3D MODE\r\n" +
            "\r\n" +
            "  Left drag               rotate the scene\r\n" +
            "\r\n" +
            "MARKERS\r\n" +
            "\r\n" +
            "  Between A and B: Δt in points and milliseconds\r\n" +
            "  (rate from FFT settings) and ΔV per channel.\r\n";

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
            Text = Lang.English ? textEn : textRu,
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
