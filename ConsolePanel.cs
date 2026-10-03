using System.Text;

namespace SerialPlotter;

// Всегда видимая консоль сырых строк от источника (порт или симулятор).
// Пишется пакетами раз в 50 мс, чтобы не захлёбываться на быстрых потоках.
public sealed class ConsolePanel : Panel
{
    private const int MAX_BATCH = 300;      // строк за один сброс
    private const int MAX_LENGTH = 150_000; // символов в логе, дальше обрезаем

    private readonly TextBox _log;
    private readonly Button _pauseBtn;
    private readonly Queue<string> _queue = new();
    private bool _paused;

    public ConsolePanel()
    {
        Dock = DockStyle.Bottom;
        Height = 150;

        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new Font("Consolas", 9f),
            BorderStyle = BorderStyle.None,
        };

        var header = new Panel { Dock = DockStyle.Top, Height = 32 };
        header.Controls.Add(new Label { Text = "Данные порта:", Left = 8, Top = 8, Width = 110 });

        _pauseBtn = new Button { Text = "Пауза", Left = 120, Top = 4, Width = 100, Height = 24 };
        _pauseBtn.Click += (_, _) =>
        {
            _paused = !_paused;
            _pauseBtn.Text = _paused ? "Продолжить" : "Пауза";
        };

        var clearBtn = new Button { Text = "Очистить", Left = 228, Top = 4, Width = 90, Height = 24 };
        clearBtn.Click += (_, _) => _log.Clear();

        header.Controls.Add(_pauseBtn);
        header.Controls.Add(clearBtn);

        Controls.Add(_log);   // Fill — добавляем первым, докируется последним
        Controls.Add(header); // Top

        var flushTimer = new System.Windows.Forms.Timer { Interval = 50 };
        flushTimer.Tick += (_, _) => Flush();
        flushTimer.Start();
    }

    public void Enqueue(string line)
    {
        lock (_queue)
        {
            _queue.Enqueue(line);
            if (_queue.Count > 5000)
                _queue.Clear(); // защита от переполнения при умершем UI
        }
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
            {
                sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ")
                  .Append(_queue.Dequeue()).Append(Environment.NewLine);
            }
        }

        if (sb.Length == 0) return;
        _log.AppendText(sb.ToString());

        // Обрезаем начало лога, не разрывая строку
        if (_log.TextLength > MAX_LENGTH)
        {
            string t = _log.Text;
            int cut = t.IndexOf('\n', t.Length - 100_000);
            _log.Text = cut > 0 ? t[(cut + 1)..] : t[^100_000..];
        }

        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
