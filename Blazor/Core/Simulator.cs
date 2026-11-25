using System.Diagnostics;
using System.Globalization;

namespace SerialPlotter.Blazor.Core;

public enum WaveKind
{
    Sine,
    Square,
    Saw,
    Noise,
    Constant,
    Walk,
}

public sealed class VirtualChannel
{
    public string Name { get; set; } = "CH";
    public WaveKind Wave { get; set; } = WaveKind.Sine;
    public double Frequency { get; set; } = 1;
    public double Amplitude { get; set; } = 1;
    public double Offset { get; set; }

    private double _walk;

    public void Reset() => _walk = Offset;

    public double Compute(double time, Random random) => Compute(time, random, ref _walk);

    public double Compute(double time, Random random, ref double walk)
    {
        double w = 2 * Math.PI * Frequency * time;
        return Wave switch
        {
            WaveKind.Square => Offset + Amplitude * (Math.Sin(w) >= 0 ? 1 : -1),
            WaveKind.Saw => Offset + Amplitude * (2 * (Frequency * time % 1.0) - 1),
            WaveKind.Noise => Offset + Amplitude * (2 * random.NextDouble() - 1),
            WaveKind.Constant => Offset,
            WaveKind.Walk => walk = Math.Clamp(
                walk + Amplitude * 0.05 * (2 * random.NextDouble() - 1),
                Offset - Amplitude, Offset + Amplitude),
            _ => Offset + Amplitude * Math.Sin(w),
        };
    }
}

public sealed class Simulator : IDisposable
{
    private readonly object _sync = new();
    private readonly Timer _timer;
    private readonly Random _random = new();
    private double _time;

    public List<VirtualChannel> Channels { get; } = new();

    public bool Running { get; private set; }
    public bool ExternalClock { get; private set; }
    public double ExternalRate { get; set; } = 50;

    public event Action<string>? LineGenerated;
    public event Action? Started;
    public event Action? Stopped;

    public Simulator()
    {
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        lock (_sync)
        {
            if (Channels.Count == 0)
                throw new InvalidOperationException(Lang.T("Добавь хотя бы один виртуальный канал.",
                    "Add at least one virtual channel."));

            foreach (var channel in Channels)
            {
                if (string.IsNullOrWhiteSpace(channel.Name))
                    channel.Name = "CH";
                channel.Name = channel.Name.Replace(',', '_').Replace(' ', '_').Trim();
                channel.Reset();
            }

            Running = true;
            _time = 0;
        }

        Started?.Invoke();
        LineGenerated?.Invoke(string.Join(",", Channels.Select(c => c.Name)));
        lock (_sync)
        {
            if (!ExternalClock)
                _timer.Change(0, 20);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!Running)
                return;
            Running = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        Stopped?.Invoke();
    }

    private void Tick()
    {
        string? line = null;
        lock (_sync)
        {
            if (!Running || ExternalClock)
                return;
            _time += 1.0 / 50;
            line = FormatLine(_time);
        }
        if (line != null)
            LineGenerated?.Invoke(line);
    }

    public void ExternalTick()
    {
        string? line = null;
        lock (_sync)
        {
            if (!Running || !ExternalClock)
                return;
            _time += 1.0 / Math.Clamp(ExternalRate, 1, 1000);
            line = FormatLine(_time);
        }
        if (line != null)
            LineGenerated?.Invoke(line);
    }

    public void UseExternalClock()
    {
        lock (_sync)
        {
            ExternalClock = true;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void UseInternalClock()
    {
        lock (_sync)
        {
            ExternalClock = false;
            ExternalRate = 50;
            if (Running)
                _timer.Change(0, 20);
        }
    }

    public void GenerateHistory(int count, double rate)
    {
        double dt = 1.0 / Math.Clamp(rate, 1, 1000);
        var walks = Channels.Select(c => c.Offset).ToArray();
        for (int i = 0; i < count; i++)
        {
            double time = (i - count) * dt;
            string line;
            lock (_sync)
            {
                line = string.Join(",", Channels.Select((channel, index) =>
                    channel.Compute(time, _random, ref walks[index])
                        .ToString("0.###", CultureInfo.InvariantCulture)));
            }
            LineGenerated?.Invoke(line);
        }
    }

    private string FormatLine(double time) =>
        string.Join(",", Channels.Select(channel =>
            channel.Compute(time, _random).ToString("0.###", CultureInfo.InvariantCulture)));

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
