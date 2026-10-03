namespace SerialPlotter;

// Тёмная/светлая тема: цвета интерфейса и палитра каналов.
public static class Theme
{
    public static bool Dark = true;

    public static Color Bg { get; private set; }
    public static Color PlotBg { get; private set; }
    public static Color Grid { get; private set; }
    public static Color Text { get; private set; }
    public static Color Axis { get; private set; }
    public static Color ButtonBg => Dark ? Color.FromArgb(45, 47, 52) : Color.FromArgb(240, 240, 240);

    // Палитра каналов по умолчанию
    public static readonly Color[] Palette =
    {
        ColorTranslator.FromHtml("#1E6FBA"), ColorTranslator.FromHtml("#298D49"),
        ColorTranslator.FromHtml("#D64541"), ColorTranslator.FromHtml("#E67E22"),
        ColorTranslator.FromHtml("#8E44AD"), ColorTranslator.FromHtml("#16A085"),
        ColorTranslator.FromHtml("#F1C40F"), ColorTranslator.FromHtml("#E84393"),
    };

    static Theme() => Recalc();

    private static void Recalc()
    {
        Bg = Dark ? Color.FromArgb(30, 31, 34) : Color.White;
        PlotBg = Dark ? Color.FromArgb(38, 40, 44) : Color.FromArgb(250, 250, 250);
        Grid = Dark ? Color.FromArgb(58, 61, 66) : Color.FromArgb(230, 230, 230);
        Text = Dark ? Color.FromArgb(200, 203, 210) : Color.Gray;
        Axis = Dark ? Color.FromArgb(95, 99, 110) : ColorTranslator.FromHtml("#AAB7C6");
    }

    public static void Apply(Control root)
    {
        Recalc();
        root.BackColor = Bg;
        StyleChildren(root);
    }

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
                case Label or CheckBox:
                    c.ForeColor = Text;
                    break;
                case Panel p:
                    p.BackColor = Bg;
                    StyleChildren(p);
                    break;
            }
        }
    }
}
