namespace SerialPlotter.Blazor.Core;

public enum ThemeKind
{
    Dark,
    Light,
    Cyberpunk,
}

public static class Theme
{
    public static ThemeKind Kind = ThemeKind.Cyberpunk;

    public static readonly string[] Palette =
    {
        "#2cc9ff", "#ff2ea6", "#a8ff3e", "#b04dff",
        "#ff3b5c", "#ffd319", "#00ffc8", "#ff7a18",
    };

    public static string CssName => Kind switch
    {
        ThemeKind.Dark => "dark",
        ThemeKind.Light => "light",
        _ => "cyberpunk",
    };

    public static string Grid => Kind switch
    {
        ThemeKind.Light => "#c9d2df",
        ThemeKind.Dark => "#263243",
        _ => "#15324a",
    };

    public static string Axis => Kind switch
    {
        ThemeKind.Light => "#687586",
        ThemeKind.Dark => "#7d8da3",
        _ => "#2cc9ff",
    };

    public static string Text => Kind switch
    {
        ThemeKind.Light => "#17202b",
        ThemeKind.Dark => "#d9e4f2",
        _ => "#d6d6d6",
    };

    public static string PlotBackground => Kind switch
    {
        ThemeKind.Light => "#ffffff",
        ThemeKind.Dark => "#101722",
        _ => "#010713",
    };
}
