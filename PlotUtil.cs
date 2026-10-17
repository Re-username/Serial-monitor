using SvgNet.Interfaces;

namespace SerialPlotter;

// Общие мелкие хелперы для отрисовщиков.
internal static class PlotUtil
{
    public static void Hint(IGraphics g, Rectangle rect, string message)
    {
        using var font = new Font("Segoe UI", 12f);
        using var brush = new SolidBrush(Theme.Text);
        var size = Measure(g, message, font);
        g.DrawString(message, font, brush,
                     rect.Left + (rect.Width - size.Width) / 2,
                     rect.Top + rect.Height / 2 - 20);
    }

    // Векторный холст SVG не умеет измерять текст — подставляем оценку по длине строки.
    public static SizeF Measure(IGraphics g, string text, Font font)
    {
        try
        {
            return g.MeasureString(text, font);
        }
        catch (NotImplementedException)
        {
            return new SizeF(text.Length * font.Size * 0.62f, font.Size * 1.4f);
        }
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

    // Скользящее среднее окном w (через префиксные суммы, O(n))
    public static double[] MovingAverage(double[] data, int w)
    {
        w = Math.Clamp(w | 1, 3, 1001);
        int n = data.Length, half = w / 2;
        var prefix = new double[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + data[i];
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - half), hi = Math.Min(n, i + half + 1);
            result[i] = (prefix[hi] - prefix[lo]) / (hi - lo);
        }
        return result;
    }

    // Медианный фильтр окном w — маленькие окна (до 31), иначе дорого
    public static double[] MedianFilter(double[] data, int w)
    {
        w = Math.Clamp(w | 1, 3, 31);
        int n = data.Length, half = w / 2;
        var result = new double[n];
        var window = new double[w];
        for (int i = 0; i < n; i++)
        {
            int m = 0;
            for (int k = Math.Max(0, i - half); k <= Math.Min(n - 1, i + half); k++)
                window[m++] = data[k];
            Array.Sort(window, 0, m);
            result[i] = m % 2 == 1 ? window[m / 2] : (window[m / 2 - 1] + window[m / 2]) / 2;
        }
        return result;
    }
}
