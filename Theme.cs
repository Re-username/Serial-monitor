namespace SerialPlotter;

// Вид темы: классическая тёмная, светлая и неоновая киберпанк
// (по мотивам темы MathArt: чёрный фон, циан/пурпур/магента).
public enum ThemeKind { Dark, Light, Cyberpunk }

// Тёмная/светлая/киберпанк тема: цвета интерфейса и палитра каналов.
public static class Theme
{
    // По умолчанию — неоновая киберпанк
    public static ThemeKind Kind = ThemeKind.Cyberpunk;

    // «Тёмная» в широком смысле: киберпанк тоже тёмный (тёмная шапка и т.п.)
    public static bool Dark => Kind != ThemeKind.Light;

    public static Color Bg { get; private set; }
    public static Color PlotBg { get; private set; }
    public static Color Grid { get; private set; }
    public static Color Text { get; private set; }
    public static Color Axis { get; private set; }
    // Акцентный цвет: рамки кнопок, выделения — в киберпанке неоновый циан
    public static Color Accent { get; private set; }
    public static Color ButtonBg => Kind switch
    {
        ThemeKind.Cyberpunk => Color.FromArgb(20, 20, 26),
        ThemeKind.Light => Color.FromArgb(240, 240, 240),
        _ => Color.FromArgb(45, 47, 52),
    };

    // Палитра каналов по умолчанию
    private static readonly Color[] DefaultPalette =
    {
        ColorTranslator.FromHtml("#1E6FBA"), ColorTranslator.FromHtml("#298D49"),
        ColorTranslator.FromHtml("#D64541"), ColorTranslator.FromHtml("#E67E22"),
        ColorTranslator.FromHtml("#8E44AD"), ColorTranslator.FromHtml("#16A085"),
        ColorTranslator.FromHtml("#F1C40F"), ColorTranslator.FromHtml("#E84393"),
    };

    // Неоновая палитра киберпанка (MathArt): циан, магента, зелёный, пурпур…
    private static readonly Color[] CyberPalette =
    {
        ColorTranslator.FromHtml("#2CC9FF"), ColorTranslator.FromHtml("#FF2BD6"),
        ColorTranslator.FromHtml("#A2DB3C"), ColorTranslator.FromHtml("#9D6BFF"),
        ColorTranslator.FromHtml("#FF3B5C"), ColorTranslator.FromHtml("#FFD319"),
        ColorTranslator.FromHtml("#00FFC8"), ColorTranslator.FromHtml("#FF7A18"),
    };

    public static Color[] Palette => Kind == ThemeKind.Cyberpunk ? CyberPalette : DefaultPalette;

    static Theme() => Recalc();

    private static void Recalc()
    {
        switch (Kind)
        {
            case ThemeKind.Light:
                Bg = Color.White;
                PlotBg = Color.FromArgb(250, 250, 250);
                Grid = Color.FromArgb(230, 230, 230);
                Text = Color.Gray;
                Axis = ColorTranslator.FromHtml("#AAB7C6");
                Accent = Color.FromArgb(160, 160, 160);
                break;
            case ThemeKind.Cyberpunk:
                Bg = Color.FromArgb(10, 10, 14);
                PlotBg = Color.Black;
                Grid = Color.FromArgb(18, 42, 52);
                Text = Color.FromArgb(235, 235, 235);
                Axis = Color.FromArgb(80, 44, 201, 255); // полупрозрачный циан
                Accent = ColorTranslator.FromHtml("#2CC9FF");
                break;
            default:
                Bg = Color.FromArgb(30, 31, 34);
                PlotBg = Color.FromArgb(38, 40, 44);
                Grid = Color.FromArgb(58, 61, 66);
                Text = Color.FromArgb(200, 203, 210);
                Axis = Color.FromArgb(95, 99, 110);
                Accent = Color.FromArgb(86, 91, 102);
                break;
        }
    }

    public static void Apply(Control root)
    {
        Recalc();
        root.BackColor = Bg;
        StyleChildren(root);
    }

    // Тёмная/светлая шапка окна (Windows 10 1809+ / Windows 11).
    // Вызывать, когда хендл окна уже создан.
    public static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated || form.IsDisposed) return;
        int value = Dark ? 1 : 0;
        try
        {
            DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)); // Win 11 / свежий Win 10
            DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int)); // старый Win 10
        }
        catch { /* DWM недоступен — останется системная шапка */ }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void StyleChildren(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Button btn when btn.Tag is not Channel:
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = ButtonBg;
                    btn.ForeColor = Text;
                    btn.FlatAppearance.BorderColor = Accent;
                    btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(ButtonBg, 0.15f);
                    break;
                case Button swatch:
                    swatch.FlatStyle = FlatStyle.Flat; // цвет задаёт канал, не трогаем
                    break;
                case ComboBox box:
                    box.BackColor = PlotBg;
                    box.ForeColor = Text;
                    break;
                case TextBoxBase tb:
                    tb.BackColor = Bg;
                    tb.ForeColor = Text;
                    break;
                case NumericUpDown num:
                    num.BackColor = PlotBg;
                    num.ForeColor = Text;
                    break;
                case GroupBox gb:
                    gb.ForeColor = Text;
                    StyleChildren(gb);
                    break;
                case Panel p:
                    p.BackColor = Bg;
                    StyleChildren(p);
                    break;
                case Label or CheckBox:
                    c.ForeColor = Text;
                    break;
                default:
                    StyleChildren(c); // прочие контейнеры (UserControl, TabPage и т.п.)
                    break;
            }
        }
    }
}
