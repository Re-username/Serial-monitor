using System.Globalization;
using System.IO.Ports;
using System.Text;
using Microsoft.JSInterop;
using SerialPlotter.Blazor.Core;

namespace SerialPlotter.Blazor.Services;

public sealed class AppState : IDisposable
{
    private static readonly int[] BaudRates = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };

    private readonly object _consoleSync = new();
    private readonly Queue<string> _console = new();
    private readonly Timer _saveTimer;
    private bool _initialized;

    private int _portChannelOffset, _simChannelOffset;
    private int _portCount, _simCount;
    private int _rateWindowLines;
    private DateTime _rateWindowStart = DateTime.Now;
    private double _portRate = 50;
    private bool _restoreXIndexPending = true;

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    public SerialLink Serial { get; } = new();
    public Simulator Sim { get; } = new();

    public string[] PortNames { get; private set; } = Array.Empty<string>();
    public string? SelectedPort { get; set; }
    public string BaudText { get; set; } = "115200";
    public bool Connecting { get; private set; }
    public string Status { get; set; } = "не подключено";
    public string? ErrorMessage { get; set; }

    public PlotMode Mode { get; set; } = PlotMode.Time;
    public int XIndex { get; set; } = -1;
    public double RotationX { get; set; } = 0.5;
    public double RotationY { get; set; } = -0.7;
    public int Time3DAxis { get; set; }
    public bool Spin3D { get; set; } = true;
    public bool DtrReset { get; set; }
    public string PortEncoding { get; set; } = "utf-8";
    public string ThemeName { get; set; } = "cyberpunk";

    public bool ShowHelp { get; set; }
    public bool ShowChannels { get; set; } = true;
    public bool ShowSimulator { get; set; }
    public bool ShowRanges { get; set; }
    public bool ShowExportRange { get; set; }
    public Channel? EditingChannel { get; set; }

    public string PortEncodingName => PortEncoding switch
    {
        "cp1251" => "Windows-1251",
        "ascii" => "ASCII",
        "utf-16" => "UTF-16",
        _ => "UTF-8",
    };

    public AppState()
    {
        _saveTimer = new Timer(_ =>
        {
            SaveSettings();
            Changed?.Invoke();
        }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        Settings.Load();
        Lang.English = Settings.Current.Language == 1;
        Theme.Kind = (ThemeKind)Math.Clamp(Settings.Current.Theme, 0, 2);
        ThemeName = Theme.CssName;
        DtrReset = Settings.Current.DtrReset;
        PortEncoding = Settings.Current.PortEncoding;
        BaudText = Settings.Current.Baud > 0 ? Settings.Current.Baud.ToString() : "115200";
        XIndex = Settings.Current.XIndex;
        PlotSettings.SampleRate = Math.Clamp(Settings.Current.SampleRate, 1, 1_000_000);
        PlotSettings.FftLog = Settings.Current.FftLog;
        PlotSettings.FftWindow = (FftWindowKind)Math.Clamp(Settings.Current.FftWindow, 0, 4);
        PlotSettings.GlobalSmooth = (SmoothKind)Math.Clamp(Settings.Current.GlobalSmooth, 0, 4);
        PlotSettings.GlobalSmoothWindow = Math.Clamp(Settings.Current.GlobalSmoothWindow, 3, 501);
        PlotSettings.PolarTrail = Math.Clamp(Settings.Current.PolarTrail, 10, 10000);

        DataHub.SeedChannels = Settings.Current.Channels.Select(c => new DataHub.SavedChannel
        {
            Name = c.Name,
            Color = c.Color,
            Visible = c.Visible,
            LineWidth = c.LineWidth,
            LineStyle = (LineKind)Math.Clamp(c.LineStyle, 0, 3),
            Smooth = (SmoothKind)Math.Clamp(c.Smooth, 0, 4),
            SmoothWindow = c.SmoothWindow,
        }).ToList();

        Serial.LineReceived += OnSerialLine;
        Serial.Closed += OnSerialClosed;
        Serial.Failed += OnSerialFailed;
        Sim.LineGenerated += line =>
        {
            DataHub.PushLine(line, _simChannelOffset);
            _simCount = Math.Max(_simCount, DataHub.Count - _simChannelOffset);
        };
        Sim.Started += OnSimStarted;
        Sim.Stopped += OnSimStopped;
        DataHub.RawLine += EnqueueConsole;
        DataHub.ChannelsChanged += OnChannelsChanged;

        SelectedPort = string.IsNullOrWhiteSpace(Settings.Current.Port) ? null : Settings.Current.Port;
        _ = RefreshPortsAsync();
        StartDemoFromEnvironment();
        UpdateStatus();
    }

    public static IReadOnlyList<int> Bauds => BaudRates;

    public async Task RefreshPortsAsync()
    {
        string? previous = SelectedPort ?? Settings.Current.Port;
        string[] ports = await Task.Run(SerialPort.GetPortNames);
        PortNames = ports;
        SelectedPort = previous != null && ports.Contains(previous) ? previous
            : ports.Length > 0 ? ports[0] : null;
        Changed?.Invoke();
    }

    private void StartDemoFromEnvironment()
    {
        string? demo = Environment.GetEnvironmentVariable("SERIALPLOTTER_DEMO");
        if (string.IsNullOrWhiteSpace(demo))
            return;

        string? theme = Environment.GetEnvironmentVariable("SERIALPLOTTER_THEME");
        if (!string.IsNullOrWhiteSpace(theme))
            ApplyTheme(theme);

        if (Enum.TryParse(demo, ignoreCase: true, out PlotMode mode))
            Mode = mode;
        if (int.TryParse(Environment.GetEnvironmentVariable("SERIALPLOTTER_3D_TIME"), out int timeAxis))
            Time3DAxis = Math.Clamp(timeAxis, 0, 3);

        Sim.Channels.Clear();
        var demoChannels = new List<VirtualChannel>
        {
            new() { Name = "X", Wave = WaveKind.Sine, Frequency = 1.2, Amplitude = 1.0 },
            new() { Name = "Y", Wave = WaveKind.Sine, Frequency = 1.7, Amplitude = 0.75 },
            new() { Name = "Z", Wave = WaveKind.Saw, Frequency = 0.6, Amplitude = 0.6 },
        };
        int channelCount = demoChannels.Count;
        if (int.TryParse(Environment.GetEnvironmentVariable("SERIALPLOTTER_DEMO_CHANNELS"), out int requestedCount))
            channelCount = Math.Clamp(requestedCount, 1, demoChannels.Count);
        Sim.Channels.AddRange(demoChannels.Take(channelCount));

        if (double.TryParse(Environment.GetEnvironmentVariable("SERIALPLOTTER_MARKER_A"), out double markerA))
            PlotSettings.MarkerA = markerA;
        if (double.TryParse(Environment.GetEnvironmentVariable("SERIALPLOTTER_MARKER_B"), out double markerB))
            PlotSettings.MarkerB = markerB;

        PlotSettings.SampleRate = 50;
        StartSimulator();
    }

    public async Task ToggleConnectAsync()
    {
        if (Serial.IsOpen)
        {
            Serial.Close();
            Status = Lang.T("отключено", "disconnected");
            Changed?.Invoke();
            return;
        }

        if (SelectedPort == null)
        {
            ErrorMessage = Lang.T("Нет доступных COM-портов. Подключи устройство и обнови список.",
                "No COM ports available. Connect the device and refresh the list.");
            return;
        }
        if (!int.TryParse(BaudText.Trim(), out int baud) || baud <= 0)
        {
            ErrorMessage = Lang.T("Неверное значение бодрейта.", "Invalid baud rate value.");
            return;
        }

        Connecting = true;
        Status = $"{Lang.T("подключение к", "connecting to")} {SelectedPort}…";
        Changed?.Invoke();

        string? error = await Serial.OpenAsync(SelectedPort, baud, DtrReset, GetPortEncoding());
        Connecting = false;
        if (error != null)
        {
            Status = Lang.T("не подключено", "not connected");
            ErrorMessage = $"{Lang.T("Не удалось открыть", "Failed to open")} {SelectedPort}:\n{error}";
            Changed?.Invoke();
            return;
        }

        if (!Sim.Running && DataHub.Count > 0)
            DataHub.Reset();
        _portChannelOffset = Sim.Running ? _simChannelOffset + _simCount : 0;
        _portCount = 0;
        if (Sim.Running)
            Sim.UseExternalClock();

        Status = $"{SelectedPort} @ {baud}" + (Sim.Running ? $" + {Lang.T("симулятор", "simulator")}" : "");
        SaveSettings();
        Changed?.Invoke();
    }

    public void TogglePause()
    {
        DataHub.Paused = !DataHub.Paused;
        Changed?.Invoke();
    }

    public void FullReset()
    {
        Serial.DiscardBuffers();
        DataHub.Reset();
        PlotSettings.ResetView();
        _portChannelOffset = _simChannelOffset = 0;
        _portCount = _simCount = 0;
        ClearConsole();
        Status = Lang.T("сброс: буферы порта, графики и вывод очищены",
            "reset: port buffers, plots and output cleared");
        Changed?.Invoke();
    }

    public bool SendLine(string line)
    {
        if (!Serial.IsOpen)
            return false;
        Serial.Send(line);
        EnqueueConsole(">> " + line);
        return true;
    }

    public void StartSimulator()
    {
        try
        {
            Sim.Start();
            ShowSimulator = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        Changed?.Invoke();
    }

    public void StopSimulator() => Sim.Stop();

    private void OnSerialLine(string line)
    {
        DataHub.PushLine(line, _portChannelOffset);
        _portCount = Math.Max(_portCount, DataHub.Count - _portChannelOffset);
        TrackPortRate();
        if (Sim.Running && Sim.ExternalClock)
            Sim.ExternalTick();
    }

    private void OnSerialClosed()
    {
        if (Sim.Running && Sim.ExternalClock)
            Sim.UseInternalClock();
        UpdateStatus();
        Changed?.Invoke();
    }

    private void OnSerialFailed(string message)
    {
        Status = Lang.T("ошибка порта", "port error");
        ErrorMessage = Lang.T("Порт закрылся с ошибкой", "The port closed with an error") + $":\n{message}";
        Changed?.Invoke();
    }

    private void OnSimStarted()
    {
        if (_simCount > 0 && _simChannelOffset + _simCount <= DataHub.Count)
        {
            for (int i = 0; i < _simCount; i++)
                DataHub.ClearChannel(_simChannelOffset + i);
            if (_simChannelOffset + Sim.Channels.Count < DataHub.Count)
                DataHub.RemoveChannelsFrom(_simChannelOffset + Sim.Channels.Count);
        }
        else
        {
            if (!Serial.IsOpen && DataHub.Count > 0)
            {
                DataHub.Reset();
                _portChannelOffset = _portCount = 0;
            }
            _simChannelOffset = Serial.IsOpen
                ? _portChannelOffset + Math.Max(_portCount, DataHub.Count - _portChannelOffset)
                : 0;
        }
        _simCount = Sim.Channels.Count;

        if (Serial.IsOpen)
        {
            Sim.UseExternalClock();
            Sim.ExternalRate = _portRate;
            int portPoints = _portChannelOffset < DataHub.Count
                ? DataHub.Channels.Skip(_portChannelOffset).Max(c => DataHub.Snapshot(c).Length)
                : 0;
            if (portPoints > 1)
                Sim.GenerateHistory(portPoints, _portRate);
        }
        else
        {
            Sim.UseInternalClock();
        }

        Status = (Serial.IsOpen ? $"{SelectedPort} @ {BaudText} + " : "") +
                 $"{Lang.T("симулятор", "simulator")}: {_simCount} {Lang.T("канал(ов)", "channel(s)")}";
        Changed?.Invoke();
    }

    private void OnSimStopped()
    {
        Status = Serial.IsOpen ? $"{SelectedPort} @ {BaudText}" : Lang.T("не подключено", "not connected");
        Changed?.Invoke();
    }

    private void OnChannelsChanged()
    {
        if (_restoreXIndexPending && Settings.Current.XIndex >= 0 && Settings.Current.XIndex < DataHub.Count)
        {
            XIndex = Settings.Current.XIndex;
            _restoreXIndexPending = false;
        }
        if (XIndex >= DataHub.Count)
            XIndex = -1;
        ScheduleSave();
        Changed?.Invoke();
    }

    public void SetXIndex(int index)
    {
        XIndex = index;
        _restoreXIndexPending = false;
        ScheduleSave();
        Changed?.Invoke();
    }

    private void TrackPortRate()
    {
        _rateWindowLines++;
        double seconds = (DateTime.Now - _rateWindowStart).TotalSeconds;
        if (seconds < 1)
            return;
        _portRate = _rateWindowLines / seconds;
        if (Sim.Running && Sim.ExternalClock)
            Sim.ExternalRate = _portRate;
        _rateWindowLines = 0;
        _rateWindowStart = DateTime.Now;
    }

    private void UpdateStatus()
    {
        Status = Serial.IsOpen ? $"{SelectedPort} @ {BaudText}"
            : Sim.Running ? Lang.T("симулятор", "simulator")
            : Lang.T("не подключено", "not connected");
    }

    private Encoding GetPortEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return PortEncoding switch
        {
            "cp1251" => Encoding.GetEncoding(1251),
            "ascii" => Encoding.ASCII,
            "utf-16" => Encoding.Unicode,
            _ => Encoding.UTF8,
        };
    }

    public void SetTheme(string theme, IJSRuntime js)
    {
        ApplyTheme(theme);
        _ = js.InvokeVoidAsync("spSetTheme", theme);
        ScheduleSave();
        Changed?.Invoke();
    }

    private void ApplyTheme(string theme)
    {
        ThemeName = theme;
        Theme.Kind = theme switch
        {
            "dark" => ThemeKind.Dark,
            "light" => ThemeKind.Light,
            _ => ThemeKind.Cyberpunk,
        };
    }

    public void SetLanguage(bool english)
    {
        Lang.English = english;
        ScheduleSave();
        Changed?.Invoke();
    }

    public void ScheduleSave()
    {
        _saveTimer.Change(500, Timeout.Infinite);
    }

    public void SaveSettings()
    {
        var settings = Settings.Current;
        settings.Port = SelectedPort ?? "";
        if (int.TryParse(BaudText.Trim(), out int baud) && baud > 0)
            settings.Baud = baud;
        settings.Theme = (int)Theme.Kind;
        settings.XIndex = XIndex;
        settings.SampleRate = PlotSettings.SampleRate;
        settings.FftWindow = (int)PlotSettings.FftWindow;
        settings.DtrReset = DtrReset;
        settings.PortEncoding = PortEncoding;
        settings.FftLog = PlotSettings.FftLog;
        settings.GlobalSmooth = (int)PlotSettings.GlobalSmooth;
        settings.GlobalSmoothWindow = PlotSettings.GlobalSmoothWindow;
        settings.PolarTrail = PlotSettings.PolarTrail;
        settings.Language = Lang.English ? 1 : 0;
        settings.Channels = DataHub.Channels.Select(c => new Settings.ChannelInfo
        {
            Name = c.Name,
            Color = c.Color,
            Visible = c.Visible,
            LineWidth = c.LineWidth,
            LineStyle = (int)c.LineStyle,
            Smooth = (int)c.Smooth,
            SmoothWindow = c.SmoothWindow,
        }).ToList();
        Settings.Save();
    }

    public List<string> DrainConsole(int max = 300)
    {
        var result = new List<string>();
        lock (_consoleSync)
        {
            while (_console.Count > 0 && result.Count < max)
                result.Add(_console.Dequeue());
        }
        return result;
    }

    public void ClearConsole()
    {
        lock (_consoleSync)
            _console.Clear();
    }

    private void EnqueueConsole(string line)
    {
        lock (_consoleSync)
        {
            _console.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
            if (_console.Count > 5000)
                _console.Clear();
        }
    }

    public string BuildCsv(bool useRange, double xFrom, double xTo)
    {
        var channels = DataHub.Channels;
        var arrays = channels.Select(c => DataHub.Snapshot(c)).ToArray();
        int n = arrays.Length == 0 ? 0 : arrays.Max(a => a.Length);
        bool timeAxis = XIndex < 0 || XIndex >= arrays.Length;
        double[]? xValues = timeAxis ? null : arrays[XIndex];

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", channels.Select(c => CsvCell(c.Name))));
        for (int row = 0; row < n; row++)
        {
            if (useRange)
            {
                bool include;
                if (timeAxis)
                {
                    double absolute = DataHub.TotalLines - n + row;
                    include = absolute >= xFrom && absolute <= xTo;
                }
                else
                {
                    include = xValues != null && row < xValues.Length &&
                              xValues[row] >= xFrom && xValues[row] <= xTo;
                }
                if (!include)
                    continue;
            }
            sb.AppendLine(string.Join(",", arrays.Select(a =>
                row < a.Length ? a[row].ToString("G9", CultureInfo.InvariantCulture) : "")));
        }
        return sb.ToString();
    }

    private static string CsvCell(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    public void Dispose()
    {
        SaveSettings();
        Serial.Dispose();
        Sim.Dispose();
        _saveTimer.Dispose();
    }
}
