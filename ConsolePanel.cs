using System.Text;

namespace SerialPlotter;

// Всегда видимая консоль порта: вкладка «Входящие» (сырые строки от
// источника, пишутся пакетами раз в 50 мс) и вкладка «Исходящие»
// (то, что отправили в порт — с меткой >> и меткой времени).
public sealed class ConsolePanel : Panel
{
    private const int MAX_BATCH = 300;      // строк за один сброс
    private const int MAX_LENGTH = 150_000; // символов в логе, дальше обрезаем

    private readonly TextBox _inLog;
    private readonly TextBox _outLog;
    private readonly Button _pauseBtn;
    private readonly TextBox _sendBox;
    private readonly Button _sendBtn;
    private readonly Queue<string> _queue = new();
    private bool _paused;

    // Отправка строки в порт; false — порт закрыт, отправить нельзя
    public Func<string, bool>? SendLine;

    public ConsolePanel()
    {
        Dock = DockStyle.Bottom;
        Height = 150;

        _inLog = MakeLog();
        _outLog = MakeLog();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var inTab = new TabPage("Входящие") { BackColor = Theme.Bg };
        inTab.Controls.Add(_inLog);
        var outTab = new TabPage("Исходящие") { BackColor = Theme.Bg };
        outTab.Controls.Add(_outLog);
        tabs.TabPages.Add(inTab);
        tabs.TabPages.Add(outTab);

        var header = new Panel { Dock = DockStyle.Top, Height = 32 };
        header.Controls.Add(new Label { Text = "Данные порта:", Left = 8, Top = 8, Width = 110 });

        _pauseBtn = new Button { Text = "Пауза", Left = 120, Top = 4, Width = 100, Height = 24 };
        _pauseBtn.Click += (_, _) =>
        {
            _paused = !_paused;
            _pauseBtn.Text = _paused ? "Продолжить" : "Пауза";
        };

        var clearBtn = new Button { Text = "Очистить", Left = 228, Top = 4, Width = 90, Height = 24 };
        clearBtn.Click += (_, _) => Clear();

        // Отправка данных в порт
        _sendBox = new TextBox { Left = 420, Top = 5, Width = 420 };
        _sendBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { SendCurrent(); e.SuppressKeyPress = true; }
        };
        _sendBtn = new Button { Text = "Отправить", Left = 846, Top = 4, Width = 90, Height = 24 };
        _sendBtn.Click += (_, _) => SendCurrent();
        SetSendEnabled(false);

        header.Controls.Add(_pauseBtn);
        header.Controls.Add(clearBtn);
        header.Controls.Add(_sendBtn);
        header.Controls.Add(_sendBox);

        Controls.Add(tabs);   // Fill — добавляем первым, докируется последним
        Controls.Add(header); // Top

        var flushTimer = new System.Windows.Forms.Timer { Interval = 50 };
        flushTimer.Tick += (_, _) => Flush();
        flushTimer.Start();
    }

    private static TextBox MakeLog() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Bg,
        ForeColor = Theme.Text,
    };

    public void Enqueue(string line)
    {
        lock (_queue)
        {
            _queue.Enqueue(line);
            if (_queue.Count > 5000)
                _queue.Clear(); // защита от переполнения при умершем UI
        }
    }

    public void Clear()
    {
        _inLog.Clear();
        _outLog.Clear();
    }

    // Доступность отправки: активна, только когда порт открыт
    public void SetSendEnabled(bool enabled)
    {
        _sendBox.Enabled = enabled;
        _sendBtn.Enabled = enabled;
        if (!enabled) _sendBox.Text = "";
    }

    private void SendCurrent()
    {
        string line = _sendBox.Text.Trim();
        if (line.Length == 0) return;
        if (SendLine?.Invoke(line) == false) return; // порт закрыт — строка остаётся
        _sendBox.Clear();
        Append(_outLog, ">> " + line);
    }

    private void Flush()
    {
        var sb = new StringBuilder();
        lock (_queue)
        {
            if (_paused)
            {
                _queue.Clear(); // на паузе строки просто пропускаем — это монитор, а не архив
                return;
            }
            int n = 0;
            while (_queue.Count > 0 && n++ < MAX_BATCH)
                sb.Append(_queue.Dequeue()).Append(Environment.NewLine);
        }

        if (sb.Length == 0) return;
        foreach (string raw in sb.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            Append(_inLog, raw);
    }

    // Строка с меткой времени в лог; начало обрезаем, не разрывая строку
    private static void Append(TextBox log, string text)
    {
        log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}");

        if (log.TextLength > MAX_LENGTH)
        {
            string t = log.Text;
            int cut = t.IndexOf('\n', t.Length - 100_000);
            log.Text = cut > 0 ? t[(cut + 1)..] : t[^100_000..];
        }

        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }
}
