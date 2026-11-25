using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace SfxStub;

internal static class Program
{
    private const string Version = "2.0.0";
    private const string AppExe = "SerialPlotter.exe";

    [STAThread]
    private static void Main()
    {
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SerialPlotter", "App");
            string marker = Path.Combine(root, "version.txt");

            if (File.Exists(marker) && File.ReadAllText(marker).Trim() != Version)
                CloseRunningApp();

            if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != Version)
            {
                Extract(root);
                Directory.CreateDirectory(root);
                File.WriteAllText(marker, Version);
            }

            string app = Path.Combine(root, AppExe);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(app)
            {
                UseShellExecute = true,
                WorkingDirectory = root,
            });
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show(
                "Не удалось запустить Serial Plotter:\n" + ex.Message,
                "Serial Plotter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void CloseRunningApp()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("SerialPlotter"))
        {
            try
            {
                if (process.Id != System.Diagnostics.Process.GetCurrentProcess().Id)
                    process.Kill();
            }
            catch { }
        }
        System.Threading.Thread.Sleep(1200);
    }

    private static void Extract(string root)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("app.zip")
            ?? throw new InvalidOperationException("app.zip не найден в ресурсах");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        string fullRoot = Path.GetFullPath(root);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            string dest = Path.GetFullPath(Path.Combine(root,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static void Log(Exception ex)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SerialPlotter");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "sfx.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
        }
        catch { }
    }
}
