using SvgNet.Interfaces;
using System.Drawing.Drawing2D;

namespace SerialPlotter;

// Режим «Время»: графики каналов по времени.
// Если в менеджере каналов на ось X выбран канал — превращается в XY-график:
// по горизонтали выбранный канал, по вертикали все остальные видимые.
//
// Ось времени — абсолютный индекс точки. Автоокно (PlotSettings.AutoWindow)
// прижато к правому краю: новые точки появляются справа, заполненное окно
// прокручивается влево. Зум/панорама (ViewX/ViewY) показывают историю
// глубиной DataHub.HISTORY. Сглаживание каналов рисуется отдельным слоем.
public static class TimePlot
{
    public static void Draw(IGraphics g, Rectangle rect, PlotContext ctx)
    {
        using (var brush = new SolidBrush(Theme.PlotBg))
            g.FillRectangle(brush, rect);
        using var gridPen = new Pen(Theme.Grid);
        using var textBrush = new SolidBrush(Theme.Text);

        for (int i = 0; i <= 4; i++)
        {
            int gy = rect.Top + rect.Height * i / 4;
            g.DrawLine(gridPen, rect.Left, gy, rect.Right, gy);
        }
        for (int i = 0; i <= 8; i++)
        {
            int gx = rect.Left + rect.Width * i / 8;
            g.DrawLine(gridPen, gx, rect.Top, gx, rect.Bottom);
        }

        var ys = ctx.YChannels.ToList();
        if (ys.Count == 0)
        {
            PlotSettings.LastXMin = 0; PlotSettings.LastXMax = 1;
            PlotSettings.LastYMin = 0; PlotSettings.LastYMax = 1;
            PlotUtil.Hint(g, rect, Lang.T("Нет видимых каналов — отметь их в панели справа", "No visible channels — check them in the panel on the right"));
            return;
        }

        bool timeMode = ctx.XChannel == null;
        int count = ctx.Visible.Max(c => c.Data.Count);

        // Ось X: время (абсолютный индекс точки) или выбранный канал
        Channel? xc = ctx.XChannel;
        double[]? xs = null;
        double xmin, xmax;

        if (xc != null)
        {
            xs = xc.Data.ToArray();
            if (xs.Length < 2)
            {
                PlotUtil.Hint(g, rect, $"{Lang.T("Нет данных по оси X", "No data on axis X")} ({xc.Name})");
                return;
            }
            PlotUtil.Range(xs, out double dxmin, out double dxmax);
            (xmin, xmax) = PlotSettings.ResolveX(PlotSettings.XAuto, dxmin, dxmax, timeMode: false);
        }
        else
        {
            // Автоокно прижато к правому краю: последние N точек
            double w = PlotSettings.XAuto
                ? Math.Min(Math.Max(count, 2), PlotSettings.AutoWindow)
                : Math.Clamp(Math.Max(PlotSettings.XMax, PlotSettings.XMin), 2, DataHub.HISTORY);
            xmax = Math.Max(count - 1, 1);
            xmin = Math.Max(0, xmax - w + 1);
        }

        // Зум/панорама перекрывает всё
        if (PlotSettings.ViewXMin is { } vx0 && PlotSettings.ViewXMax is { } vx1 && vx1 > vx0)
        {
            xmin = timeMode ? Math.Max(0, vx0) : vx0;
            xmax = vx1;
        }
        if (xmax - xmin < 1e-9) xmax = xmin + 1;

        // Общий масштаб по вертикали для всех видимых каналов
        double dymin = double.MaxValue, dymax = double.MinValue;
        foreach (var ch in ys)
        {
            PlotUtil.Range(ch.Data, out double cmin, out double cmax);
            if (cmin < dymin) dymin = cmin;
            if (cmax > dymax) dymax = cmax;
        }
        if (dymax - dymin < 1e-9) { dymin -= 1; dymax += 1; }
        var (ymin, ymax) = PlotSettings.ResolveY(PlotSettings.YAuto, dymin, dymax);
        if (PlotSettings.ViewYMin is { } vy0 && PlotSettings.ViewYMax is { } vy1 && vy1 > vy0)
            (ymin, ymax) = (vy0, vy1);
        if (ymax - ymin < 1e-9) ymax = ymin + 1;

        // Реально использованные пределы — для пересчёта мыши в значения
        PlotSettings.LastXMin = xmin; PlotSettings.LastXMax = xmax;
        PlotSettings.LastYMin = ymin; PlotSettings.LastYMax = ymax;

        using var centerPen = new Pen(Theme.Axis) { DashStyle = DashStyle.Dash };
        double ymid = (ymin + ymax) / 2;
        int cy = rect.Bottom - (int)(rect.Height * (ymid - ymin) / (ymax - ymin));
        g.DrawLine(centerPen, rect.Left, cy, rect.Right, cy);

        if (xc != null)
        {
            double xmid = (xmin + xmax) / 2;
            int cx2 = rect.Left + (int)(rect.Width * (xmid - xmin) / (xmax - xmin));
            g.DrawLine(centerPen, cx2, rect.Top, cx2, rect.Bottom);
        }

        using var font = new Font("Segoe UI", 10f);
        g.DrawString(ymax.ToString("0.##"), font, textBrush, rect.Left - 45, rect.Top - 6);
        g.DrawString(ymid.ToString("0.##"), font, textBrush, rect.Left - 45, cy - 8);
        g.DrawString(ymin.ToString("0.##"), font, textBrush, rect.Left - 45, rect.Bottom - 10);

        // Подписи диапазона по оси X
        if (xc != null)
        {
            g.DrawString(xmin.ToString("0.##"), font, textBrush, rect.Left, rect.Bottom + 4);
            string maxLabel = xmax.ToString("0.##");
            g.DrawString(maxLabel, font, textBrush,
                         rect.Right - PlotUtil.Measure(g, maxLabel, font).Width, rect.Bottom + 4);
            string nameLabel = $"{Lang.T("ось X", "axis X")}: {xc.Name}";
            g.DrawString(nameLabel, font, textBrush,
                         rect.Left + (rect.Width - PlotUtil.Measure(g, nameLabel, font).Width) / 2,
                         rect.Bottom + 4);
        }
        else
        {
            g.DrawString($"{(long)Math.Max(0, xmin)}", font, textBrush, rect.Left, rect.Bottom + 4);
            string maxLabel = $"{(long)xmax + 1} {Lang.T("точ.", "pts")}";
            g.DrawString(maxLabel, font, textBrush,
                         rect.Right - PlotUtil.Measure(g, maxLabel, font).Width, rect.Bottom + 4);

            // Время под числами точек — сколько секунд на этих позициях
            double sr = Math.Max(PlotSettings.SampleRate, 1e-6);
            using var timeFont = new Font("Segoe UI", 8.5f);
            using var timeBrush = new SolidBrush(Theme.Axis);
            string tLeft = $"{Math.Max(0, xmin) / sr:0.#} {Lang.T("с", "s")}";
            g.DrawString(tLeft, timeFont, timeBrush, rect.Left, rect.Bottom + 18);
            string tRight = $"{(xmax + 1) / sr:0.#} {Lang.T("с", "s")}";
            g.DrawString(tRight, timeFont, timeBrush,
                         rect.Right - PlotUtil.Measure(g, tRight, timeFont).Width, rect.Bottom + 18);
        }

        // Тонкая оранжевая линия живого края: отсюда график сейчас строится.
        // Ориентир при панораме — видно, где кончаются данные. Позиция — по
        // фактическим данным (самая свежая точка), а не по глобальному
        // счётчику: после полного сброса/перезапуска симулятора счётчик
        // продолжает расти, и линия по нему улетала вправо от данных
        if (xs == null && count > 0)
        {
            double edgeX = count - 1;
            if (edgeX >= xmin && edgeX <= xmax)
            {
                int sx = rect.Left + (int)(rect.Width * (edgeX - xmin) / (xmax - xmin));
                using var startPen = new Pen(Color.Orange, 1);
                g.DrawLine(startPen, sx, rect.Top, sx, rect.Bottom);
                using var startFont = new Font("Segoe UI", 8f);
                using var startBrush = new SolidBrush(Color.Orange);
                string edgeLabel = Lang.T("сейчас", "live");
                g.DrawString(edgeLabel, startFont, startBrush,
                             sx - PlotUtil.Measure(g, edgeLabel, startFont).Width - 3, rect.Top + 2);
            }
        }

        // Линии графиков (и слой сглаживания поверх)
        foreach (var ch in ys)
        {
            var data = ch.Data.ToArray();
            if (data.Length < 2) continue;
            using var pen = new Pen(ch.Color, ch.LineWidth) { DashStyle = ch.LineStyle.ToDashStyle() };

            if (xs == null)
            {
                // Ось X — время: точки с абсолютными индексами из [xmin, xmax]
                int k0 = Math.Max(0, (int)Math.Ceiling(xmin));
                int k1 = Math.Min(data.Length - 1, (int)Math.Floor(xmax));
                if (k1 < k0) continue;
                if (EffectiveSmooth(ch, out int win))
                {
                    // Сглаживание включено — оригинальную линию не рисуем,
                    // показываем только сглаженный слой
                    DrawSmoothed(g, data, ch, win, rect, xmin, xmax, ymin, ymax,
                                 sm => MapTime(sm, k0, k1, xmin, xmax, ymin, ymax, rect));
                }
                else
                {
                    var points = MapTime(data, k0, k1, xmin, xmax, ymin, ymax, rect);
                    g.DrawLines(pen, points);
                }
            }
            else
            {
                // XY-режим: пары (x[i], y[i])
                int n = Math.Min(xs.Length, data.Length);
                if (n < 2) continue;
                if (EffectiveSmooth(ch, out int win))
                {
                    DrawSmoothed(g, data, ch, win, rect, xmin, xmax, ymin, ymax,
                                 sm => MapXy(xs, sm, n, xmin, xmax, ymin, ymax, rect));
                }
                else
                {
                    var points = MapXy(xs, data, n, xmin, xmax, ymin, ymax, rect);
                    g.DrawLines(pen, points);
                }
            }
        }

        if (!ctx.ForExport)
        {
            DrawMarkers(g, rect, ys, xs, xmin, xmax, ymin, ymax);
            DrawRuler(g, rect, ctx, ys, xs, xmin, xmax, ymin, ymax);
        }
    }

