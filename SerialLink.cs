using System.IO.Ports;
using System.Text;

namespace SerialPlotter;

// Чтение строк из COM-порта. Строки уходят наружу через событие LineReceived.
// Open/Close выполняются в фоне с таймаутом: SerialPort.Open() и Close()
// могут навечно зависнуть на кривых драйверах (Bluetooth, выдернутый кабель) —
// интерфейс при этом остаётся живым.
public sealed class SerialLink : IDisposable
{
    private const int OpenTimeoutMs = 4000;

    private SerialPort? _port;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };

    public event Action<string>? LineReceived;
    public event Action? Closed;              // порт закрыт (в т.ч. из-за ошибки)
    public event Action<string>? Failed;      // порт отвалился с ошибкой (выдернули кабель и т.п.)

    public bool IsOpen => _port?.IsOpen == true;

    public SerialLink() => _timer.Tick += (_, _) => Read();

    // Открывает порт в фоновом потоке. Возвращает null при успехе
    // или текст ошибки (включая таймаут зависшего драйвера).
    // dtrEnable: true — выставить DTR (многие платы перезагружаются от этого),
    // false — не трогать плату при подключении.
    // encoding: как декодировать байты в строки (кириллица и пр. не-ASCII)
    public async Task<string?> OpenAsync(string portName, int baud, bool dtrEnable, System.Text.Encoding encoding)
    {
        // Создание и Open — в фоне: зависший драйвер не должен замораживать UI.
        // Таймер и поле назначаются после await в потоке вызова: WinForms Timer
        // привязан к message loop потока, который его стартует — из потока пула
        // он бы никогда не «тикнул», и данные не пошли бы.
        Task<(SerialPort? Port, Exception? Error)> openTask = Task.Run(() =>
        {
            try
            {
                // Кодировка: устройства шлют UTF-8/CP1251/..., по умолчанию
                // SerialPort читает ASCII и заменяет не-ASCII на '?'
                var port = new SerialPort(portName, baud)
                {
                    ReadTimeout = 100, NewLine = "\n",
                    DtrEnable = dtrEnable,
                    Encoding = encoding,
                };
                try { port.Open(); }
                catch
                {
                    try { port.Dispose(); } catch { /* порт уже мёртв */ }
                    throw;
                }
                return ((SerialPort?)port, (Exception?)null);
            }
            catch (Exception ex) { return ((SerialPort?)null, ex); }
        });

        if (await Task.WhenAny(openTask, Task.Delay(OpenTimeoutMs)) != openTask)
        {
            // Open завис внутри драйвера. Dispose тоже завис бы — объект
            // приходится бросить: поток утечёт, но приложение продолжит работу
            return $"порт не отвечает {OpenTimeoutMs / 1000} с — драйвер завис. " +
                   "Проверь кабель/переходник и перезапусти приложение.";
        }

        var (opened, error) = await openTask;
        if (error != null) return error.Message;

        _port = opened;
        // Декодируем сами (Read читает сырые байты) — той же кодировкой,
        // что и SerialPort для Write
        _encoding = encoding;
        _decoder = encoding.GetDecoder();
        _timer.Start();
        return null;
    }

    // Чтение — на UI-потоке (таймер). Непрерывный поток (в т.ч. мусор
    // при неверном бодрейте) держит BytesToRead > 0 вечно, поэтому ReadLine
    // не подходит: он блокирует поток до перевода строки. Читаем сырые байты
    // (забирается всё, что накопилось, без ожидания) и режем на строки сами.
    private const int RxBufSize = 4096;

    private readonly byte[] _rxBuf = new byte[RxBufSize];
    private readonly char[] _rxChars = new char[RxBufSize * 2]; // UTF-16: до 2 символов на байт... хватит с запасом
    private string _rxTail = ""; // остаток после последнего перевода строки

    // Декодер выбранной кодировки: хранит состояние между чтениями, чтобы
    // мультибайтовый символ (UTF-8/UTF-16), разорванный между пачками байт,
    // склеивался, а не превращался в кракозябру
    private Encoding _encoding = Encoding.ASCII;
    private Decoder _decoder = Encoding.ASCII.GetDecoder();

    private void Read()
    {
        try
        {
            var port = _port;
            if (port is not { IsOpen: true } || port.BytesToRead == 0) return;

            int n = port.Read(_rxBuf, 0, Math.Min(port.BytesToRead, RxBufSize));
            if (n <= 0) return;

            int chars = _decoder.GetChars(_rxBuf, 0, n, _rxChars, 0);
            string chunk = new(_rxChars, 0, chars);
            string text = _rxTail + chunk;

            int start = 0, nl;
            while ((nl = text.IndexOf('\n', start)) >= 0)
            {
                string? line = text[start..nl].Trim();
                if (line.Length > 0)
                    LineReceived?.Invoke(line);
                start = nl + 1;
            }
            _rxTail = text[start..];
            // строка без '\n' (мусор) не должна копиться бесконечно
            if (_rxTail.Length > 10_000) _rxTail = "";
        }
        catch (TimeoutException) { /* байты ушли до чтения — не страшно */ }
        catch (Exception ex)
        {
            Close();
            Failed?.Invoke(ex.Message);
        }
    }

    public void Close()
    {
        _timer.Stop();
        var port = _port;
        _port = null;
        if (port != null)
            // Close/Dispose тоже умеют зависать (выдернутый кабель) — в фоне
            Task.Run(() =>
            {
                try { port.Close(); } catch { /* порт уже мёртв */ }
                try { port.Dispose(); } catch { }
            });
        Closed?.Invoke();
    }

    // Отправка строки в порт (с переводом строки, как у ReadLine)
    public void Send(string line)
    {
        if (_port is not { IsOpen: true }) return;
        try { _port.Write(line + "\n"); }
        catch (Exception ex)
        {
            Close();
            Failed?.Invoke(ex.Message);
        }
    }

    // Сброс входных/выходных буферов порта
    public void DiscardBuffers()
    {
        _rxTail = "";
        _decoder.Reset(); // выкинуть недокодированный хвост
        if (_port is not { IsOpen: true }) return;
        try { _port.DiscardInBuffer(); _port.DiscardOutBuffer(); } catch { }
    }

    public void Dispose()
    {
        Close();
        _timer.Dispose();
    }
}
