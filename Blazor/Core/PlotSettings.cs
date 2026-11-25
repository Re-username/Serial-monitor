namespace SerialPlotter.Blazor.Core;

public static class PlotSettings
{
    public static bool YAuto = true;
    public static bool XAuto = true;

    public static double YMin = -1, YMax = 1;
    public static double XMin = 0, XMax = DataHub.HISTORY;

    public const int AutoWindow = 400;

    public static double? ViewXMin, ViewXMax, ViewYMin, ViewYMax;
    public static double? MarkerA, MarkerB;

    public static double SampleRate = 50;
    public static bool FftLog = true;
    public static FftWindowKind FftWindow = FftWindowKind.Hann;

    public static SmoothKind GlobalSmooth = SmoothKind.None;
    public static int GlobalSmoothWindow = 9;
    public static int PolarTrail = 400;

    public static double LastXMin, LastXMax, LastYMin, LastYMax;

    public static void ResetView()
    {
        ViewXMin = ViewXMax = ViewYMin = ViewYMax = null;
        MarkerA = MarkerB = null;
    }

    public static (double Min, double Max) ResolveY(bool auto, double dataMin, double dataMax)
    {
        if (auto)
            return (dataMin, dataMax);
        double min = Math.Min(YMin, YMax), max = Math.Max(YMin, YMax);
        if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }
        return (min, max);
    }

    public static (double Min, double Max) ResolveX(bool auto, double dataMin, double dataMax, bool timeMode)
    {
        if (auto)
            return (dataMin, dataMax);
        if (timeMode)
        {
            int window = (int)Math.Clamp(Math.Max(XMax, XMin), 2, DataHub.HISTORY);
            return (0, window - 1);
        }
        double min = Math.Min(XMin, XMax), max = Math.Max(XMin, XMax);
        if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }
        return (min, max);
    }
}
