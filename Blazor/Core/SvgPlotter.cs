using System.Globalization;
using System.Text;

namespace SerialPlotter.Blazor.Core;

public enum PlotMode
{
    Time,
    Polar,
    ThreeD,
    Fft,
}

public readonly record struct PlotRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public bool Contains(double x, double y) =>
        x >= Left && x <= Right && y >= Top && y <= Bottom;
}

public sealed class PlotContext
{
    public PlotMode Mode;
    public IReadOnlyList<Channel> Visible = Array.Empty<Channel>();
    public int XIndex = -1;
    public double RotX, RotY;
    public int Time3DAxis;
    public double MouseX = -1, MouseY = -1;
    public PlotRect? Selection;
    public bool ForExport;

    public Channel? XChannel =>
        XIndex >= 0 && XIndex < DataHub.Count ? DataHub.ChannelAt(XIndex) : null;

    public IEnumerable<Channel> YChannels =>
        XChannel is { } x ? Visible.Where(c => !ReferenceEquals(c, x)) : Visible;
}

public static class SvgPlotter
{
    public const int Width = 1000;
    public const int Height = 600;
    public static readonly PlotRect Plot = new(70, 25, 900, 480);

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string I(double value) => value.ToString(CultureInfo.InvariantCulture);

    public static string RenderSvg(PlotContext context)
    {
        var sb = new StringBuilder(128 * 1024);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" preserveAspectRatio=\"none\">");
        sb.Append($"<rect width=\"{Width}\" height=\"{Height}\" fill=\"{Theme.PlotBackground}\"/>");

        switch (context.Mode)
        {
            case PlotMode.Polar:
                DrawPolar(sb, context);
                break;
            case PlotMode.ThreeD:
                Draw3D(sb, context);
                break;
            case PlotMode.Fft:
                DrawFft(sb, context);
                break;
            default:
                DrawTime(sb, context);
                break;
        }

