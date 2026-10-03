using SvgNet.Interfaces;
namespace SerialPlotter;

// 3D-режим: первые три видимых канала как траектория в пространстве.
// Если в PlotContext.Time3DAxis выбрана ось (1..3 = X/Y/Z), эта ось — время,
// а каналов достаточно двух: они занимают оставшиеся две оси.
// Углы поворота приходят в PlotContext.RotX/RotY (вращение мышью в MainForm).
public static class Plot3D
{
    private static PointF Project3D(double x, double y, double z,
                                    double[] mid, double[] half,
                                    double rotX, double rotY,
                                    int cx, int cy, double R)
    {
        double nx = (x - mid[0]) / half[0];
        double ny = (y - mid[1]) / half[1];
        double nz = (z - mid[2]) / half[2];

        double cosY = Math.Cos(rotY), sinY = Math.Sin(rotY);
        double rx = nx * cosY + nz * sinY;
        double rz = -nx * sinY + nz * cosY;
        double cosX = Math.Cos(rotX), sinX = Math.Sin(rotX);
        double ry = ny * cosX - rz * sinX;

        return new PointF((float)(cx + rx * R), (float)(cy - ry * R));
    }

    public static void Draw(IGraphics g, Rectangle rect, PlotContext ctx)
    {
        using (var brush = new SolidBrush(Theme.PlotBg))
            g.FillRectangle(brush, rect);
        using var gridPen = new Pen(Theme.Grid);
        using var textBrush = new SolidBrush(Theme.Text);

        int tAxis = ctx.Time3DAxis; // 0 — выкл, 1–3 — время на оси X/Y/Z
        int need = tAxis > 0 ? 2 : 3;

        var vis = ctx.Visible;
        if (vis.Count < need)
        {
            PlotUtil.Hint(g, rect, tAxis > 0
                ? "Для 3D с осью времени нужно минимум 2 видимых канала"
                : "Для 3D нужно минимум 3 видимых канала (X, Y, Z)");
            return;
        }

        var chans = vis.Take(need).ToArray();
        int n = chans.Min(c => c.Data.Count);

        // Последние n точек каждого канала — ряды выровнены по времени
        var chanSeries = chans.Select(c => c.Data.TakeLast(n).ToArray()).ToArray();

        // Раскладываем по трём осям: на ось tAxis-1 — время (индекс точки),
        // на остальные — каналы по порядку
        var series = new double[3][];
        var names = new string[3];
        var colors = new Color[3];
        int ci = 0;
        for (int i = 0; i < 3; i++)
        {
            if (i == tAxis - 1)
            {
                series[i] = Enumerable.Range(0, n).Select(k => (double)k).ToArray();
                names[i] = "t";
                colors[i] = Theme.Axis;
            }
            else
            {
                series[i] = chanSeries[ci];
                names[i] = chans[ci].Name;
                colors[i] = chans[ci].Color;
                ci++;
            }
        }

        var mid = new double[3];
        var half = new double[3];
        for (int i = 0; i < 3; i++)
        {
            PlotUtil.Range(series[i], out double min, out double max);
            mid[i] = (min + max) / 2;
            half[i] = Math.Max((max - min) / 2, 1e-9);
        }

        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        double R = Math.Min(rect.Width, rect.Height) / 2 - 30;
        double rotX = ctx.RotX, rotY = ctx.RotY;

        // Границы куба [-1,1]^3
        var corners = new PointF[8];
        for (int i = 0; i < 8; i++)
            corners[i] = Project3D(mid[0] + ((i & 1) != 0 ? 1 : -1) * half[0],
                                   mid[1] + ((i & 2) != 0 ? 1 : -1) * half[1],
                                   mid[2] + ((i & 4) != 0 ? 1 : -1) * half[2],
                                   mid, half, rotX, rotY, cx, cy, R);
        for (int i = 0; i < 8; i++)
            for (int b = 1; b < 8; b <<= 1)
                if ((i & b) == 0)
                    g.DrawLine(gridPen, corners[i], corners[i | b]);

        // Оси с именами каналов
        var origin = Project3D(mid[0], mid[1], mid[2], mid, half, rotX, rotY, cx, cy, R);
        using var axisFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        for (int i = 0; i < 3; i++)
        {
            var tip = new double[] { mid[0], mid[1], mid[2] };
            tip[i] += half[i];
            var p = Project3D(tip[0], tip[1], tip[2], mid, half, rotX, rotY, cx, cy, R);
            using var axisPen = new Pen(colors[i], 2);
            g.DrawLine(axisPen, origin, p);
            g.DrawString(names[i], axisFont, new SolidBrush(colors[i]), p.X + 4, p.Y - 8);
        }

        // Траектория
        if (n > 1)
        {
            using var trailPen = new Pen(Color.FromArgb(110, colors[2])) { Width = 2 };
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = Project3D(series[0][i], series[1][i], series[2][i], mid, half, rotX, rotY, cx, cy, R);
            g.DrawLines(trailPen, pts);

            // Текущая точка
            var cur = pts[^1];
            using var glowBrush = new SolidBrush(Color.FromArgb(70, colors[2]));
            g.FillEllipse(glowBrush, cur.X - 12, cur.Y - 12, 24, 24);
            using var dotBrush = new SolidBrush(colors[2]);
            g.FillEllipse(dotBrush, cur.X - 5, cur.Y - 5, 10, 10);
        }

        // Легенда со значениями
        using var legendFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        int lx = rect.Left + 10;
        for (int i = 0; i < 3; i++)
        {
            string label = $"{names[i]} = {(series[i].Length > 0 ? series[i][^1] : 0):0.##}";
            g.DrawString(label, legendFont, new SolidBrush(colors[i]), lx, rect.Top + 8);
            lx += (int)PlotUtil.Measure(g, label, legendFont).Width + 24;
        }

        using var hintFont = new Font("Segoe UI", 9f);
        g.DrawString("Перетаскивай мышью, чтобы вращать", hintFont, textBrush,
                     rect.Right - 230, rect.Bottom - 20);
    }
}
