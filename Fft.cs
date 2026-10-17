namespace SerialPlotter;

// Итеративный radix-2 БПФ (Кули–Тьюки) с побитовой перестановкой.
// Вход — n комплексных отсчётов, n — степень двойки; результат на месте.
public static class Fft
{
    public static void Compute(Span<(double Re, double Im)> a)
    {
        int n = a.Length;
        // Побитовая перестановка
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wLenRe = Math.Cos(ang), wLenIm = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double wRe = 1, wIm = 0;
                for (int j = 0; j < len / 2; j++)
                {
                    var (uRe, uIm) = a[i + j];
                    double vRe = a[i + j + len / 2].Re * wRe - a[i + j + len / 2].Im * wIm;
                    double vIm = a[i + j + len / 2].Re * wIm + a[i + j + len / 2].Im * wRe;
                    a[i + j] = (uRe + vRe, uIm + vIm);
                    a[i + j + len / 2] = (uRe - vRe, uIm - vIm);
                    (wRe, wIm) = (wRe * wLenRe - wIm * wLenIm, wRe * wLenIm + wIm * wLenRe);
                }
            }
        }
    }

    // Спектр последних samples.Length отсчётов: окно Ханна + БПФ + амплитуды.
    // Возвращает n/2+1 амплитуд. Нормировка на сумму окна компенсирует
    // когерентное усиление Ханна (0.5) — амплитуда синуса читается напрямую.
    public static double[] MagnitudeSpectrum(double[] samples)
    {
        int n = samples.Length;
        var a = new (double Re, double Im)[n];
        double winSum = 0;
        for (int i = 0; i < n; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n - 1)); // Ханн
            winSum += w;
            a[i] = (samples[i] * w, 0);
        }
        Compute(a);

        var mag = new double[n / 2 + 1];
        for (int k = 0; k <= n / 2; k++)
            mag[k] = 2.0 * Math.Sqrt(a[k].Re * a[k].Re + a[k].Im * a[k].Im) / winSum;
        mag[0] /= 2; // DC не удваивается
        return mag;
    }
}
