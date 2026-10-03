using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace SerialPlotter;

// Форма сигнала виртуального канала. Названия выводятся в интерфейс как есть.
public enum WaveKind { Синус, Прямоугольник, Пила, Шум, Постоянный, Блуждание }

// Один виртуальный канал симулятора.
public sealed class VChannel
{
    public string Name { get; set; } = "CH";
    public WaveKind Wave { get; set; } = WaveKind.Синус;
    public double Freq { get; set; } = 1;        // Гц
    public double Amplitude { get; set; } = 1;
    public double Offset { get; set; }

    private double _walk;                        // состояние для «Блуждание»

    public void Reset() => _walk = Offset;

    public double Compute(double t, Random rnd)
    {
        double w = 2 * Math.PI * Freq * t;
        return Wave switch
        {
            WaveKind.Прямоугольник => Offset + Amplitude * (Math.Sin(w) >= 0 ? 1 : -1),
            WaveKind.Пила => Offset + Amplitude * (2 * (Freq * t % 1.0) - 1),
            WaveKind.Шум => Offset + Amplitude * (2 * rnd.NextDouble() - 1),
            WaveKind.Постоянный => Offset,
            WaveKind.Блуждание => _walk = Math.Clamp(
                _walk + Amplitude * 0.05 * (2 * rnd.NextDouble() - 1),
                Offset - Amplitude, Offset + Amplitude),
            _ => Offset + Amplitude * Math.Sin(w),
        };
    }
}

// Генератор виртуальных данных: выдаёт строки в том же формате, что и COM-порт
// ("v1,v2,..." 50 раз в секунду, при старте — строка-заголовок с именами).
// Графики и консоль не отличают его от реального порта.
public sealed class Simulator : IDisposable
{
    public BindingList<VChannel> Channels { get; } = new();

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 20 };
    private readonly Stopwatch _sw = new();
    private readonly Random _rnd = new();

    public bool Running { get; private set; }

    public event Action<string>? LineGenerated;
    public event Action? Started;
    public event Action? Stopped;

    public Simulator() => _timer.Tick += (_, _) => Tick();

    public void Start()
    {
        if (Channels.Count == 0)
            throw new InvalidOperationException("Добавь хотя бы один виртуальный канал.");

        foreach (var c in Channels)
        {
            if (string.IsNullOrWhiteSpace(c.Name)) c.Name = "CH";
            c.Name = c.Name.Replace(',', '_').Replace(' ', '_').Trim();
            c.Reset();
        }

        Running = true;
        _sw.Restart();
        Started?.Invoke(); // сначала событие: MainForm сбросит старые каналы и закроет порт
        LineGenerated?.Invoke(string.Join(",", Channels.Select(c => c.Name))); // заголовок
        _timer.Start();
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _timer.Stop();
        Stopped?.Invoke();
    }

    private void Tick()
    {
        double t = _sw.Elapsed.TotalSeconds;
        string line = string.Join(",",
            Channels.Select(c => c.Compute(t, _rnd).ToString("0.###", CultureInfo.InvariantCulture)));
        LineGenerated?.Invoke(line);
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
