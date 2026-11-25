namespace SerialPlotter.Blazor.Core;

public enum FftWindowKind
{
    Rectangular,
    Hann,
    Hamming,
    Blackman,
    BlackmanHarris,
}

public static class Fft
{
    public static double[] Window(FftWindowKind kind, int n)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
        {
            double x = 2.0 * Math.PI * i / (n - 1);
            values[i] = kind switch
            {
                FftWindowKind.Hann => 0.5 - 0.5 * Math.Cos(x),
                FftWindowKind.Hamming => 0.54 - 0.46 * Math.Cos(x),
                FftWindowKind.Blackman => 0.42 - 0.5 * Math.Cos(x) + 0.08 * Math.Cos(2 * x),
                FftWindowKind.BlackmanHarris => 0.35875 - 0.48829 * Math.Cos(x)
                    + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x),
                _ => 1.0,
            };
        }
        return values;
    }

    public static void Compute(Span<(double Re, double Im)> values)
    {
        int n = values.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
                (values[i], values[j]) = (values[j], values[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            double wLenRe = Math.Cos(angle), wLenIm = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double wRe = 1, wIm = 0;
                for (int j = 0; j < len / 2; j++)
                {
                    var (uRe, uIm) = values[i + j];
                    double vRe = values[i + j + len / 2].Re * wRe - values[i + j + len / 2].Im * wIm;
                    double vIm = values[i + j + len / 2].Re * wIm + values[i + j + len / 2].Im * wRe;
                    values[i + j] = (uRe + vRe, uIm + vIm);
                    values[i + j + len / 2] = (uRe - vRe, uIm - vIm);
                    (wRe, wIm) = (wRe * wLenRe - wIm * wLenIm, wRe * wLenIm + wIm * wLenRe);
                }
            }
        }
    }

    public static double[] MagnitudeSpectrum(double[] samples, FftWindowKind window = FftWindowKind.Hann)
    {
        int n = samples.Length;
        var values = new (double Re, double Im)[n];
        double[] coefficients = Window(window, n);
        double coefficientSum = 0;
        for (int i = 0; i < n; i++)
        {
            coefficientSum += coefficients[i];
            values[i] = (samples[i] * coefficients[i], 0);
        }
        Compute(values);

        var magnitude = new double[n / 2 + 1];
        for (int k = 0; k <= n / 2; k++)
            magnitude[k] = 2.0 * Math.Sqrt(values[k].Re * values[k].Re + values[k].Im * values[k].Im) / coefficientSum;
        magnitude[0] /= 2;
        return magnitude;
    }
}
