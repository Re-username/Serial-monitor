using System.IO.Ports;

namespace SerialPlotter;

// Чтение строк из COM-порта. Строки уходят наружу через событие LineReceived.
public sealed class SerialLink : IDisposable
{
    private SerialPort? _port;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };

    public event Action<string>? LineReceived;
    public event Action? Closed;              // порт закрыт (в т.ч. из-за ошибки)
    public event Action<string>? Failed;      // порт отвалился с ошибкой (выдернули кабель и т.п.)

    public bool IsOpen => _port?.IsOpen == true;

    public SerialLink() => _timer.Tick += (_, _) => Read();

    public void Open(string portName, int baud)
    {
        _port = new SerialPort(portName, baud) { ReadTimeout = 500, NewLine = "\n" };
        _port.Open();
        _timer.Start();
    }

    private void Read()
    {
        try
        {
            while (_port is { IsOpen: true } && _port.BytesToRead > 0)
            {
                string? line = _port.ReadLine()?.Trim();
                if (!string.IsNullOrEmpty(line))
                    LineReceived?.Invoke(line);
            }
        }
        catch (TimeoutException) { /* нет данных — не страшно */ }
        catch (Exception ex)
        {
            Close();
            Failed?.Invoke(ex.Message);
        }
    }

    public void Close()
    {
        _timer.Stop();
        if (_port != null)
        {
            try { _port.Close(); } catch { /* порт уже мёртв */ }
            _port.Dispose();
            _port = null;
        }
        Closed?.Invoke();
    }

    public void Dispose()
    {
        Close();
        _timer.Dispose();
    }
}
