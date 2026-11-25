using System.IO.Ports;
using System.Text;

namespace SerialPlotter.Blazor.Core;

public sealed class SerialLink : IDisposable
{
    private const int OpenTimeoutMs = 4000;

    private SerialPort? _port;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;

    public event Action<string>? LineReceived;
    public event Action? Closed;
    public event Action<string>? Failed;

    public bool IsOpen => _port?.IsOpen == true;

    public async Task<string?> OpenAsync(string portName, int baud, bool dtrEnable, Encoding encoding)
    {
        Close();

        Task<(SerialPort? Port, Exception? Error)> openTask = Task.Run(() =>
        {
            try
            {
                var port = new SerialPort(portName, baud)
                {
                    ReadTimeout = 100,
                    NewLine = "\n",
                    DtrEnable = dtrEnable,
                    Encoding = encoding,
                };
                try
                {
                    port.Open();
                }
                catch
                {
                    try { port.Dispose(); } catch { }
                    throw;
                }
                return ((SerialPort?)port, (Exception?)null);
            }
            catch (Exception ex)
            {
                return ((SerialPort?)null, ex);
            }
        });

        if (await Task.WhenAny(openTask, Task.Delay(OpenTimeoutMs)) != openTask)
        {
            return $"порт не отвечает {OpenTimeoutMs / 1000} с — драйвер завис. " +
                   "Проверь кабель/переходник и перезапусти приложение.";
        }

        var (opened, error) = await openTask;
        if (error != null)
            return error.Message;

        _port = opened;
        _encoding = encoding;
        _decoder = encoding.GetDecoder();
        _rxTail = "";
        _readCancellation = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoop(_readCancellation.Token));
        return null;
    }

    private readonly byte[] _rxBuffer = new byte[4096];
    private readonly char[] _rxChars = new char[8192];
    private string _rxTail = "";
    private Encoding _encoding = Encoding.ASCII;
    private Decoder _decoder = Encoding.ASCII.GetDecoder();

    private async Task ReadLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var port = _port;
                if (port is not { IsOpen: true })
                    break;

                if (port.BytesToRead > 0)
                    ReadAvailable(port);
                else
                    await Task.Delay(10, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Close();
            Failed?.Invoke(ex.Message);
            return;
        }

        // Порт закрылся сам (кабель/драйвер), а не через Close(): UI должен узнать.
        if (!cancellationToken.IsCancellationRequested && _port != null)
            Close();
    }

    private void ReadAvailable(SerialPort port)
    {
        try
        {
            int count = port.Read(_rxBuffer, 0, Math.Min(port.BytesToRead, _rxBuffer.Length));
            if (count <= 0)
                return;

            int chars = _decoder.GetChars(_rxBuffer, 0, count, _rxChars, 0);
            string text = _rxTail + new string(_rxChars, 0, chars);

            int start = 0, newline;
            while ((newline = text.IndexOf('\n', start)) >= 0)
            {
                string line = text[start..newline].Trim();
                if (line.Length > 0)
                    LineReceived?.Invoke(line);
                start = newline + 1;
            }
            _rxTail = text[start..];
            if (_rxTail.Length > 10_000)
                _rxTail = "";
        }
        catch (TimeoutException) { }
    }

    public void Close()
    {
        var cancellation = _readCancellation;
        _readCancellation = null;
        cancellation?.Cancel();

        var port = _port;
        _port = null;
        if (port != null)
        {
            Task.Run(() =>
            {
                try { port.Close(); } catch { }
                try { port.Dispose(); } catch { }
            });
            Closed?.Invoke();
        }
    }

    public void Send(string line)
    {
        if (_port is not { IsOpen: true })
            return;
        try
        {
            _port.Write(line + "\n");
        }
        catch (Exception ex)
        {
            Close();
            Failed?.Invoke(ex.Message);
        }
    }

    public void DiscardBuffers()
    {
        _rxTail = "";
        _decoder.Reset();
        if (_port is not { IsOpen: true })
            return;
        try
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }
        catch { }
    }

    public void Dispose()
    {
        Close();
        _readCancellation?.Dispose();
    }
}
