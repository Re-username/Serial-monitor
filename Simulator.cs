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

    public double Compute(double t, Random rnd) => Compute(t, rnd, ref _walk);

    // Вычисление с внешним состоянием блуждания — для ретроспективной
    // генерации истории (Simulator.GenerateHistory)
    public double Compute(double t, Random rnd, ref double walk)
    {
        double w = 2 * Math.PI * Freq * t;
        return Wave switch
        {
            WaveKind.Прямоугольник => Offset + Amplitude * (Math.Sin(w) >= 0 ? 1 : -1),
            WaveKind.Пила => Offset + Amplitude * (2 * (Freq * t % 1.0) - 1),
            WaveKind.Шум => Offset + Amplitude * (2 * rnd.NextDouble() - 1),
            WaveKind.Постоянный => Offset,
            WaveKind.Блуждание => walk = Math.Clamp(
                walk + Amplitude * 0.05 * (2 * rnd.NextDouble() - 1),
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

    // Внешний такт: когда порт подключён, симулятор не тикает по своему
    // таймеру — один отсчёт на каждую принятую строку порта (1:1). Тогда
    // сигнал симулятора идёт строго вместе с сигналом порта, без разъезда
    public bool ExternalClock { get; set; }
    // Скорость внешнего такта, отсчётов/с (измеряется по потоку порта);
    // определяет временную базу волн
    public double ExternalRate { get; set; } = 50;

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
        _t = 0;
        Started?.Invoke(); // сначала событие: MainForm расставит смещения каналов
        LineGenerated?.Invoke(string.Join(",", Channels.Select(c => c.Name))); // заголовок
        if (!ExternalClock)
            _timer.Start();
    }

    public void Stop()
    {
        if (!Running) return;
        Running = false;
        _timer.Stop();
        Stopped?.Invoke();
    }

    private double _t; // текущее время генерации, сек

    private void Emit(double t)
    {
        string line = string.Join(",",
            Channels.Select(c => c.Compute(t, _rnd).ToString("0.###", CultureInfo.InvariantCulture)));
        LineGenerated?.Invoke(line);
    }

    private void Tick()
    {
        if (ExternalClock) return; // такт приходит извне — из обработчика порта
        _t += 1.0 / 50;
        Emit(_t);
    }

    // Один отсчёт по внешнему такту (вызывается на каждую строку порта)
    public void ExternalTick()
    {
        if (!Running || !ExternalClock) return;
        _t += 1.0 / Math.Clamp(ExternalRate, 1, 1000);
        Emit(_t);
    }

    // Переключение на внешний такт (порт подключился) / обратно (порт закрылся)
    public void UseExternalClock()
    {
        ExternalClock = true;
        _timer.Stop();
    }

    public void UseInternalClock()
    {
        ExternalClock = false;
        ExternalRate = 50;
        if (Running)
            _timer.Start();
    }

    // Ретроспектива: count строк «из прошлого» с шагом 1/rate, время
    // от -count/rate до 0. Нужна, чтобы при совместной работе с портом
    // сигнал симулятора начинался с того же момента, что и сигнал порта,
    // а не с момента нажатия «Старт»
    public void GenerateHistory(int count, double rate)
    {
        double dt = 1.0 / Math.Clamp(rate, 1, 1000);
        var walks = Channels.Select(c => c.Offset).ToArray();
        for (int i = 0; i < count; i++)
        {
            double t = (i - count) * dt;
            string line = string.Join(",", Channels.Select((c, k) =>
                c.Compute(t, _rnd, ref walks[k]).ToString("0.###", CultureInfo.InvariantCulture)));
            LineGenerated?.Invoke(line);
        }
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
