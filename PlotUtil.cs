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

    // Гауссово сглаживание: взвешенное среднее с ядром exp(-k²/2σ²)
    public static double[] GaussianFilter(double[] data, int w)
    {
        w = Math.Clamp(w | 1, 3, 501);
        int n = data.Length, half = w / 2;
        double sigma = w / 6.0; // края ядра ~0 при |k|>3σ

        var kernel = new double[w];
        double sum = 0;
        for (int k = -half; k <= half; k++)
        {
            double v = Math.Exp(-k * k / (2 * sigma * sigma));
            kernel[k + half] = v;
            sum += v;
        }
        for (int k = 0; k < w; k++) kernel[k] /= sum;

        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double acc = 0, wsum = 0;
            for (int k = -half; k <= half; k++)
            {
                int j = i + k;
                if (j < 0 || j >= n) continue;
                double kw = kernel[k + half];
                acc += data[j] * kw;
                wsum += kw;
            }
            result[i] = acc / wsum; // на краях — перенормировка
        }
        return result;
    }

    // Экспоненциальное сглаживание (EMA): alpha из окна, один проход
    public static double[] ExponentialFilter(double[] data, int w)
    {
        double alpha = 2.0 / (Math.Clamp(w | 1, 3, 501) + 1);
        int n = data.Length;
        var result = new double[n];
        if (n == 0) return result;
        double acc = data[0];
        result[0] = acc;
        for (int i = 1; i < n; i++)
        {
            acc = alpha * data[i] + (1 - alpha) * acc;
            result[i] = acc;
        }
        return result;
    }
}
