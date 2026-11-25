namespace SerialPlotter.Blazor.Core;

public static class PlotMath
{
    public static void Range(IEnumerable<double> data, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        foreach (double value in data)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }
        if (min == double.MaxValue)
        {
            min = 0;
            max = 1;
        }
        if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }
    }

    public static double[] Smooth(double[] data, SmoothKind kind, int window) => kind switch
    {
        SmoothKind.MovingAverage => MovingAverage(data, window),
        SmoothKind.Median => MedianFilter(data, window),
        SmoothKind.Gaussian => GaussianFilter(data, window),
        SmoothKind.Exponential => ExponentialFilter(data, window),
        _ => data,
    };

    public static double[] MovingAverage(double[] data, int window)
    {
        window = Math.Clamp(window | 1, 3, 1001);
        int n = data.Length, half = window / 2;
        var prefix = new double[n + 1];
        for (int i = 0; i < n; i++)
            prefix[i + 1] = prefix[i] + data[i];
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - half), hi = Math.Min(n, i + half + 1);
            result[i] = (prefix[hi] - prefix[lo]) / (hi - lo);
        }
        return result;
    }

    public static double[] MedianFilter(double[] data, int window)
    {
        window = Math.Clamp(window | 1, 3, 31);
        int n = data.Length, half = window / 2;
        var result = new double[n];
        var values = new double[window];
        for (int i = 0; i < n; i++)
        {
            int count = 0;
            for (int k = Math.Max(0, i - half); k <= Math.Min(n - 1, i + half); k++)
                values[count++] = data[k];
            Array.Sort(values, 0, count);
            result[i] = count % 2 == 1
                ? values[count / 2]
                : (values[count / 2 - 1] + values[count / 2]) / 2;
        }
        return result;
    }

    public static double[] GaussianFilter(double[] data, int window)
    {
        window = Math.Clamp(window | 1, 3, 501);
        int n = data.Length, half = window / 2;
        double sigma = window / 6.0;
        var kernel = new double[window];
        double sum = 0;
        for (int k = -half; k <= half; k++)
        {
            double value = Math.Exp(-k * k / (2 * sigma * sigma));
            kernel[k + half] = value;
            sum += value;
        }
        for (int k = 0; k < window; k++)
            kernel[k] /= sum;

        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double acc = 0, weight = 0;
            for (int k = -half; k <= half; k++)
            {
                int j = i + k;
                if (j < 0 || j >= n)
                    continue;
                double kw = kernel[k + half];
                acc += data[j] * kw;
                weight += kw;
            }
            result[i] = acc / weight;
        }
        return result;
    }

    public static double[] ExponentialFilter(double[] data, int window)
    {
        double alpha = 2.0 / (Math.Clamp(window | 1, 3, 501) + 1);
        var result = new double[data.Length];
        if (data.Length == 0)
            return result;
        double acc = data[0];
        result[0] = acc;
        for (int i = 1; i < data.Length; i++)
        {
            acc = alpha * data[i] + (1 - alpha) * acc;
            result[i] = acc;
        }
        return result;
    }
}
