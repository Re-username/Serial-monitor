namespace SerialPlotter;

// Точка входа. Вся логика — в MainForm и соседних файлах.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
