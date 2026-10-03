using System.Drawing.Drawing2D;

namespace SerialPlotter;

// Полярный режим: первые два видимых канала как вектор (R, φ).
// Выбор пары — галочками видимости в менеджере каналов.
public static class PolarPlot
{
    public static void Draw(Graphics g, Rectangle rect, PlotContext ctx)
    {
        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        int R = Math.Min(rect.Width, rect.Height) / 2 - 12;

        using (var brush = new SolidBrush(Theme.PlotBg))
            g.FillRectangle(brush, rect);
        using var gridPen = new Pen(Theme.Grid);
        using var axisPen = new Pen(Theme.Axis) { DashStyle = DashStyle.Dash };
        using var textBrush = new SolidBrush(Theme.Text);

        for (int i = 1; i <= 4; i++)
        {
            int r = R * i / 4;
            g.DrawEllipse(gridPen, cx - r, cy - r, 2 * r, 2 * r);
        }
        for (int a = 0; a < 360; a += 30)
        {
            double rad = a * Math.PI / 180.0;
            g.DrawLine(gridPen, cx, cy, cx + (int)(R * Math.Cos(rad)), cy - (int)(R * Math.Sin(rad)));
        }
        g.DrawLine(axisPen, cx - R, cy, cx + R, cy);
        g.DrawLine(axisPen, cx, cy - R, cx, cy + R);

        var vis = ctx.Visible;
        if (vis.Count < 2)
        {
            PlotUtil.Hint(g, rect, "Для полярного режима нужно минимум 2 видимых канала");
            return;
        }

        var chX = vis[0];
        var chY = vis[1];
        PlotUtil.Range(chX.Data, out double minX, out double maxX);
        PlotUtil.Range(chY.Data, out double minY, out double maxY);
        double cX = (minX + maxX) / 2, halfX = (maxX - minX) / 2;
        double cY = (minY + maxY) / 2, halfY = (maxY - minY) / 2;
        double half = Math.Max(halfX, halfY);

        using var font = new Font("Segoe UI", 10f);
        g.DrawString(half.ToString("0.##"), font, textBrush, cx + R + 4, cy - 8);
        g.DrawString($"Центр = ({cX:0.##}, {cY:0.##}),   R — отклонение от центра,   φ — от оси +{chX.Name}",
                     font, textBrush, rect.Left + 8, rect.Bottom - 22);

        int n = Math.Min(chX.Data.Count, chY.Data.Count);
        if (n == 0) return;

        var xd = chX.Data.ToArray();
        var yd = chY.Data.ToArray();

        PointF ToScreen(double x, double y)
        {
            double dx = (x - cX) / half, dy = (cY - y) / half;
            double r = Math.Min(Math.Sqrt(dx * dx + dy * dy), 1);
            double a = Math.Atan2(dy, dx);
            return new PointF((float)(cx + r * Math.Cos(a) * R), (float)(cy - r * Math.Sin(a) * R));
        }

        if (n > 1)
        {
            using var trailPen = new Pen(Color.FromArgb(110, chY.Color)) { Width = 2 };
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = ToScreen(xd[i], yd[i]);
            g.DrawLines(trailPen, pts);
        }

        var cur = ToScreen(xd[^1], yd[^1]);
        using var vecPen = new Pen(chX.Color, 2);
        g.DrawLine(vecPen, cx, cy, cur.X, cur.Y);
        using var dotBrush = new SolidBrush(chY.Color);
        g.FillEllipse(dotBrush, cur.X - 6, cur.Y - 6, 12, 12);
        using var dotPen = new Pen(Theme.PlotBg, 2);
        g.DrawEllipse(dotPen, cur.X - 6, cur.Y - 6, 12, 12);
        using var centerBrush = new SolidBrush(Theme.Axis);
        g.FillEllipse(centerBrush, cx - 3, cy - 3, 6, 6);

        double ddx = xd[^1] - cX, ddy = cY - yd[^1];
        double rr = Math.Min(Math.Sqrt(ddx * ddx + ddy * ddy), half);
        double phi = (Math.Atan2(ddy, ddx) * 180.0 / Math.PI + 360) % 360;

        using var legendFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        g.DrawString($"R = {rr:0.##}", legendFont, new SolidBrush(chX.Color), rect.Left + 10, rect.Top + 8);
        g.DrawString($"φ = {phi:F0}°", legendFont, new SolidBrush(chY.Color), rect.Left + 110, rect.Top + 8);
    }
}
