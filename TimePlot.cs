using System.Drawing.Drawing2D;

namespace SerialPlotter;

// Режим «Время»: графики каналов по времени.
// Если в менеджере каналов на ось X выбран канал — превращается в XY-график:
// по горизонтали выбранный канал, по вертикали все остальные видимые.
public static class TimePlot
{
    public static void Draw(Graphics g, Rectangle rect, PlotContext ctx)
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
            PlotUtil.Range(xs, out xmin, out xmax);
        }

        // Общий масштаб по вертикали для всех видимых каналов
        double ymin = double.MaxValue, ymax = double.MinValue;
        foreach (var ch in ys)
        {
            PlotUtil.Range(ch.Data, out double cmin, out double cmax);
            if (cmin < ymin) ymin = cmin;
            if (cmax > ymax) ymax = cmax;
        }
        if (ymax - ymin < 1e-9) { ymin -= 1; ymax += 1; }

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
                         rect.Right - g.MeasureString(maxLabel, font).Width, rect.Bottom + 4);
            string nameLabel = $"ось X: {xc.Name}";
            g.DrawString(nameLabel, font, textBrush,
                         rect.Left + (rect.Width - g.MeasureString(nameLabel, font).Width) / 2,
                         rect.Bottom + 4);
        }

        // Легенда с текущими значениями
        int lx = rect.Left + 10;
        foreach (var ch in ys)
        {
            using var dotBrush = new SolidBrush(ch.Color);
            g.FillRectangle(dotBrush, lx, rect.Top + 10, 10, 10);
            string label = $"{ch.Name} = {ch.Last:0.##}";
            using var legendFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            g.DrawString(label, legendFont, new SolidBrush(ch.Color), lx + 14, rect.Top + 6);
            lx += (int)g.MeasureString(label, legendFont).Width + 34;
        }

        // Линии графиков
        foreach (var ch in ys)
        {
            var data = ch.Data.ToArray();
            if (data.Length < 2) continue;
            using var pen = new Pen(ch.Color, 2);

            if (xs == null)
            {
                // Ось X — время: точки равномерно по ширине
                var points = new Point[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    int px = rect.Left + rect.Width * i / (DataHub.HISTORY - 1);
                    int py = rect.Bottom - (int)(rect.Height * (data[i] - ymin) / (ymax - ymin));
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
    }
}