        if (context.Selection is { } selection && selection.Width > 0 && selection.Height > 0)
        {
            sb.Append($"<rect x=\"{F(selection.Left)}\" y=\"{F(selection.Top)}\" width=\"{F(selection.Width)}\" height=\"{F(selection.Height)}\" fill=\"none\" stroke=\"#00e5ff\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\"/>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void DrawGrid(StringBuilder sb, PlotRect rect, int horizontal, int vertical)
    {
        for (int i = 0; i <= horizontal; i++)
        {
            double y = rect.Top + rect.Height * i / horizontal;
            sb.Append(Line(rect.Left, y, rect.Right, y, Theme.Grid, 1));
        }
        for (int i = 0; i <= vertical; i++)
        {
            double x = rect.Left + rect.Width * i / vertical;
            sb.Append(Line(x, rect.Top, x, rect.Bottom, Theme.Grid, 1));
        }
    }

    private static void DrawTime(StringBuilder sb, PlotContext context)
    {
        var rect = Plot;
        DrawGrid(sb, rect, 4, 8);

        var channels = context.YChannels.ToList();
        if (channels.Count == 0)
        {
            Hint(sb, rect, Lang.T("Нет видимых каналов — отметь их в панели справа",
                "No visible channels — check them in the panel on the right"));
            SetLast(0, 1, 0, 1);
            return;
        }

        bool timeMode = context.XChannel == null;
        var xChannel = context.XChannel;
        double[]? xs = null;
        int count = context.Visible.Count == 0 ? 0 : context.Visible.Max(c => DataHub.Snapshot(c).Length);
        double xmin, xmax;

        if (xChannel != null)
        {
            xs = DataHub.Snapshot(xChannel);
            if (xs.Length < 2)
            {
                Hint(sb, rect, $"{Lang.T("Нет данных по оси X", "No data on axis X")} ({xChannel.Name})");
                SetLast(0, 1, 0, 1);
                return;
            }
            PlotMath.Range(xs, out double dataMin, out double dataMax);
            (xmin, xmax) = PlotSettings.ResolveX(PlotSettings.XAuto, dataMin, dataMax, timeMode: false);
        }
        else
        {
            double window = PlotSettings.XAuto
                ? Math.Min(Math.Max(count, 2), PlotSettings.AutoWindow)
                : Math.Clamp(Math.Max(PlotSettings.XMax, PlotSettings.XMin), 2, DataHub.HISTORY);
            xmax = Math.Max(count - 1, 1);
            xmin = Math.Max(0, xmax - window + 1);
        }

        if (PlotSettings.ViewXMin is { } viewXMin && PlotSettings.ViewXMax is { } viewXMax && viewXMax > viewXMin)
        {
            xmin = timeMode ? Math.Max(0, viewXMin) : viewXMin;
            xmax = viewXMax;
        }
        if (xmax - xmin < 1e-9)
            xmax = xmin + 1;

        double dataYMin = double.MaxValue, dataYMax = double.MinValue;
        foreach (var channel in channels)
        {
            PlotMath.Range(DataHub.Snapshot(channel), out double min, out double max);
            dataYMin = Math.Min(dataYMin, min);
            dataYMax = Math.Max(dataYMax, max);
        }
        if (dataYMax - dataYMin < 1e-9)
        {
            dataYMin -= 1;
            dataYMax += 1;
        }

        var (ymin, ymax) = PlotSettings.ResolveY(PlotSettings.YAuto, dataYMin, dataYMax);
        if (PlotSettings.ViewYMin is { } viewYMin && PlotSettings.ViewYMax is { } viewYMax && viewYMax > viewYMin)
            (ymin, ymax) = (viewYMin, viewYMax);
        if (ymax - ymin < 1e-9)
            ymax = ymin + 1;

        SetLast(xmin, xmax, ymin, ymax);

        double yMid = (ymin + ymax) / 2;
        double centerY = Y(yMid, ymin, ymax, rect);
        sb.Append(Line(rect.Left, centerY, rect.Right, centerY, Theme.Axis, 1, "6 5"));

        if (xChannel != null)
        {
            double xMid = (xmin + xmax) / 2;
            double centerX = X(xMid, xmin, xmax, rect);
            sb.Append(Line(centerX, rect.Top, centerX, rect.Bottom, Theme.Axis, 1, "6 5"));
        }

        Text(sb, rect.Left - 10, rect.Top + 4, F(ymax), Theme.Text, 13, "end");
        Text(sb, rect.Left - 10, centerY + 4, F(yMid), Theme.Text, 13, "end");
        Text(sb, rect.Left - 10, rect.Bottom + 4, F(ymin), Theme.Text, 13, "end");

        if (xChannel != null)
        {
            Text(sb, rect.Left, rect.Bottom + 22, F(xmin), Theme.Text, 12);
            Text(sb, rect.Right, rect.Bottom + 22, F(xmax), Theme.Text, 12, "end");
            Text(sb, rect.Left + rect.Width / 2, rect.Bottom + 22,
                $"{Lang.T("ось X", "axis X")}: {Escape(xChannel.Name)}", Theme.Text, 12, "middle");
        }
        else
        {
            Text(sb, rect.Left, rect.Bottom + 22, ((long)Math.Max(0, xmin)).ToString(), Theme.Text, 12);
            Text(sb, rect.Right, rect.Bottom + 22,
                $"{(long)xmax + 1} {Lang.T("точ.", "pts")}", Theme.Text, 12, "end");
            double sampleRate = Math.Max(PlotSettings.SampleRate, 1e-6);
            Text(sb, rect.Left, rect.Bottom + 39, $"{Math.Max(0, xmin) / sampleRate:0.#} {Lang.T("с", "s")}", Theme.Axis, 11);
            Text(sb, rect.Right, rect.Bottom + 39, $"{(xmax + 1) / sampleRate:0.#} {Lang.T("с", "s")}", Theme.Axis, 11, "end");
        }

        if (xs == null && count > 0)
        {
            double edgeX = count - 1;
            if (edgeX >= xmin && edgeX <= xmax)
            {
                double sx = X(edgeX, xmin, xmax, rect);
                sb.Append(Line(sx, rect.Top, sx, rect.Bottom, "#ff9f1a", 1));
                Text(sb, sx - 4, rect.Top + 14, Lang.T("сейчас", "live"), "#ff9f1a", 11, "end");
            }
        }

        foreach (var channel in channels)
        {
            var data = DataHub.Snapshot(channel);
            if (data.Length < 2)
                continue;

            string dash = Dash(channel.LineStyle);
            double width = Math.Max(0.5, channel.LineWidth);
            string points;

            if (EffectiveSmooth(channel, out int smoothWindow))
            {
                var smoothed = PlotMath.Smooth(data, EffectiveSmoothKind(channel), smoothWindow);
                points = timeMode
                    ? TimePath(smoothed, xmin, xmax, ymin, ymax, rect)
                    : XyPath(xs!, smoothed, Math.Min(xs!.Length, smoothed.Length), xmin, xmax, ymin, ymax, rect);
            }
            else
            {
                points = timeMode
                    ? TimePath(data, xmin, xmax, ymin, ymax, rect)
                    : XyPath(xs!, data, Math.Min(xs!.Length, data.Length), xmin, xmax, ymin, ymax, rect);
            }

            if (points.Length > 0)
            {
                sb.Append($"<path d=\"{points}\" fill=\"none\" stroke=\"{channel.Color}\" stroke-width=\"{F(width)}\" stroke-dasharray=\"{dash}\" stroke-linejoin=\"round\" stroke-linecap=\"round\" vector-effect=\"non-scaling-stroke\"/>");
            }
        }

        if (!context.ForExport)
        {
            DrawMarkers(sb, context, channels, xs, xmin, xmax, ymin, ymax, rect);
            DrawRuler(sb, context, channels, xs, xmin, xmax, ymin, ymax, rect);
        }
    }

    private static void DrawMarkers(StringBuilder sb, PlotContext context, List<Channel> channels,
        double[]? xs, double xmin, double xmax, double ymin, double ymax, PlotRect rect)
    {
        if (PlotSettings.MarkerA is not { } markerA)
            return;

        void Marker(double value, string name, string color)
        {
            if (value < xmin || value > xmax)
                return;
            double x = X(value, xmin, xmax, rect);
            sb.Append(Line(x, rect.Top, x, rect.Bottom, color, 1.5));
            Text(sb, x + 4, rect.Top + 16, name, color, 12);
        }

        Marker(markerA, "A", "#00e5ff");
        if (PlotSettings.MarkerB is not { } markerB)
            return;

        Marker(markerB, "B", "#ff2bd6");
        double dt = Math.Abs(markerB - markerA);
        double ms = dt / Math.Max(PlotSettings.SampleRate, 1e-6) * 1000;
        double left = X(Math.Min(markerA, markerB), xmin, xmax, rect);
        double right = X(Math.Max(markerA, markerB), xmin, xmax, rect);
        double y = rect.Top + 28;
        sb.Append(Line(left, y, right, y, "#f4f45c", 1));
        sb.Append(Line(left, y - 5, left, y + 5, "#f4f45c", 1));
        sb.Append(Line(right, y - 5, right, y + 5, "#f4f45c", 1));
        Text(sb, (left + right) / 2, y - 7,
            $"Δt = {F(dt)} {Lang.T("точ.", "pts")} / {F(ms)} {Lang.T("мс", "ms")}", "#f4f45c", 11, "middle");

        int indexA = NearestIndex(markerA, xs, channels.Max(c => DataHub.Snapshot(c).Length));
        int indexB = NearestIndex(markerB, xs, channels.Max(c => DataHub.Snapshot(c).Length));
        var values = new List<string>();
        foreach (var channel in channels.Take(6))
        {
            var data = DataHub.Snapshot(channel);
            if (indexA >= 0 && indexA < data.Length && indexB >= 0 && indexB < data.Length)
                values.Add($"{Escape(channel.Name)}  A={F(data[indexA])}  B={F(data[indexB])}  Δ={F(data[indexB] - data[indexA])}");
        }
        if (values.Count > 0)
        {
            double boxWidth = Math.Min(360, 24 + values.Max(v => v.Length) * 6.4);
            double boxHeight = values.Count * 15 + 10;
            sb.Append($"<rect x=\"{F(rect.Left + 8)}\" y=\"{F(rect.Top + 42)}\" width=\"{F(boxWidth)}\" height=\"{F(boxHeight)}\" rx=\"4\" fill=\"{Theme.PlotBackground}\" opacity=\"0.9\" stroke=\"#f4f45c\"/>");
            for (int i = 0; i < values.Count; i++)
                Text(sb, rect.Left + 16, rect.Top + 58 + i * 15, values[i], "#f4f45c", 10.5);
        }
    }

    private static int NearestIndex(double value, double[]? xs, int dataLength)
    {
        if (dataLength <= 0)
            return -1;
        if (xs == null || xs.Length == 0)
            return Math.Clamp((int)Math.Round(value), 0, dataLength - 1);

        int best = 0;
        double bestDistance = Math.Abs(xs[0] - value);
        int limit = Math.Min(xs.Length, dataLength);
        for (int i = 1; i < limit; i++)
        {
            double distance = Math.Abs(xs[i] - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    private static void DrawRuler(StringBuilder sb, PlotContext context, List<Channel> channels,
        double[]? xs, double xmin, double xmax, double ymin, double ymax, PlotRect rect)
    {
        if (context.MouseX < rect.Left || context.MouseX > rect.Right ||
            context.MouseY < rect.Top || context.MouseY > rect.Bottom)
            return;

        double x = context.MouseX;
        sb.Append(Line(x, rect.Top, x, rect.Bottom, Theme.Axis, 1, "3 4"));

        double valueX = xmin + (x - rect.Left) / rect.Width * (xmax - xmin);
        var values = new List<string> { $"x={F(valueX)}" };
        int index = NearestIndex(valueX, xs, channels.Count == 0 ? 0 : channels.Max(c => DataHub.Snapshot(c).Length));
        foreach (var channel in channels.Take(4))
        {
            var data = DataHub.Snapshot(channel);
            if (index >= 0 && index < data.Length)
                values.Add($"{Escape(channel.Name)}={F(data[index])}");
        }

        double labelX = Math.Min(x + 8, rect.Right - 190);
        double labelY = Math.Max(rect.Top + 18, context.MouseY - 12);
        sb.Append($"<rect x=\"{F(labelX - 5)}\" y=\"{F(labelY - 15)}\" width=\"190\" height=\"{F(values.Count * 16 + 8)}\" rx=\"5\" fill=\"{Theme.PlotBackground}\" opacity=\"0.88\" stroke=\"{Theme.Grid}\"/>");
        for (int i = 0; i < values.Count; i++)
            Text(sb, labelX, labelY + i * 16, values[i], Theme.Text, 11);
    }

    private static void DrawPolar(StringBuilder sb, PlotContext context)
    {
        var rect = Plot;
        double cx = rect.Left + rect.Width / 2;
        double cy = rect.Top + rect.Height / 2;
        double radius = Math.Min(rect.Width, rect.Height) / 2 - 12;

        DrawGrid(sb, rect, 4, 8);
        for (int i = 1; i <= 4; i++)
        {
            double r = radius * i / 4;
            sb.Append($"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" fill=\"none\" stroke=\"{Theme.Grid}\"/>");
        }
        for (int angle = 0; angle < 360; angle += 30)
        {
            double rad = angle * Math.PI / 180.0;
            sb.Append(Line(cx, cy, cx + radius * Math.Cos(rad), cy - radius * Math.Sin(rad), Theme.Grid, 1));
        }
        sb.Append(Line(cx - radius, cy, cx + radius, cy, Theme.Axis, 1, "6 5"));
        sb.Append(Line(cx, cy - radius, cx, cy + radius, Theme.Axis, 1, "6 5"));

        if (context.Visible.Count < 2)
        {
            Hint(sb, rect, Lang.T("Для полярного режима нужно минимум 2 видимых канала",
                "Polar mode needs at least 2 visible channels"));
            return;
        }

        var channelX = context.Visible[0];
        var channelY = context.Visible[1];
        var dataX = DataHub.Snapshot(channelX);
        var dataY = DataHub.Snapshot(channelY);
        PlotMath.Range(dataX, out double minX, out double maxX);
        PlotMath.Range(dataY, out double minY, out double maxY);
        double centerX = (minX + maxX) / 2, halfX = (maxX - minX) / 2;
        double centerY = (minY + maxY) / 2, halfY = (maxY - minY) / 2;
        double half = Math.Max(halfX, halfY);

        int count = Math.Min(dataX.Length, dataY.Length);
        if (count == 0)
            return;
        int skip = Math.Max(0, count - Math.Clamp(PlotSettings.PolarTrail, 10, 10000));

        (double X, double Y) ToScreen(double x, double y)
        {
            double dx = (x - centerX) / half;
            double dy = (centerY - y) / half;
            double r = Math.Min(Math.Sqrt(dx * dx + dy * dy), 1);
            double a = Math.Atan2(dy, dx);
            return (cx + r * Math.Cos(a) * radius, cy - r * Math.Sin(a) * radius);
        }

        var points = new List<(double X, double Y)>();
        for (int i = skip; i < count; i++)
            points.Add(ToScreen(dataX[i], dataY[i]));
        if (points.Count > 1)
            sb.Append(Path(points, WithAlpha(channelY.Color, 0.55), 2));

        var current = ToScreen(dataX[^1], dataY[^1]);
        sb.Append(Line(cx, cy, current.X, current.Y, channelX.Color, 2));
        sb.Append($"<circle cx=\"{F(current.X)}\" cy=\"{F(current.Y)}\" r=\"6\" fill=\"{channelY.Color}\" stroke=\"{Theme.PlotBackground}\" stroke-width=\"2\"/>");
        sb.Append($"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"3\" fill=\"{Theme.Axis}\"/>");

        double dxLast = dataX[^1] - centerX;
        double dyLast = centerY - dataY[^1];
        double rLast = Math.Min(Math.Sqrt(dxLast * dxLast + dyLast * dyLast), half);
        double phi = (Math.Atan2(dyLast, dxLast) * 180 / Math.PI + 360) % 360;
        Text(sb, rect.Left + 10, rect.Top + 20, $"R = {F(rLast)}", channelX.Color, 14, weight: 700);
        Text(sb, rect.Left + 120, rect.Top + 20, $"φ = {phi:F0}°", channelY.Color, 14, weight: 700);
        Text(sb, rect.Right - 10, rect.Top + 20,
            $"{Lang.T("Пара", "Pair")}: {Escape(channelX.Name)} / {Escape(channelY.Name)}", Theme.Text, 11, "end");
        Text(sb, rect.Left + 8, rect.Bottom - 8,
            $"{Lang.T("Центр", "Center")} = ({F(centerX)}, {F(centerY)})", Theme.Text, 11);
    }

    private static void Draw3D(StringBuilder sb, PlotContext context)
    {
        var rect = Plot;
        DrawGrid(sb, rect, 4, 8);

        int timeAxis = context.Time3DAxis;
        // Двух каналов достаточно: третьей осью автоматически становится время.
        if (timeAxis == 0 && context.Visible.Count == 2)
            timeAxis = 1;

        int required = timeAxis > 0 ? 2 : 3;
        if (context.Visible.Count < required)
        {
            Hint(sb, rect, timeAxis > 0
                ? Lang.T("Для 3D с осью времени нужно минимум 2 видимых канала",
                    "3D with a time axis needs at least 2 visible channels")
                : Lang.T("Для 3D нужно минимум 3 видимых канала (X, Y, Z)",
                    "3D needs at least 3 visible channels (X, Y, Z)"));
            return;
        }

        var channels = context.Visible.Take(required).ToArray();
        int count = channels.Min(c => DataHub.Snapshot(c).Length);
        var channelSeries = channels.Select(c => DataHub.Snapshot(c).TakeLast(count).ToArray()).ToArray();

        var series = new double[3][];
        var names = new string[3];
        var colors = new string[3];
        int channelIndex = 0;
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis == timeAxis - 1)
            {
                series[axis] = Enumerable.Range(0, count).Select(i => (double)i).ToArray();
                names[axis] = "t";
                colors[axis] = Theme.Axis;
            }
            else
            {
                series[axis] = channelSeries[channelIndex];
                names[axis] = channels[channelIndex].Name;
                colors[axis] = channels[channelIndex].Color;
                channelIndex++;
            }
        }

        var middle = new double[3];
        var half = new double[3];
        for (int i = 0; i < 3; i++)
        {
            PlotMath.Range(series[i], out double min, out double max);
            middle[i] = (min + max) / 2;
            half[i] = Math.Max((max - min) / 2, 1e-9);
        }

        double cx = rect.Left + rect.Width / 2;
        double cy = rect.Top + rect.Height / 2;
        double radius = Math.Min(rect.Width, rect.Height) / 2 - 30;

        (double X, double Y) Project(double x, double y, double z)
        {
            double nx = (x - middle[0]) / half[0];
            double ny = (y - middle[1]) / half[1];
            double nz = (z - middle[2]) / half[2];
            double cosY = Math.Cos(context.RotY), sinY = Math.Sin(context.RotY);
            double rx = nx * cosY + nz * sinY;
            double rz = -nx * sinY + nz * cosY;
            double cosX = Math.Cos(context.RotX), sinX = Math.Sin(context.RotX);
            double ry = ny * cosX - rz * sinX;
            return (cx + rx * radius, cy - ry * radius);
        }

        (double X, double Y) Clamp((double X, double Y) p) =>
            (Math.Clamp(p.X, rect.Left, rect.Right), Math.Clamp(p.Y, rect.Top, rect.Bottom));

        var corners = new (double X, double Y)[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = Clamp(Project(
                middle[0] + ((i & 1) != 0 ? 1 : -1) * half[0],
                middle[1] + ((i & 2) != 0 ? 1 : -1) * half[1],
                middle[2] + ((i & 4) != 0 ? 1 : -1) * half[2]));
        }
        for (int i = 0; i < 8; i++)
            for (int bit = 1; bit < 8; bit <<= 1)
                if ((i & bit) == 0)
                    sb.Append(Line(corners[i].X, corners[i].Y, corners[i | bit].X, corners[i | bit].Y, Theme.Grid, 1));

        var origin = Clamp(Project(middle[0], middle[1], middle[2]));
        for (int axis = 0; axis < 3; axis++)
        {
            var tip = new[] { middle[0], middle[1], middle[2] };
            tip[axis] += half[axis];
            var point = Clamp(Project(tip[0], tip[1], tip[2]));
            sb.Append(Line(origin.X, origin.Y, point.X, point.Y, colors[axis], 2));
            Text(sb, Math.Clamp(point.X + 5, rect.Left, rect.Right - 30), point.Y - 7,
                Escape(names[axis]), colors[axis], 12, weight: 700);
        }

        if (count > 1)
        {
            var points = new List<(double X, double Y)>();
            for (int i = 0; i < count; i++)
                points.Add(Clamp(Project(series[0][i], series[1][i], series[2][i])));
            sb.Append(Path(points, WithAlpha(colors[2], 0.6), 2));
            var current = points[^1];
            sb.Append($"<circle cx=\"{F(current.X)}\" cy=\"{F(current.Y)}\" r=\"12\" fill=\"{WithAlpha(colors[2], 0.25)}\"/>");
            sb.Append($"<circle cx=\"{F(current.X)}\" cy=\"{F(current.Y)}\" r=\"5\" fill=\"{colors[2]}\"/>");
        }

        double legendX = rect.Left + 10;
        for (int i = 0; i < 3; i++)
        {
            string label = $"{Escape(names[i])} = {(series[i].Length > 0 ? F(series[i][^1]) : "0")}";
            Text(sb, legendX, rect.Top + 18, label, colors[i], 13, weight: 700);
            legendX += label.Length * 7.5 + 24;
        }
        if (context.Time3DAxis == 0 && timeAxis > 0)
        {
            Text(sb, rect.Left + 10, rect.Bottom - 8,
                Lang.T("2 канала: время → X (авто)", "2 channels: time → X (auto)"),
                Theme.Axis, 11);
        }
        Text(sb, rect.Right - 10, rect.Bottom - 8,
            Lang.T("Перетаскивай мышью, чтобы вращать", "Drag with the mouse to rotate"),
            Theme.Text, 11, "end");
    }

    private static void DrawFft(StringBuilder sb, PlotContext context)
    {
        var rect = Plot;
        var channel = context.Visible.FirstOrDefault();
        if (channel == null)
        {
            Hint(sb, rect, Lang.T("Нет видимых каналов — спектр строится по первому видимому",
                "No visible channels — the spectrum uses the first visible one"));
            return;
        }

        var data = DataHub.Snapshot(channel);
        int n = 1;
        while (n * 2 <= Math.Min(data.Length, 4096))
            n <<= 1;
        if (n < 16)
        {
            Hint(sb, rect, $"{Lang.T("Спектр", "Spectrum")} {Escape(channel.Name)}: " +
                $"{Lang.T("мало данных (нужно минимум 16 точек, есть", "not enough data (need at least 16 points, have")} {data.Length})");
            return;
        }

        double sampleRate = Math.Max(PlotSettings.SampleRate, 1e-6);
        double[] magnitude = Fft.MagnitudeSpectrum(data.TakeLast(n).ToArray(), PlotSettings.FftWindow);
        bool log = PlotSettings.FftLog;
        double max = magnitude.Skip(1).DefaultIfEmpty(0).Max();
        if (max <= 0)
            max = 1;
        double ymin, ymax;
        if (log)
        {
            ymax = 20 * Math.Log10(max) + 6; // запас, чтобы пик не упирался в верхний край
            ymin = ymax - 80;
        }
        else
        {
            ymin = 0;
            ymax = max * 1.1;
        }

        DrawGrid(sb, rect, 4, 8);
        for (int i = 0; i <= 4; i++)
        {
            double y = rect.Top + rect.Height * i / 4;
            double value = ymax - (ymax - ymin) * i / 4;
            Text(sb, rect.Left - 10, y + 4,
                log ? $"{value:F0} {Lang.T("дБ", "dB")}" : F(value), Theme.Text, 12, "end");
        }
        for (int i = 1; i <= 8; i++)
        {
            double x = rect.Left + rect.Width * i / 8;
            double hz = sampleRate / 2 * i / 8;
            string label = hz >= 1000 ? $"{hz / 1000:0.##} {Lang.T("кГц", "kHz")}" : $"{hz:0.#} {Lang.T("Гц", "Hz")}";
            Text(sb, x, rect.Bottom + 22, label, Theme.Text, 11, "middle");
        }

        var points = new List<(double X, double Y)>();
        for (int k = 0; k < magnitude.Length; k++)
        {
            double value = log
                ? Math.Max(20 * Math.Log10(Math.Max(magnitude[k], 1e-12)), ymin)
                : magnitude[k];
            points.Add((rect.Left + rect.Width * k / (magnitude.Length - 1.0),
                        Y(value, ymin, ymax, rect)));
        }
        sb.Append(Path(points, channel.Color, 2));

        int peak = 1;
        for (int k = 2; k < magnitude.Length; k++)
            if (magnitude[k] > magnitude[peak])
                peak = k;
        double peakHz = sampleRate / 2.0 * peak / (magnitude.Length - 1);
        string peakDb = log ? $", {20 * Math.Log10(Math.Max(magnitude[peak], 1e-12)):F0} {Lang.T("дБ", "dB")}" : "";
        Text(sb, rect.Left + 10, rect.Top + 20,
            $"{Escape(channel.Name)}: {Lang.T("пик", "peak")} {peakHz:0.##} {Lang.T("Гц", "Hz")}{peakDb}",
            channel.Color, 13, weight: 700);
        Text(sb, rect.Left + 10, rect.Top + 39,
            $"FFT {n} {Lang.T("точек", "pts")}, Δf = {sampleRate / n:0.###} {Lang.T("Гц", "Hz")}   " +
            (log ? Lang.T("логарифмическая шкала", "logarithmic scale") : Lang.T("линейная шкала", "linear scale")),
            Theme.Text, 11);

        SetLast(0, sampleRate / 2, ymin, ymax);
    }

    private static string TimePath(double[] data, double xmin, double xmax, double ymin, double ymax, PlotRect rect)
    {
        int first = Math.Max(0, (int)Math.Ceiling(xmin));
        int last = Math.Min(data.Length - 1, (int)Math.Floor(xmax));
        if (last < first)
            return "";
        var points = new List<(double X, double Y)>(last - first + 1);
        for (int i = first; i <= last; i++)
            points.Add((X(i, xmin, xmax, rect), Math.Clamp(Y(data[i], ymin, ymax, rect), rect.Top, rect.Bottom)));
        return Path(points, "", 0, raw: true);
    }

    private static string XyPath(double[] xs, double[] data, int count,
        double xmin, double xmax, double ymin, double ymax, PlotRect rect)
    {
        if (count < 2)
            return "";
        var points = new List<(double X, double Y)>(count);
        for (int i = 0; i < count; i++)
        {
            points.Add((Math.Clamp(X(xs[i], xmin, xmax, rect), rect.Left, rect.Right),
                        Math.Clamp(Y(data[i], ymin, ymax, rect), rect.Top, rect.Bottom)));
        }
        return Path(points, "", 0, raw: true);
    }

    private static string Path(IReadOnlyList<(double X, double Y)> points, string color, double width, bool raw = false)
    {
        if (points.Count == 0)
            return "";
        var sb = new StringBuilder(points.Count * 18);
        sb.Append("M ").Append(F(points[0].X)).Append(' ').Append(F(points[0].Y));
        for (int i = 1; i < points.Count; i++)
            sb.Append(" L ").Append(F(points[i].X)).Append(' ').Append(F(points[i].Y));
        if (raw)
            return sb.ToString();
        return $"<path d=\"{sb}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{F(width)}\" stroke-linejoin=\"round\" stroke-linecap=\"round\" vector-effect=\"non-scaling-stroke\"/>";
    }

    private static double X(double value, double xmin, double xmax, PlotRect rect) =>
        rect.Left + rect.Width * (value - xmin) / (xmax - xmin);

    private static double Y(double value, double ymin, double ymax, PlotRect rect) =>
        rect.Bottom - rect.Height * (value - ymin) / (ymax - ymin);

    private static void SetLast(double xmin, double xmax, double ymin, double ymax)
    {
        PlotSettings.LastXMin = xmin;
        PlotSettings.LastXMax = xmax;
        PlotSettings.LastYMin = ymin;
        PlotSettings.LastYMax = ymax;
    }

    private static SmoothKind EffectiveSmoothKind(Channel channel) =>
        channel.Smooth != SmoothKind.None ? channel.Smooth : PlotSettings.GlobalSmooth;

    private static bool EffectiveSmooth(Channel channel, out int window)
    {
        window = channel.Smooth != SmoothKind.None ? channel.SmoothWindow : PlotSettings.GlobalSmoothWindow;
        return EffectiveSmoothKind(channel) != SmoothKind.None;
    }

    private static string Dash(LineKind kind) => kind switch
    {
        LineKind.Dash => "8 5",
        LineKind.Dot => "2 5",
        LineKind.DashDot => "9 4 2 4",
        _ => "none",
    };

    private static string Line(double x1, double y1, double x2, double y2, string color,
        double width, string dash = "none") =>
        $"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"{color}\" stroke-width=\"{F(width)}\" stroke-dasharray=\"{dash}\" vector-effect=\"non-scaling-stroke\"/>";

    private static void Text(StringBuilder sb, double x, double y, string text, string color,
        double size, string anchor = "start", int weight = 400) =>
        sb.Append($"<text x=\"{F(x)}\" y=\"{F(y)}\" fill=\"{color}\" font-family=\"Segoe UI, sans-serif\" font-size=\"{F(size)}\" font-weight=\"{weight}\" text-anchor=\"{anchor}\">{text}</text>");

    private static void Hint(StringBuilder sb, PlotRect rect, string text) =>
        Text(sb, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2, text, Theme.Text, 15, "middle");

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&apos;");

    private static string WithAlpha(string hex, double alpha)
    {
        if (hex.StartsWith('#') && hex.Length == 7)
        {
            int a = (int)Math.Clamp(alpha * 255, 0, 255);
            // SVG/CSS ожидает #RRGGBBAA, а не #AARRGGBB.
            return $"#{hex[1..]}{a:x2}";
        }
        return hex;
    }
}
