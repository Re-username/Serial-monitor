namespace SerialPlotter;

// Общие мелкие хелперы для отрисовщиков.
internal static class PlotUtil
{
    public static void Hint(Graphics g, Rectangle rect, string message)
    {
        using var font = new Font("Segoe UI", 12f);
        using var brush = new SolidBrush(Theme.Text);
        var size = g.MeasureString(message, font);
        g.DrawString(message, font, brush,
                     rect.Left + (rect.Width - size.Width) / 2,
                     rect.Top + rect.Height / 2 - 20);
    }

    public static void Range(IEnumerable<double> data, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        foreach (double v in data)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (min == double.MaxValue) { min = 0; max = 1; }
        if (max - min < 1e-9) { min -= 1; max += 1; } // не делим на ноль
    }
}
