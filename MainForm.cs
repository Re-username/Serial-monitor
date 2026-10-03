using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace SerialPlotter;

// Главное окно: тулбар с портом, панель каналов справа, консоль снизу,
// в центре — график (режим переключается кнопкой «Режим»).
internal sealed class MainForm : Form
{
    private const int MODE_TIME = 0, MODE_POLAR = 1, MODE_3D = 2;
    private static readonly int[] BAUDS = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
    private static readonly string[] MODE_NAMES = { "график по времени", "полярный режим", "3D-режим" };

    private int _mode = MODE_TIME;
    private string _status = "не подключено";

    // Поворот 3D-сцены
    private double _rotY = -0.7, _rotX = 0.5;
    private bool _dragging;
    private Point _lastMouse;
    private Point _mousePos = new(-1, -1); // курсор на графике, для линейки

    private readonly PlotPanel _plot = new() { Dock = DockStyle.Fill };
    private readonly ChannelsPanel _channels = new();
    private readonly ConsolePanel _console = new();
    private readonly ComboBox _portBox = new();
    private readonly ComboBox _baudBox = new();
    private readonly Button _connectBtn = new();
    private readonly Button _modeBtn = new();
    private readonly ComboBox _time3dBox = new();
    private readonly CheckBox _spinBox = new();
    private readonly Button _themeBtn = new();
    private readonly Button _pauseBtn = new();
    private readonly SerialLink _serial = new();
    private readonly Simulator _sim = new();
    private SimulatorForm? _simForm;

