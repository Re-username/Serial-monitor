using System.Text.Json;

namespace SerialPlotter;

// Файл настроек (%AppData%/SerialPlotter/settings.json): порт, бодрейт,
// тема, каналы (имена, цвета, видимость, линии, сглаживание), ось X, FFT.
public static class Settings
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public sealed class ChannelInfo
    {
        public string Name { get; set; } = "";
        public int ColorArgb { get; set; }
        public bool Visible { get; set; } = true;
        public float LineWidth { get; set; } = 2;
        public int LineStyle { get; set; }
        public int Smooth { get; set; }
        public int SmoothWindow { get; set; } = 5;
    }

    public sealed class Data
    {
        public string Port { get; set; } = "";
        public int Baud { get; set; } = 115200;
        public int Theme { get; set; } = (int)ThemeKind.Cyberpunk;
        public int XIndex { get; set; } = -1;
        public double SampleRate { get; set; } = 50;
        public bool FftLog { get; set; } = true;
        public int FftWindow { get; set; } = (int)FftWindowKind.Hann;
        public int GlobalSmooth { get; set; }
        public int GlobalSmoothWindow { get; set; } = 9;
        public int Language { get; set; }        // 0 — русский, 1 — English
        public int PolarTrail { get; set; } = 400; // хвост траектории в полярном режиме, точек
        public List<ChannelInfo> Channels { get; set; } = new();
    }

    public static Data Current { get; private set; } = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SerialPlotter", "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch { /* битый файл — молча работаем на дефолтах */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOpts));
        }
        catch { /* настройки — не критично */ }
    }
}
