namespace SerialPlotter.Blazor.Core;

public static class Lang
{
    public static bool English;

    public static string T(string ru, string en) => English ? en : ru;
}