    // Отображение индексов k0..k1 на область графика (ось времени)
    private static PointF[] MapTime(double[] data, int k0, int k1,
                                    double xmin, double xmax, double ymin, double ymax, Rectangle rect)
    {
        var points = new PointF[k1 - k0 + 1];
        for (int k = k0; k <= k1; k++)
        {
            float px = rect.Left + (float)(rect.Width * (k - xmin) / (xmax - xmin));
            float py = rect.Bottom - (float)(rect.Height * (data[k] - ymin) / (ymax - ymin));
            points[k - k0] = new PointF(px, Math.Clamp(py, rect.Top, rect.Bottom));
        }
        return points;
    }

    // Отображение пар (x, y) на область графика (XY-режим)
    private static PointF[] MapXy(double[] xs, double[] data, int n,
                                  double xmin, double xmax, double ymin, double ymax, Rectangle rect)
    {
        var points = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float px = rect.Left + (float)(rect.Width * (xs[i] - xmin) / (xmax - xmin));
            float py = rect.Bottom - (float)(rect.Height * (data[i] - ymin) / (ymax - ymin));
            points[i] = new PointF(Math.Clamp(px, rect.Left, rect.Right),
                                   Math.Clamp(py, rect.Top, rect.Bottom));
        }
        return points;
    }

    // Слой сглаживания поверх исходной линии: полупрозрачная утолщённая линия
    // Эффективное сглаживание канала: своё, иначе глобальное из меню
    // «Инструменты». win — эффективное окно.
    private static bool EffectiveSmooth(Channel ch, out int win)
    {
        var kind = ch.Smooth != SmoothKind.None ? ch.Smooth : PlotSettings.GlobalSmooth;
        win = ch.Smooth != SmoothKind.None ? ch.SmoothWindow : PlotSettings.GlobalSmoothWindow;
        return kind != SmoothKind.None;
    }

    private static void DrawSmoothed(IGraphics g, double[] data, Channel ch, int win, Rectangle rect,
                                     double xmin, double xmax, double ymin, double ymax,
                                     Func<double[], PointF[]> map)
    {
        var kind = ch.Smooth != SmoothKind.None ? ch.Smooth : PlotSettings.GlobalSmooth;
        int w = Math.Clamp(win | 1, 3, 501);
        double[] sm = kind switch
        {
            SmoothKind.Median when w <= 31 => PlotUtil.MedianFilter(data, w),
            SmoothKind.Gaussian => PlotUtil.GaussianFilter(data, w),
            SmoothKind.Exponential => PlotUtil.ExponentialFilter(data, w),
            _ => PlotUtil.MovingAverage(data, Math.Clamp(w, 3, 1001)),
        };
        int k0 = Math.Max(0, (int)Math.Ceiling(xmin));
        int k1 = Math.Min(sm.Length - 1, (int)Math.Floor(xmax));
        if (k1 < k0) return;
        using var pen = new Pen(Color.FromArgb(170, ch.Color), ch.LineWidth + 2f)
            { LineJoin = LineJoin.Round };
        g.DrawLines(pen, map(sm));
    }

    // Маркеры измерений: вертикальные линии A и B; между ними — Δt и ΔV
    // по каждому видимому каналу (значения в ближайших к маркерам точках).
    private static void DrawMarkers(IGraphics g, Rectangle rect, List<Channel> ys,
                                    double[]? xs, double xmin, double xmax, double ymin, double ymax)
    {
        var a = PlotSettings.MarkerA;
        var b = PlotSettings.MarkerB;
        if (a == null && b == null) return;

        using var font = new Font("Segoe UI", 9f);
        using var penA = new Pen(Theme.Accent) { DashStyle = DashStyle.Dash };
        using var penB = new Pen(Color.FromArgb(230, 120, 60)) { DashStyle = DashStyle.Dash };

        DrawOne(a, penA, "A");
        DrawOne(b, penB, "B");

        void DrawOne(double? mx, Pen pen, string tag)
        {
            if (mx is not { } v || v < xmin || v > xmax) return;
            int px = rect.Left + (int)(rect.Width * (v - xmin) / (xmax - xmin));
            g.DrawLine(pen, px, rect.Top, px, rect.Bottom);
            using var bsh = new SolidBrush(Theme.PlotBg);
            g.FillRectangle(bsh, px + 3, rect.Top + 2, 14, 15);
            using var br = new SolidBrush(pen.Color);
            g.DrawString(tag, font, br, px + 4, rect.Top + 2);
        }

        if (a is not { } va || b is not { } vb) return;
        double dt = Math.Abs(vb - va);
        string dtText = xs == null
            ? $"Δt = {dt:0.##} {Lang.T("точ", "pts")} ({dt / Math.Max(PlotSettings.SampleRate, 1e-6) * 1000:0.###} {Lang.T("мс", "ms")})"
            : $"ΔX = {dt:0.###}";

        // Значения каналов у маркеров и их разности
        var lines = new List<string> { dtText };
        var brushes = new List<Brush> { new SolidBrush(Theme.Text) };
        foreach (var ch in ys)
        {
            var data = ch.Data.ToArray();
            if (data.Length == 0) continue;
            double? vaCh = ValueAt(ch, data, xs, va);
            double? vbCh = ValueAt(ch, data, xs, vb);
            if (vaCh == null || vbCh == null) continue;
            lines.Add($"Δ{ch.Name} = {Math.Abs(vbCh.Value - vaCh.Value):0.###}");
            brushes.Add(new SolidBrush(ch.Color));
        }

        float boxW = 10 + lines.Max(t => PlotUtil.Measure(g, t, font).Width);
        float boxH = 6 + lines.Count * 16;
        float bx = rect.Right - boxW - 8, by = rect.Top + 6;
        using (var back = new SolidBrush(Theme.PlotBg))
            g.FillRectangle(back, bx, by, boxW, boxH);
        using var frame = new Pen(Theme.Accent);
        g.DrawRectangle(frame, bx, by, boxW, boxH);
        for (int i = 0; i < lines.Count; i++)
            g.DrawString(lines[i], font, brushes[i], bx + 5, by + 3 + i * 16);
        foreach (var br in brushes) br.Dispose();
    }

    // Значение канала в точке оси X: время — ближайший индекс; XY — ближайшая пара
    private static double? ValueAt(Channel ch, double[] data, double[]? xs, double x)
    {
        if (xs == null)
        {
            int i = (int)Math.Clamp(Math.Round(x), 0, data.Length - 1);
            return data[i];
        }
        int n = Math.Min(xs.Length, data.Length);
        int best = 0;
        double dist = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            double d = Math.Abs(xs[i] - x);
            if (d < dist) { dist = d; best = i; }
        }
        return data[best];
    }

    // Линейка под курсором: вертикальная линия — время/значение оси X,
    // горизонтальная — амплитуда, рядом с курсором — значения каналов в этой точке.
    private static void DrawRuler(IGraphics g, Rectangle rect, PlotContext ctx,
                                  List<Channel> ys, double[]? xs,
                                  double xmin, double xmax, double ymin, double ymax)
    {
        if (ctx.MouseX < rect.Left || ctx.MouseX > rect.Right ||
            ctx.MouseY < rect.Top || ctx.MouseY > rect.Bottom)
            return;

        using var rulerPen = new Pen(Theme.Axis) { DashStyle = DashStyle.Dash };
        using var font = new Font("Segoe UI", 9f);
        g.DrawLine(rulerPen, ctx.MouseX, rect.Top, ctx.MouseX, rect.Bottom);
        g.DrawLine(rulerPen, rect.Left, ctx.MouseY, rect.Right, ctx.MouseY);

        // Амплитуда по горизонтальной линии — на левую ось
        double yval = ymin + (rect.Bottom - ctx.MouseY) * (ymax - ymin) / rect.Height;
        using var yBrush = new SolidBrush(Theme.Text);
        string yLabel = yval.ToString("0.##");
        g.DrawString(yLabel, font, yBrush, rect.Left - 45, ctx.MouseY - 8);

        // Значение по оси X — вниз, под график; для времени рядом секунды
        double xval = xmin + (ctx.MouseX - rect.Left) * (xmax - xmin) / rect.Width;
        string xLabel = xs == null
            ? $"t = {(int)Math.Round(xval)} ({xval / Math.Max(PlotSettings.SampleRate, 1e-6):0.#} {Lang.T("с", "s")})"
            : $"{xval:0.##}";
        g.DrawString(xLabel, font, yBrush, ctx.MouseX + 4, rect.Bottom + 4);

        // Значения каналов в точке курсора (ищем ближайший индекс)
        int rowY = rect.Top + 24;
        foreach (var ch in ys)
        {
            var data = ch.Data.ToArray();
            if (data.Length == 0) continue;

            double v;
            if (xs == null)
            {
                int i = (int)Math.Clamp((int)Math.Round(xval), 0, data.Length - 1);
                v = data[i];
            }
            else
            {
                int n = Math.Min(xs.Length, data.Length);
                int i = (int)Math.Clamp((int)Math.Round(
                    (ctx.MouseX - rect.Left) * (n - 1.0) / rect.Width), 0, n - 1);
                v = data[i];
            }

            string s = $"{ch.Name} = {v:0.##}";
            var size = PlotUtil.Measure(g, s, font);
            int bx = ctx.MouseX + 12, by = rowY;
            if (bx + size.Width + 6 > rect.Right) bx = ctx.MouseX - (int)size.Width - 18;
            if (by + size.Height + 4 > rect.Bottom) by = rect.Bottom - (int)size.Height - 4;
            using var backBrush = new SolidBrush(Theme.PlotBg);
            g.FillRectangle(backBrush, bx, by, size.Width + 6, size.Height + 2);
            using var nameBrush = new SolidBrush(ch.Color);
            g.DrawString(s, font, nameBrush, bx + 3, by + 1);
            rowY += (int)size.Height + 5;
        }

        // Подпись времени у вертикальной линии (внутри графика, сверху)
        if (xs != null)
        {
            using var xb = new SolidBrush(Theme.Text);
            g.DrawString(xLabel, font, xb, ctx.MouseX + 4, rect.Top + 4);
        }
    }
}
