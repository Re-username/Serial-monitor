namespace SerialPlotter.Blazor.Core;

public enum LineKind
{
    Solid,
    Dash,
    Dot,
    DashDot,
}

public enum SmoothKind
{
    None,
    MovingAverage,
    Median,
    Gaussian,
    Exponential,
}

public sealed class Channel
{
    public Channel(string name, string color)
    {
        Name = name;
        Color = color;
    }

    public string Name;
    public string Color;
    public bool Visible = true;
    public float LineWidth = 2;
    public LineKind LineStyle = LineKind.Solid;
    public SmoothKind Smooth = SmoothKind.None;
    public int SmoothWindow = 5;
    public double Last;
    public readonly Queue<double> Data = new();

    public double[] Snapshot() => Data.ToArray();
}