    public MainForm()
    {
        Text = "Serial Plotter";
        ClientSize = new Size(1240, 720);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        HandleCreated += (_, _) => Theme.ApplyTitleBar(this);

        // Вращение 3D мышью
        _plot.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) { _dragging = true; _lastMouse = e.Location; }
        };
        _plot.MouseMove += (_, e) =>
        {
            _mousePos = e.Location; // линейка следит за курсором
            if (!_dragging) return;
            _rotY += (e.X - _lastMouse.X) * 0.01;
            _rotX = Math.Clamp(_rotX + (e.Y - _lastMouse.Y) * 0.01, -1.5, 1.5);
            _lastMouse = e.Location;
        };
        _plot.MouseUp += (_, _) => _dragging = false;
        _plot.MouseLeave += (_, _) => _mousePos = new Point(-1, -1);
        _plot.Paint += OnPaint;

        // Экспорт: правый клик по графику
        var menu = new ContextMenuStrip();
        menu.Items.Add("Сохранить снимок PNG", null, (_, _) => SavePng());
        menu.Items.Add("Сохранить снимок SVG", null, (_, _) => SaveSvg());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Экспорт данных в CSV", null, (_, _) => ExportCsv());
        _plot.ContextMenuStrip = menu;

        // Порядок добавления важен: Fill добавляем первым — он докируется последним.
        // Итог: тулбар сверху на всю ширину, консоль снизу на всю ширину,
        // панель каналов справа между ними, график занимает остаток.
        Controls.Add(_plot);
        Controls.Add(_channels);
        Controls.Add(_console);
        BuildToolbar();

        // Таймер перерисовки (~30 FPS), в 3D — медленный автоповорот
        var drawTimer = new System.Windows.Forms.Timer { Interval = 33 };
        drawTimer.Tick += (_, _) =>
        {
            if (_mode == MODE_3D && !_dragging && _spinBox.Checked) _rotY += 0.004;
            _plot.Invalidate();
        };
        drawTimer.Start();

        _serial.LineReceived += DataHub.PushLine;
        _serial.Closed += OnSerialClosed;
        _serial.Failed += msg =>
        {
            _status = "ошибка порта";
            UpdateTitle();
            MessageBox.Show(this, $"Порт закрылся с ошибкой:\n{msg}", "Ошибка порта",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        _sim.LineGenerated += DataHub.PushLine;
        _sim.Started += OnSimStarted;
        _sim.Stopped += OnSimStopped;

        DataHub.RawLine += _console.Enqueue;
        DataHub.ChannelsChanged += UpdateTitle;

        FormClosed += (_, _) => { _serial.Dispose(); _sim.Dispose(); };

        Theme.Apply(this);
        UpdateTitle();
    }

    // --- Тулбар ---

    private void BuildToolbar()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 46 };

        panel.Controls.Add(new Label { Text = "Порт:", Left = 12, Top = 14, Width = 40 });

        _portBox.Left = 55; _portBox.Top = 11; _portBox.Width = 90;
        _portBox.DropDownStyle = ComboBoxStyle.DropDownList;

        panel.Controls.Add(new Label { Text = "Бодрейт:", Left = 155, Top = 14, Width = 60 });

        _baudBox.Left = 218; _baudBox.Top = 11; _baudBox.Width = 90;
        foreach (int b in BAUDS) _baudBox.Items.Add(b);
        _baudBox.Text = "115200";

        var refreshBtn = new Button { Text = "Обновить", Left = 316, Top = 9, Width = 80 };
        refreshBtn.Click += (_, _) => RefreshPorts();

        _connectBtn.Text = "Подключить";
        _connectBtn.Left = 404; _connectBtn.Top = 9; _connectBtn.Width = 110;
        _connectBtn.Click += (_, _) => ToggleConnect();

        // Режим доступен всегда: без данных отрисовщики показывают подсказку
        _modeBtn.Text = "Режим: Время";
        _modeBtn.Left = 524; _modeBtn.Top = 9; _modeBtn.Width = 140;
        _modeBtn.Click += (_, _) =>
        {
            _mode = (_mode + 1) % 3;
            _modeBtn.Text = $"Режим: {_mode switch { MODE_TIME => "Время", MODE_POLAR => "Полярный", _ => "3D" }}";
            _time3dBox.Visible = _spinBox.Visible = _mode == MODE_3D;
            UpdateTitle();
        };

        var simBtn = new Button { Text = "Симулятор", Left = 674, Top = 9, Width = 100 };
        simBtn.Click += (_, _) => ShowSimulator();

        _pauseBtn.Text = "Пауза";
        _pauseBtn.Left = 784; _pauseBtn.Top = 9; _pauseBtn.Width = 90;
        _pauseBtn.Click += (_, _) =>
        {
            DataHub.Paused = !DataHub.Paused;
            _pauseBtn.Text = DataHub.Paused ? "Продолжить" : "Пауза";
            UpdateTitle();
        };

        // Ось времени в 3D: «без времени» или t на оси X/Y/Z;
        // две оставшиеся оси занимают первые два видимых канала
        _time3dBox.Left = 1002; _time3dBox.Top = 11; _time3dBox.Width = 118;
        _time3dBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _time3dBox.Items.AddRange(new object[] { "3D: без времени", "3D: t → X", "3D: t → Y", "3D: t → Z" });
        _time3dBox.SelectedIndex = 0;

        _spinBox.Text = "Вращение";
        _spinBox.Left = 1126; _spinBox.Top = 14; _spinBox.Width = 80;
        _spinBox.Checked = true;

        // Настройки 3D видны только в 3D-режиме
        _time3dBox.Visible = _spinBox.Visible = false;

        _themeBtn.Text = "Тема: Киберпанк";
        _themeBtn.Left = 882; _themeBtn.Top = 9; _themeBtn.Width = 112;
        _themeBtn.Click += (_, _) =>
        {
            Theme.Kind = Theme.Kind switch
            {
                ThemeKind.Dark => ThemeKind.Light,
                ThemeKind.Light => ThemeKind.Cyberpunk,
                _ => ThemeKind.Dark,
            };
            _themeBtn.Text = $"Тема: {Theme.Kind switch
            {
                ThemeKind.Dark => "Тёмная",
                ThemeKind.Light => "Светлая",
                _ => "Киберпанк",
            }}";
            Theme.Apply(this);
            Theme.ApplyTitleBar(this);
        };

        panel.Controls.AddRange(new Control[]
            { _portBox, _baudBox, refreshBtn, _connectBtn, _modeBtn, simBtn, _pauseBtn, _themeBtn, _time3dBox, _spinBox });
        Controls.Add(panel);

        RefreshPorts();
    }

    // --- Порт ---

    private void RefreshPorts()
    {
        string? prev = (string?)_portBox.SelectedItem;
        // GetPortNames() может подвисать на десятки секунд из-за кривых
        // Bluetooth/COM-драйверов — сканируем в фоне, чтобы не блокировать запуск
        Task.Run(SerialPort.GetPortNames).ContinueWith(t =>
        {
            if (t.IsFaulted || IsDisposed) return;
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                _portBox.Items.Clear();
                _portBox.Items.AddRange(t.Result);
                if (prev != null && _portBox.Items.Contains(prev)) _portBox.SelectedItem = prev;
                else if (_portBox.Items.Count > 0) _portBox.SelectedIndex = 0;
            });
        });
    }

    private void ToggleConnect()
    {
        if (_serial.IsOpen)
        {
            _serial.Close(); // дернёт OnSerialClosed
            _status = "отключено";
            UpdateTitle();
            return;
        }

        if (_portBox.SelectedItem is not string portName)
        {
            MessageBox.Show(this, "Нет доступных COM-портов.\nПодключи ESP32 и нажми «Обновить».",
                            "Ошибка порта", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(_baudBox.Text.Trim(), out int baud) || baud <= 0)
        {
            MessageBox.Show(this, "Неверное значение бодрейта.", "Ошибка",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _serial.Open(portName, baud);
            DataHub.Reset(); // старые каналы от прошлого источника не нужны
            _status = $"{portName} @ {baud}";
            _connectBtn.Text = "Отключить";
        }
        catch (Exception ex)
        {
            _status = "не подключено";
            MessageBox.Show(this, $"Не удалось открыть {portName}:\n{ex.Message}\n\n" +
                            "Закрой монитор PlatformIO и плоттеры, проверь порт и бодрейт.",
                            "Ошибка порта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        UpdateTitle();
    }

    private void OnSerialClosed() => _connectBtn.Text = "Подключить";

    // --- Симулятор ---

    private void ShowSimulator()
    {
        if (_simForm is { IsDisposed: false })
        {
            _simForm.Activate();
            return;
        }
        _simForm = new SimulatorForm(_sim);
        _simForm.Show(this); // немодально — график и консоль остаются видимы
    }

    private void OnSimStarted()
    {
        if (_serial.IsOpen)
            _serial.Close();
        _connectBtn.Text = "Подключить";
        _connectBtn.Enabled = false; // один активный источник: либо порт, либо симулятор
        DataHub.Reset();             // старые каналы не смешиваем с виртуальными
        _status = $"симулятор: {_sim.Channels.Count} канал(ов)";
        UpdateTitle();
    }

    private void OnSimStopped()
    {
        _connectBtn.Enabled = true;
        if (!_serial.IsOpen)
            _status = "не подключено";
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = $"Serial Plotter — {_status} — {MODE_NAMES[_mode]}" + (DataHub.Paused ? " — ПАУЗА" : "");

    // --- Отрисовка ---

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        // Рисуем в собственный Bitmap и копируем на экран: встроенный
        // BufferedGraphics (DoubleBuffered) падает с "Parameter is not valid"
        // в Graphics.GetHdc — известная проблема WinForms.
        try
        {
            using var bmp = new Bitmap(Math.Max(1, _plot.ClientSize.Width), Math.Max(1, _plot.ClientSize.Height));
            using (var bg = Graphics.FromImage(bmp))
            {
                bg.Clear(Theme.Bg); // фон под всем кадром, включая поля вокруг графика
                bg.SmoothingMode = SmoothingMode.AntiAlias;
                using var gg = new SvgNet.GdiGraphics(bg);
                RenderPlot(gg);
            }
            e.Graphics.DrawImage(bmp, 0, 0);
        }
        catch
        {
            // один испорченный кадр не должен ронять приложение-монитор
        }
    }

    // Вся отрисовка графика; используется и для показа на экране, и для экспорта.
    // IGraphics — общий интерфейс экранного Graphics и векторного SvgGraphics.
    // forExport: снимок без линейки курсора.
    private void RenderPlot(SvgNet.Interfaces.IGraphics g, bool forExport = false)
    {
        var rect = new Rectangle(40, 40, _plot.ClientSize.Width - 60, _plot.ClientSize.Height - 80);
        var ctx = new PlotContext
        {
            Visible = DataHub.Channels.Where(c => c.Visible).ToList(),
            XIndex = _channels.XIndex,
            RotX = _rotX,
            RotY = _rotY,
            Time3DAxis = _time3dBox.SelectedIndex,
            MouseX = forExport ? -1 : _mousePos.X,
            MouseY = forExport ? -1 : _mousePos.Y,
        };

        switch (_mode)
        {
            case MODE_POLAR: PolarPlot.Draw(g, rect, ctx); break;
            case MODE_3D: Plot3D.Draw(g, rect, ctx); break;
            default: TimePlot.Draw(g, rect, ctx); break;
        }

        using var font = new Font("Segoe UI", 10f);
        using var textBrush = new SolidBrush(Theme.Text);

        // Легенда — под графиком, чтобы не перекрывать линии:
        // цветной квадрат, имя и текущее значение каждого видимого канала
        if (_mode == MODE_TIME)
        {
            int lx = rect.Left;
            foreach (var ch in ctx.YChannels)
            {
                using var dotBrush = new SolidBrush(ch.Color);
                g.FillRectangle(dotBrush, lx, rect.Bottom + 24, 10, 10);
                string label = $"{ch.Name} = {ch.Last:0.##}";
                using var legendFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                using var legendBrush = new SolidBrush(ch.Color);
                g.DrawString(label, legendFont, legendBrush, lx + 14, rect.Bottom + 20);
                lx += (int)PlotUtil.Measure(g, label, legendFont).Width + 34;
            }
        }

        g.DrawString($"{_status}   каналов: {DataHub.Channels.Count}   " +
                     $"точек: {DataHub.Channels.FirstOrDefault()?.Data.Count ?? 0}",
                     font, textBrush, rect.Left, rect.Bottom + 42);
    }

    // --- Экспорт ---

    private void SavePng()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG-изображение|*.png",
            FileName = "serialplot.png",
            Title = "Сохранить снимок графика",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var bmp = new Bitmap(Math.Max(1, _plot.ClientSize.Width), Math.Max(1, _plot.ClientSize.Height));
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var gg = new SvgNet.GdiGraphics(g);
            RenderPlot(gg, forExport: true);
        }
        bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
        _status = $"снимок сохранён: {Path.GetFileName(dlg.FileName)}";
        UpdateTitle();
    }

    private void SaveSvg()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "SVG-изображение|*.svg",
            FileName = "serialplot.svg",
            Title = "Сохранить снимок графика",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // SvgGraphics — Graphics-совместимый векторный холст,
        // тот же код отрисовки рисует сразу в SVG
        using var svg = new SvgNet.SvgGraphics();
        RenderPlot(svg, forExport: true);
        File.WriteAllText(dlg.FileName,
            svg.WriteSVGString(_plot.ClientSize.Width, _plot.ClientSize.Height));        _status = $"снимок сохранён: {Path.GetFileName(dlg.FileName)}";
        UpdateTitle();
    }

    private void ExportCsv()
    {
        if (DataHub.Channels.Count == 0 || DataHub.Channels.All(c => c.Data.Count == 0))
        {
            MessageBox.Show(this, "Нет данных для экспорта.", "Экспорт CSV",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (разделитель — запятая)|*.csv",
            FileName = "serialplot.csv",
            Title = "Экспорт данных каналов",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var arrays = DataHub.Channels.Select(c => c.Data.ToArray()).ToArray();
        int n = arrays.Max(a => a.Length);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", DataHub.Channels.Select(c => CsvCell(c.Name))));
        for (int i = 0; i < n; i++)
        {
            int row = i;
            sb.AppendLine(string.Join(",",
                arrays.Select(a => row < a.Length ? a[row].ToString("G9", CultureInfo.InvariantCulture) : "")));
        }

        File.WriteAllText(dlg.FileName, sb.ToString());
        _status = $"CSV сохранён: {Path.GetFileName(dlg.FileName)}";
        UpdateTitle();
    }

    private static string CsvCell(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}

// Панель графика. Двойной буфер делаем сами в MainForm.OnPaint
// (Bitmap + DrawImage): встроенный BufferedGraphics периодически падает
// с "Parameter is not valid" в Graphics.GetHdc — известная проблема WinForms.
// UserPaint + AllPaintingInWmPaint + Opaque: система не стирает фон сама
// (иначе мерцает), весь кадр рисуем мы одним Bitmap.
internal sealed class PlotPanel : Panel
{
    public PlotPanel() =>
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.Opaque, true);
}
