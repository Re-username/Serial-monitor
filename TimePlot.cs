using SvgNet.Interfaces;
using System.Drawing.Drawing2D;

namespace SerialPlotter;

// Режим «Время»: графики каналов по времени.
// Если в менеджере каналов на ось X выбран канал — превращается в XY-график:
// по горизонтали выбранный канал, по вертикали все остальные видимые.
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
            PlotUtil.Hint(g, rect, "Нет видимых каналов — отметь их в панели справа");
            return;
        }

        bool timeMode = ctx.XChannel == null;

        // Ось X: время (индекс точки) или выбранный канал
        Channel? xc = ctx.XChannel;
        double[]? xs = null;
        double xmin = 0, xmax = DataHub.HISTORY - 1;

        if (xc != null)
        {
            xs = xc.Data.ToArray();
            if (xs.Length < 2)
            {
                PlotUtil.Hint(g, rect, $"Нет данных по оси X ({xc.Name})");
                return;
            }
            PlotUtil.Range(xs, out double dxmin, out double dxmax);
            (xmin, xmax) = PlotSettings.ResolveX(PlotSettings.XAuto, dxmin, dxmax, timeMode: false);
        }
        else
        {
            (_, xmax) = PlotSettings.ResolveX(PlotSettings.XAuto, 0, DataHub.HISTORY - 1, timeMode: true);
        }

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

        using var centerPen = new Pen(Theme.Axis) { DashStyle = DashStyle.Dash };
        double ymid = (ymin + ymax) / 2;
        int cy = rect.Bottom - (int)(rect.Height * (ymid - ymin) / (ymax - ymin));
        g.DrawLine(centerPen, rect.Left, cy, rect.Right, cy);

        using var font = new Font("Segoe UI", 10f);
        g.DrawString(ymax.ToString("0.##"), font, textBrush, rect.Left - 45, rect.Top - 6);
        g.DrawString(ymid.ToString("0.##"), font, textBrush, rect.Left - 45, cy - 8);
        g.DrawString(ymin.ToString("0.##"), font, textBrush, rect.Left - 45, rect.Bottom - 10);

        if (xc != null)
        {
            // Ось X от канала: средняя линия и подписи диапазона
            double xmid = (xmin + xmax) / 2;
            int cx2 = rect.Left + (int)(rect.Width * (xmid - xmin) / (xmax - xmin));
            g.DrawLine(centerPen, cx2, rect.Top, cx2, rect.Bottom);

            g.DrawString(xmin.ToString("0.##"), font, textBrush, rect.Left, rect.Bottom + 4);
            string maxLabel = xmax.ToString("0.##");
            g.DrawString(maxLabel, font, textBrush,
                         rect.Right - PlotUtil.Measure(g, maxLabel, font).Width, rect.Bottom + 4);
            string nameLabel = $"ось X: {xc.Name}";
            g.DrawString(nameLabel, font, textBrush,
                         rect.Left + (rect.Width - PlotUtil.Measure(g, nameLabel, font).Width) / 2,
                         rect.Bottom + 4);
        }
        else if (!PlotSettings.XAuto)
        {
            // Ручное окно по времени: подписи — сколько точек показано
            g.DrawString("0", font, textBrush, rect.Left, rect.Bottom + 4);
            string maxLabel = $"{(int)xmax + 1} точ.";
            g.DrawString(maxLabel, font, textBrush,
                         rect.Right - PlotUtil.Measure(g, maxLabel, font).Width, rect.Bottom + 4);
        }

        // Линии графиков
        foreach (var ch in ys)
        {
            var data = ch.Data.ToArray();
            if (data.Length < 2) continue;
            using var pen = new Pen(ch.Color, ch.LineWidth) { DashStyle = ch.LineStyle.ToDashStyle() };

            if (xs == null)
            {
                // Ось X — время: последние (xmax+1) точек равномерно по ширине
                int window = (int)xmax + 1;
                int n = Math.Min(window, data.Length);
                int skip = data.Length - n; // начало окна в буфере
                var points = new Point[n];
                for (int i = 0; i < n; i++)
                {
                    int px = rect.Left + rect.Width * i / (window - 1);
                    int py = rect.Bottom - (int)(rect.Height * (data[skip + i] - ymin) / (ymax - ymin));
                    points[i] = new Point(px, Math.Clamp(py, rect.Top, rect.Bottom));
                }
                g.DrawLines(pen, points);
            }
            else
            {
                // XY-режим: пары (x[i], y[i])
                int n = Math.Min(xs.Length, data.Length);
                if (n < 2) continue;
                var points = new PointF[n];
                for (int i = 0; i < n; i++)
                {
                    float px = rect.Left + (float)(rect.Width * (xs[i] - xmin) / (xmax - xmin));
                    float py = rect.Bottom - (float)(rect.Height * (data[i] - ymin) / (ymax - ymin));
                    points[i] = new PointF(Math.Clamp(px, rect.Left, rect.Right),
                                           Math.Clamp(py, rect.Top, rect.Bottom));
                }
                g.DrawLines(pen, points);
            }
        }

        DrawRuler(g, rect, ctx, ys, xs, xmin, xmax, ymin, ymax);
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

        // Значение по оси X — вниз, под график
        double xval = xmin + (ctx.MouseX - rect.Left) * (xmax - xmin) / rect.Width;
        string xLabel = xs == null ? $"t = {(int)Math.Round(xval)}" : $"{xval:0.##}";
        g.DrawString(xLabel, font, yBrush, ctx.MouseX + 4, rect.Bottom + 4);

        // Значения каналов в точке курсора (ищем ближайший индекс)
        int rowY = rect.Top + 6;
        foreach (var ch in ys)
        {
            var data = ch.Data.ToArray();
            if (data.Length == 0) continue;

            double v;
            if (xs == null)
            {
                int window = (int)xmax + 1;
                int n = Math.Min(window, data.Length);
                int skip = data.Length - n;
                int i = skip + (int)Math.Clamp((int)Math.Round(
                    (ctx.MouseX - rect.Left) * (window - 1.0) / rect.Width), 0, n - 1);
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
