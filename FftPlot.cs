using SvgNet.Interfaces;
using System.Drawing.Drawing2D;

namespace SerialPlotter;

// FFT-режим: спектр первого видимого канала по последним n отсчётам.
// Ось X — частота 0..Fs/2, ось Y — амплитуда (линейная) или дБ (логарифм).
// Частота дискретизации Fs задаётся в тулбаре (по умолчанию 50 Гц —
// скорость симулятора; для железа — фактическая частота пакетов).
public static class FftPlot
{
    private const int MaxN = 4096; // максимальный размер БПФ

    public static void Draw(IGraphics g, Rectangle rect, PlotContext ctx)
    {
        using (var brush = new SolidBrush(Theme.PlotBg))
            g.FillRectangle(brush, rect);
        using var gridPen = new Pen(Theme.Grid);
        using var textBrush = new SolidBrush(Theme.Text);

        var ch = ctx.Visible.FirstOrDefault();
        if (ch == null)
        {
            PlotUtil.Hint(g, rect, Lang.T("Нет видимых каналов — спектр строится по первому видимому", "No visible channels — the spectrum uses the first visible one"));
            return;
        }

        int count = ch.Data.Count;
        int n = 1;
        while (n * 2 <= Math.Min(count, MaxN)) n <<= 1;

        if (n < 16)
        {
            PlotUtil.Hint(g, rect, $"{Lang.T("Спектр", "Spectrum")} {ch.Name}: {Lang.T("мало данных (нужно минимум 16 точек, есть", "not enough data (need at least 16 points, have")} {count})");
            return;
        }

        double fs = Math.Max(PlotSettings.SampleRate, 1e-6);
        double[] mag = Fft.MagnitudeSpectrum(ch.Data.TakeLast(n).ToArray());
        bool log = PlotSettings.FftLog;

        // Вертикальная шкала
        double dymax = mag[1..].Max();
        if (dymax <= 0) dymax = 1;
        double ymin, ymax;
        if (log)
        {
            ymax = 20 * Math.Log10(dymax);
            ymin = ymax - 80; // окно 80 дБ
        }
        else
        {
            ymin = 0;
            ymax = dymax * 1.1;
        }

        // Сетка и подписи: 4 горизонтали, 8 вертикалей
        using var font = new Font("Segoe UI", 10f);
        for (int i = 0; i <= 4; i++)
        {
            int gy = rect.Top + rect.Height * i / 4;
            g.DrawLine(gridPen, rect.Left, gy, rect.Right, gy);
            double v = ymax - (ymax - ymin) * i / 4;
            string lbl = log ? $"{v:F0} {Lang.T("дБ", "dB")}" : v.ToString("0.##");
            g.DrawString(lbl, font, textBrush, rect.Left - 52, gy - 8);
        }
        for (int i = 0; i <= 8; i++)
        {
            int gx = rect.Left + rect.Width * i / 8;
            g.DrawLine(gridPen, gx, rect.Top, gx, rect.Bottom);
            if (i > 0)
            {
                double hz = fs / 2 * i / 8;
                string lbl = hz >= 1000 ? $"{hz / 1000:0.##} {Lang.T("кГц", "kHz")}" : $"{hz:0.#} {Lang.T("Гц", "Hz")}";
                var size = PlotUtil.Measure(g, lbl, font);
                g.DrawString(lbl, font, textBrush, gx - size.Width / 2, rect.Bottom + 4);
            }
        }

        // Спектр
        int bins = mag.Length;
        var pts = new PointF[bins];
        for (int k = 0; k < bins; k++)
        {
            double v = log
                ? Math.Max(20 * Math.Log10(Math.Max(mag[k], 1e-12)), ymin)
                : mag[k];
            float px = rect.Left + (float)(rect.Width * k / (bins - 1.0));
            float py = rect.Bottom - (float)(rect.Height * (v - ymin) / (ymax - ymin));
            pts[k] = new PointF(px, Math.Clamp(py, rect.Top, rect.Bottom));
        }
        using var pen = new Pen(ch.Color, 2) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);

        // Пик (кроме DC) и шапка
        int peak = 1;
        for (int k = 2; k < bins; k++)
            if (mag[k] > mag[peak]) peak = k;
        double peakHz = fs / 2.0 * peak / (bins - 1);
        string peakDb = log ? $", {20 * Math.Log10(Math.Max(mag[peak], 1e-12)):F0} {Lang.T("дБ", "dB")}" : "";
        using var legendFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var legendBrush = new SolidBrush(ch.Color);
        g.DrawString($"{ch.Name}: {Lang.T("пик", "peak")} {peakHz:0.##} {Lang.T("Гц", "Hz")}{peakDb}",
                     legendFont, legendBrush, rect.Left + 10, rect.Top + 8);

        using var hintFont = new Font("Segoe UI", 9f);
        g.DrawString($"FFT {n} {Lang.T("точек", "pts")}, Δf = {fs / n:0.###} {Lang.T("Гц", "Hz")}   " +
                     (log ? Lang.T("логарифмическая шкала", "logarithmic scale") : Lang.T("линейная шкала", "linear scale")),
                     hintFont, textBrush, rect.Left + 10, rect.Top + 28);

        PlotSettings.LastXMin = 0;
        PlotSettings.LastXMax = fs / 2;
        PlotSettings.LastYMin = ymin;
        PlotSettings.LastYMax = ymax;
    }
}
