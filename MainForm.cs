using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace SerialPlotter;

// Главное окно: тулбар с портом, панель каналов справа, консоль снизу,
// в центре — график (режим переключается кнопкой «Режим»: время/полярный/3D/FFT).
internal sealed class MainForm : Form
{
    private const int MODE_TIME = 0, MODE_POLAR = 1, MODE_3D = 2, MODE_FFT = 3;
    private static readonly int[] BAUDS = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
    private static readonly string[] MODE_NAMES_RU = { "график по времени", "полярный режим", "3D-режим", "FFT-спектр" };
    private static readonly string[] MODE_NAMES_EN = { "time plot", "polar mode", "3D mode", "FFT spectrum" };

    private int _mode = MODE_TIME;
    private string _status = Lang.T("не подключено", "not connected");

    // Поворот 3D-сцены
    private double _rotY = -0.7, _rotX = 0.5;
    private bool _dragging;
    private Point _lastMouse;
    private Point _mousePos = new(-1, -1); // курсор на графике, для линейки

    // Выделение области (зум) и панорамирование в режиме «Время»
    private bool _selecting, _panning;
    private bool _dragMoved; // левая кнопка реально протащили (иначе это клик)
    private Point _dragStart;
    private Rectangle _selRect;
    private bool _pendingViewReset; // сбросить зум после экспорта

    private readonly PlotPanel _plot = new() { Dock = DockStyle.Fill };
    private readonly ChannelsPanel _channels = new();
    private readonly ConsolePanel _console = new();
    private readonly Button _portBtn = new();     // «Порт ▾» — выбор порта
    private readonly ContextMenuStrip _portMenu = new();
    private readonly ComboBox _baudBox = new();   // бодрейт — можно ввести вручную
    private readonly Label _baudLabel = new();
    private readonly Button _connectBtn = new();
    private readonly Button _modeBtn = new();     // «Режим ▾» — выбор режима отрисовки
    private readonly ContextMenuStrip _modeMenu = new();
    private readonly Button _toolsBtn = new();    // «Инструменты ▾» — диапазоны, сброс, тема
    private readonly ContextMenuStrip _toolsMenu = new();
    private string? _port;                          // выбранный порт; null — нет в списке
    private int _baud = 115200;                     // выбранный бодрейт
    private string[] _portNames = Array.Empty<string>();
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 500 };    private readonly ComboBox _time3dBox = new();
    private readonly CheckBox _spinBox = new();
    private readonly Label _fftRateLabel = new();
    private readonly NumericUpDown _fftRateBox = new();
    private readonly CheckBox _fftLogBox = new();
    private readonly Button _pauseBtn = new();
    private readonly SerialLink _serial = new();
    private readonly Simulator _sim = new();
    private SimulatorForm? _simForm;

    public MainForm()
    {
        Text = "Serial Plotter";
        ClientSize = new Size(1240, 720);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        KeyPreview = true; // F1 — справка, даже когда фокус в полях ввода
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F1) { HelpForm.Toggle(this); e.Handled = true; }
        };
        HandleCreated += (_, _) => Theme.ApplyTitleBar(this);

        // Настройки прошлого запуска: тема, язык, частота дискретизации, каналы
        Settings.Load();
        Theme.Kind = (ThemeKind)Settings.Current.Theme;
        Lang.English = Settings.Current.Language == 1;
        if (Settings.Current.Baud > 0) _baud = Settings.Current.Baud;
        PlotSettings.SampleRate = Math.Clamp(Settings.Current.SampleRate, 1, 1_000_000);
        PlotSettings.FftLog = Settings.Current.FftLog;
        PlotSettings.GlobalSmooth = (SmoothKind)Settings.Current.GlobalSmooth;
        PlotSettings.GlobalSmoothWindow = Math.Clamp(Settings.Current.GlobalSmoothWindow, 3, 501);
        PlotSettings.PolarTrail = Math.Clamp(Settings.Current.PolarTrail, 10, 10000);
        DataHub.SeedChannels = Settings.Current.Channels.Select(c => new DataHub.SavedChannel
        {
            Name = c.Name,
            Color = c.ColorArgb != 0 ? Color.FromArgb(c.ColorArgb) : Color.Empty,
            Visible = c.Visible,
            LineWidth = c.LineWidth,
            LineStyle = (LineKind)c.LineStyle,
            Smooth = (SmoothKind)c.Smooth,
            SmoothWindow = c.SmoothWindow,
        }).ToList();

        // Мышь на графике: линейка, выделение (зум), панорама, маркеры, 3D-вращение
        _plot.MouseDown += (_, e) =>
        {
            if (_mode == MODE_3D)
            {
                if (e.Button == MouseButtons.Left) { _dragging = true; _lastMouse = e.Location; }
                return;
            }
            if (_mode != MODE_TIME) return;

            if (e.Button == MouseButtons.Middle)
            {
                // Панорама средней кнопкой — всегда
                InitViewFromCurrent();
                _panning = true;
                _lastMouse = e.Location;
            }
            else if (e.Button == MouseButtons.Left)
            {
                // Левая кнопка: клик — маркер, протяжка — рамка зума
                // (или панорама, если вид уже зумлен). Различим по перемещению.
                _selecting = true;
                _dragStart = e.Location;
                _selRect = Rectangle.Empty;
                _dragMoved = false;
                if (PlotSettings.ViewXMin != null)
                {
                    InitViewFromCurrent();
                    _panning = true;
                }
            }
        };
        _plot.MouseMove += (_, e) =>
        {
            _mousePos = e.Location; // линейка следит за курсором
            if (_selecting && !_dragMoved &&
                (Math.Abs(e.X - _dragStart.X) > 4 || Math.Abs(e.Y - _dragStart.Y) > 4))
                _dragMoved = true; // это протяжка, а не клик

            if (_dragging)
            {
                _rotY += (e.X - _lastMouse.X) * 0.01;
                _rotX = Math.Clamp(_rotX + (e.Y - _lastMouse.Y) * 0.01, -1.5, 1.5);
                _lastMouse = e.Location;
            }
            else if (_panning)
            {
                PanBy(e.Location);
                _lastMouse = e.Location;
            }
            else if (_selecting && _dragMoved)
            {
                _selRect = Rectangle.FromLTRB(
                    Math.Min(_dragStart.X, e.X), Math.Min(_dragStart.Y, e.Y),
                    Math.Max(_dragStart.X, e.X), Math.Max(_dragStart.Y, e.Y));
            }
        };
        _plot.MouseUp += (_, e) =>
        {
            _dragging = _panning = false;
            if (!_selecting) return;
            _selecting = false;

            if (!_dragMoved)
            {
                PlaceMarker(e.X); // просто клик — ставим линейку A, потом B
            }
            else if (_selRect.Width > 8 && _selRect.Height > 8)
            {
                ZoomTo(_selRect);
            }
            _selRect = Rectangle.Empty;
        };
        _plot.MouseLeave += (_, _) => _mousePos = new Point(-1, -1);
        _plot.DoubleClick += (_, _) => PlotSettings.ResetView();
        _plot.Paint += OnPaint;

        // Экспорт: правый клик по графику
        var menu = new ContextMenuStrip();
        menu.Items.Add(Lang.T("Сохранить снимок PNG", "Save PNG snapshot"), null, (_, _) => SavePng());
        menu.Items.Add(Lang.T("Сохранить снимок SVG", "Save SVG snapshot"), null, (_, _) => SaveSvg());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Lang.T("Экспорт данных в CSV", "Export data to CSV"), null, (_, _) => ExportCsv());
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
            _status = Lang.T("ошибка порта", "port error");
            _console.SetSendEnabled(false);
            UpdateTitle();
            MessageBox.Show(this, $"Порт закрылся с ошибкой:\n{msg}", Lang.T("Ошибка порта", "Port error"),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        _console.SendLine = line =>
        {
            if (!_serial.IsOpen) return false;
            _serial.Send(line);
            return true;
        };

        _sim.LineGenerated += DataHub.PushLine;
        _sim.Started += OnSimStarted;
        _sim.Stopped += OnSimStopped;

        DataHub.RawLine += _console.Enqueue;
        DataHub.ChannelsChanged += OnChannelsChanged;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveSettings(); };

        FormClosed += (_, _) => { SaveSettings(); _serial.Dispose(); _sim.Dispose(); };

        Theme.Apply(this);
        UpdateTitle();
    }

    // --- Тулбар ---

    private void BuildToolbar()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 80 }; // две строки

        // Первая строка — подключение, режим, инструменты (компактно, с выпадающими меню)
        _portBtn.Left = 12; _portBtn.Top = 9; _portBtn.Width = 140;
        _portMenu.Opening += (_, _) => BuildPortMenu();
        AttachDropdown(_portBtn, _portMenu);
        UpdatePortBtn();

        panel.Controls.Add(_baudLabel);
        _baudLabel.Text = Lang.T("Бодрейт:", "Baud rate:");
        _baudLabel.Left = 160; _baudLabel.Top = 14; _baudLabel.Width = 60;
        _baudBox.Left = 224; _baudBox.Top = 11; _baudBox.Width = 90;
        foreach (int b in BAUDS) _baudBox.Items.Add(b);
        _baudBox.Text = _baud.ToString();

        _connectBtn.Text = Lang.T("Подключить", "Connect");
        _connectBtn.Left = 322; _connectBtn.Top = 9; _connectBtn.Width = 130;
        _connectBtn.Click += (_, _) => ToggleConnect();

        _modeBtn.Text = $"{Lang.T("Режим", "Mode")}: {Lang.T("Время", "Time")}";
        _modeBtn.Left = 460; _modeBtn.Top = 9; _modeBtn.Width = 140;
        _modeMenu.Opening += (_, _) => BuildModeMenu();
        AttachDropdown(_modeBtn, _modeMenu);

        var simBtn = new Button { Text = Lang.T("Симулятор", "Simulator"), Left = 608, Top = 9, Width = 100 };
        simBtn.Click += (_, _) => ShowSimulator();

        _pauseBtn.Text = Lang.T("Пауза", "Pause");
        _pauseBtn.Left = 716; _pauseBtn.Top = 9; _pauseBtn.Width = 90;
        _pauseBtn.Click += (_, _) =>
        {
            DataHub.Paused = !DataHub.Paused;
            _pauseBtn.Text = DataHub.Paused ? Lang.T("Продолжить", "Resume") : Lang.T("Пауза", "Pause");
            UpdateTitle();
        };

        _toolsBtn.Text = Lang.T("Инструменты ▾", "Tools ▾");
        _toolsBtn.Left = 814; _toolsBtn.Top = 9; _toolsBtn.Width = 130;
        _toolsMenu.Opening += (_, _) => BuildToolsMenu();
        AttachDropdown(_toolsBtn, _toolsMenu);

        // Вторая строка — настройки активного режима (3D/FFT), видны только в своих режимах
        // Ось времени в 3D: «без времени» или t на оси X/Y/Z;
        // две оставшиеся оси занимают первые два видимых канала
        _time3dBox.Left = 12; _time3dBox.Top = 45; _time3dBox.Width = 118;
        _time3dBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _time3dBox.Items.AddRange(new object[] { "3D: без времени", "3D: t → X", "3D: t → Y", "3D: t → Z" });
        _time3dBox.SelectedIndex = 0;

        _spinBox.Text = Lang.T("Вращение", "Spin");
        _spinBox.Left = 140; _spinBox.Top = 48; _spinBox.Width = 80;
        _spinBox.Checked = true;

        // FFT: частота дискретизации и тип шкалы
        _fftRateLabel.Text = Lang.T("Частота, Гц:", "Sample rate, Hz:");
        _fftRateLabel.Left = 240; _fftRateLabel.Top = 48; _fftRateLabel.Width = 80;

        _fftRateBox.Left = 322; _fftRateBox.Top = 45; _fftRateBox.Width = 90;
        _fftRateBox.Minimum = 1; _fftRateBox.Maximum = 1_000_000;
        _fftRateBox.DecimalPlaces = 1; _fftRateBox.Increment = 10;
        _fftRateBox.Value = (decimal)Math.Clamp(PlotSettings.SampleRate, 1, 1_000_000);
        _fftRateBox.ValueChanged += (_, _) =>
        {
            PlotSettings.SampleRate = (double)_fftRateBox.Value;
            SaveSettings();
        };

        _fftLogBox.Text = Lang.T("Лог (дБ)", "Log (dB)");
        _fftLogBox.Left = 424; _fftLogBox.Top = 48; _fftLogBox.Width = 90;
        _fftLogBox.Checked = PlotSettings.FftLog;
        _fftLogBox.CheckedChanged += (_, _) =>
        {
            PlotSettings.FftLog = _fftLogBox.Checked;
            SaveSettings();
        };

        _time3dBox.Visible = _spinBox.Visible = false;
        _fftRateLabel.Visible = _fftRateBox.Visible = _fftLogBox.Visible = false;

        panel.Controls.AddRange(new Control[]
        {
            _portBtn, _baudBox, _connectBtn, _modeBtn, simBtn, _pauseBtn, _toolsBtn,
            _time3dBox, _spinBox, _fftRateLabel, _fftRateBox, _fftLogBox,
        });
        Controls.Add(panel);

        _port = Settings.Current.Port;
        RefreshPorts();
    }

    // Кнопка, раскрывающая меню под собой (вкладка с выпадающим списком)
    private static void AttachDropdown(Button btn, ContextMenuStrip menu) =>
        btn.Click += (_, _) => menu.Show(btn, new Point(0, btn.Height));

    private void UpdatePortBtn() => _portBtn.Text = $"{Lang.T("Порт", "Port")}: {(_port ?? Lang.T("нет", "none"))} ▾";

    // Содержимое меню порта собирается при каждом открытии: список портов
    // меняется динамически, галочки — по текущему выбору
    private void BuildPortMenu()
    {
        _portMenu.Items.Clear();
        if (_portNames.Length == 0)
            _portMenu.Items.Add(new ToolStripMenuItem(Lang.T(
                "Порты не найдены — «Обновить список»", "No ports found — use «Refresh list»")) { Enabled = false });

        foreach (string name in _portNames)
        {
            var item = new ToolStripMenuItem(name) { Checked = name == _port };
            string port = name;
            item.Click += (_, _) => { _port = port; UpdatePortBtn(); };
            _portMenu.Items.Add(item);
        }

        _portMenu.Items.Add(new ToolStripSeparator());
        var refresh = new ToolStripMenuItem(Lang.T("Обновить список портов", "Refresh port list"));
        refresh.Click += (_, _) => RefreshPorts();
        _portMenu.Items.Add(refresh);
    }

    private void BuildModeMenu()
    {
        _modeMenu.Items.Clear();
        string[] names = { Lang.T("Время", "Time"), Lang.T("Полярный", "Polar"), "3D", "FFT" };
        for (int m = 0; m < names.Length; m++)
        {
            var item = new ToolStripMenuItem(names[m]) { Checked = _mode == m };
            int mode = m;
            item.Click += (_, _) => SetMode(mode);
            _modeMenu.Items.Add(item);
        }
    }

    private void SetMode(int mode)
    {
        _mode = mode;
        _modeBtn.Text = $"{Lang.T("Режим", "Mode")}: {mode switch
        {
            MODE_TIME => Lang.T("Время", "Time"), MODE_POLAR => Lang.T("Полярный", "Polar"),
            MODE_3D => "3D", _ => "FFT",
        }}";
        _time3dBox.Visible = _spinBox.Visible = _mode == MODE_3D;
        _fftRateLabel.Visible = _fftRateBox.Visible = _fftLogBox.Visible = _mode == MODE_FFT;
        UpdateTitle();
    }

    private void BuildToolsMenu()
    {
        _toolsMenu.Items.Clear();

        var ranges = new ToolStripMenuItem(Lang.T("Диапазоны графика…", "Plot ranges…"));
        ranges.Click += (_, _) =>
        {
            using var dlg = new RangeForm();
            dlg.ShowDialog(this);
        };
        _toolsMenu.Items.Add(ranges);

        var resetView = new ToolStripMenuItem(Lang.T("Сброс вида и маркеров", "Reset view and markers"));
        resetView.Click += (_, _) => PlotSettings.ResetView();
        _toolsMenu.Items.Add(resetView);

        var resetAll = new ToolStripMenuItem(Lang.T("Полный сброс: порт, графики, вывод", "Full reset: port, plots, output"));
        resetAll.Click += (_, _) => FullReset();
        _toolsMenu.Items.Add(resetAll);

        _toolsMenu.Items.Add(new ToolStripSeparator());

        // Глобальное сглаживание: по умолчанию «Нет»; канал со своим
        // сглаживанием (диалог «…») перекрывает глобальный режим
        var smooth = new ToolStripMenuItem($"{Lang.T("Сглаживание", "Smoothing")}: {SmoothName(PlotSettings.GlobalSmooth)}");
        foreach ((SmoothKind kind, string ru, string en) in new[]
        {
            (SmoothKind.None, "Нет", "None"),
            (SmoothKind.MovingAverage, "Скользящее среднее", "Moving average"),
            (SmoothKind.Median, "Медиана", "Median"),
            (SmoothKind.Gaussian, "Гауссово", "Gaussian"),
            (SmoothKind.Exponential, "Экспоненциальное", "Exponential"),
        })
        {
            var si = new ToolStripMenuItem(Lang.T(ru, en)) { Checked = PlotSettings.GlobalSmooth == kind };
            SmoothKind k = kind;
            si.Click += (_, _) =>
            {
                PlotSettings.GlobalSmooth = k;
                SaveSettings();
            };
            smooth.DropDownItems.Add(si);
        }
        smooth.DropDownItems.Add(new ToolStripSeparator());
        var win = new ToolStripMenuItem($"{Lang.T("Окно", "Window")}: {PlotSettings.GlobalSmoothWindow}");
        foreach (int w in new[] { 3, 5, 7, 9, 11, 15, 21, 31, 51, 75, 101, 151, 201, 301, 501 })
        {
            var wi = new ToolStripMenuItem(w.ToString()) { Checked = PlotSettings.GlobalSmoothWindow == w };
            int ww = w;
            wi.Click += (_, _) =>
            {
                PlotSettings.GlobalSmoothWindow = ww;
                SaveSettings();
            };
            win.DropDownItems.Add(wi);
        }
        smooth.DropDownItems.Add(win);
        _toolsMenu.Items.Add(smooth);

        _toolsMenu.Items.Add(new ToolStripSeparator());

        // Язык интерфейса: применяется сразу и запоминается
        var lang = new ToolStripMenuItem(Lang.T("Язык", "Language"));
        foreach ((int code, string name) in new[] { (0, "Русский"), (1, "English") })
        {
            var li = new ToolStripMenuItem(name) { Checked = (Lang.English ? 1 : 0) == code };
            int c = code;
            li.Click += (_, _) =>
            {
                Lang.English = c == 1;
                Settings.Current.Language = c;
                Settings.Save();
                ApplyLanguage();
            };
            lang.DropDownItems.Add(li);
        }
        _toolsMenu.Items.Add(lang);

        _toolsMenu.Items.Add(new ToolStripSeparator());

        var theme = new ToolStripMenuItem($"{Lang.T("Тема", "Theme")}: {Theme.Kind switch
        {
            ThemeKind.Dark => Lang.T("Тёмная", "Dark"), ThemeKind.Light => Lang.T("Светлая", "Light"), _ => "Киберпанк",
        }}");
        foreach ((ThemeKind kind, string ru, string en) in new[]
        {
            (ThemeKind.Dark, "Тёмная", "Dark"), (ThemeKind.Light, "Светлая", "Light"), (ThemeKind.Cyberpunk, "Киберпанк", "Cyberpunk"),
        })
        {
            var ti = new ToolStripMenuItem(Lang.T(ru, en)) { Checked = Theme.Kind == kind };
            ThemeKind k = kind;
            ti.Click += (_, _) =>
            {
                Theme.Kind = k;
                Theme.Apply(this);
                Theme.ApplyTitleBar(this);
                SaveSettings();
            };
            theme.DropDownItems.Add(ti);
        }
        _toolsMenu.Items.Add(theme);
    }

    private static string SmoothName(SmoothKind k) => k switch
    {
        SmoothKind.MovingAverage => Lang.T("Скользящее среднее", "Moving average"),
        SmoothKind.Median => Lang.T("Медиана", "Median"),
        SmoothKind.Gaussian => Lang.T("Гауссово", "Gaussian"),
        SmoothKind.Exponential => Lang.T("Экспоненциальное", "Exponential"),
        _ => Lang.T("Нет", "None"),
    };

    // Переключение языка: обновляем видимые строки главного окна
    private void ApplyLanguage()
    {
        UpdatePortBtn();
        _connectBtn.Text = _serial.IsOpen
            ? Lang.T("Отключить", "Disconnect")
            : Lang.T("Подключить", "Connect");
        _pauseBtn.Text = DataHub.Paused ? Lang.T("Продолжить", "Resume") : Lang.T("Пауза", "Pause");
        _toolsBtn.Text = Lang.T("Инструменты ▾", "Tools ▾");
        _baudLabel.Text = Lang.T("Бодрейт:", "Baud rate:");
        _fftRateLabel.Text = Lang.T("Частота, Гц:", "Sample rate, Hz:");
        _fftLogBox.Text = Lang.T("Лог (дБ)", "Log (dB)");
        _spinBox.Text = Lang.T("Вращение", "Spin");
        SetMode(_mode);
        _status = _serial.IsOpen ? $"{_port} @ {_baud}"
                : _sim.Running ? Lang.T("симулятор", "simulator")
                : Lang.T("не подключено", "not connected");
        UpdateTitle();
        _console.ApplyLanguage();
        _channels.ApplyLanguage();
    }

    private void FullReset()
    {
        _serial.DiscardBuffers();
        DataHub.Reset();
        PlotSettings.ResetView();
        _console.Clear();
        _status = Lang.T("сброс: буферы порта, графики и вывод очищены", "reset: port buffers, plots and output cleared");
        UpdateTitle();
    }

    // --- Порт ---

    private void RefreshPorts()
    {
        string? prev = _port ?? Settings.Current.Port;
        // GetPortNames() может подвисать на десятки секунд из-за кривых
        // Bluetooth/COM-драйверов — сканируем в фоне, чтобы не блокировать запуск
        Task.Run(SerialPort.GetPortNames).ContinueWith(t =>
        {
            if (t.IsFaulted || IsDisposed) return;
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                _portNames = t.Result;
                _port = prev != null && _portNames.Contains(prev) ? prev
                      : _portNames.Length > 0 ? _portNames[0] : null;
                UpdatePortBtn();
            });
        });
    }

    private async void ToggleConnect()
    {
        if (_serial.IsOpen)
        {
            _serial.Close(); // дернёт OnSerialClosed
            _status = Lang.T("отключено", "disconnected");
            UpdateTitle();
            return;
        }

        if (_port == null)
        {
            MessageBox.Show(this,
                Lang.T("Нет доступных COM-портов.\nПодключи ESP32 и выбери «Обновить список» в меню «Порт».",
                       "No COM ports available.\nConnect the device and use «Refresh list» in the «Port» menu."),
                Lang.T("Ошибка порта", "Port error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(_baudBox.Text.Trim(), out int baud) || baud <= 0)
        {
            MessageBox.Show(this, Lang.T("Неверное значение бодрейта.", "Invalid baud rate value."), Lang.T("Ошибка", "Error"),
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _baud = baud;

        // Открытие порта — в фоне: зависший драйвер не должен замораживать окно
        _connectBtn.Enabled = false;
        _status = $"{Lang.T("подключение к", "connecting to")} {_port}…";
        UpdateTitle();
        string? error = await _serial.OpenAsync(_port, baud);
        _connectBtn.Enabled = true;

        if (error != null)
        {
            _status = Lang.T("не подключено", "not connected");
            UpdateTitle();
            MessageBox.Show(this, $"{Lang.T("Не удалось открыть", "Failed to open")} {_port}:\n{error}\n\n" +
                            Lang.T("Закрой монитор PlatformIO и плоттеры, проверь порт и бодрейт.", "Close the PlatformIO monitor and other plotters, check the port and baud rate."),
                            Lang.T("Ошибка порта", "Port error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        DataHub.Reset(); // старые каналы от прошлого источника не нужны
        _status = $"{_port} @ {baud}";
        _connectBtn.Text = Lang.T("Отключить", "Disconnect");
        _console.SetSendEnabled(true);
        SaveSettings();
        UpdateTitle();
    }

    private void OnSerialClosed()
    {
        _connectBtn.Text = Lang.T("Подключить", "Connect");
        _console.SetSendEnabled(false);
    }

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
        _connectBtn.Text = Lang.T("Подключить", "Connect");
        _connectBtn.Enabled = false; // один активный источник: либо порт, либо симулятор
        _console.SetSendEnabled(false);
        DataHub.Reset();             // старые каналы не смешиваем с виртуальными
        _status = $"{Lang.T("симулятор", "simulator")}: {_sim.Channels.Count} {Lang.T("канал(ов)", "channel(s)")}";
        UpdateTitle();
    }

    private void OnSimStopped()
    {
        _connectBtn.Enabled = true;
        if (!_serial.IsOpen)
            _console.SetSendEnabled(false);
        if (!_serial.IsOpen)
            _status = Lang.T("не подключено", "not connected");
        UpdateTitle();
    }

    private void UpdateTitle() =>
        Text = $"Serial Plotter — {_status} — {(Lang.English ? MODE_NAMES_EN[_mode] : MODE_NAMES_RU[_mode])}" +
               (DataHub.Paused ? (Lang.English ? " — PAUSED" : " — ПАУЗА") : "");

    // --- Зум, панорама, маркеры (режим «Время») ---

    // Область графика: поля слева под подписи оси Y, снизу — под оси X, легенду, статус
    private Rectangle PlotRect => new(56, 16,
        Math.Max(50, _plot.ClientSize.Width - 76),
        Math.Max(50, _plot.ClientSize.Height - 80));

    private double XAt(int px)
    {
        var r = PlotRect;
        return PlotSettings.LastXMin + (px - r.Left) * (PlotSettings.LastXMax - PlotSettings.LastXMin) / r.Width;
    }

    private double YAt(int py)
    {
        var r = PlotRect;
        return PlotSettings.LastYMin + (r.Bottom - py) * (PlotSettings.LastYMax - PlotSettings.LastYMin) / r.Height;
    }

    // Взять текущий вид (авто- или ручное окно) как основу панорамы/зума;
    // заполняет только ещё не заданные оси, чтобы панорама по X
    // не сбрасывала зум по Y и наоборот
    private static void InitViewFromCurrent()
    {
        PlotSettings.ViewXMin ??= PlotSettings.LastXMin;
        PlotSettings.ViewXMax ??= PlotSettings.LastXMax;
        PlotSettings.ViewYMin ??= PlotSettings.LastYMin;
        PlotSettings.ViewYMax ??= PlotSettings.LastYMax;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_mode != MODE_TIME) return;
        var r = PlotRect;
        Point p = _plot.PointToClient(MousePosition);
        if (!_plot.ClientRectangle.Contains(p) || p.X < r.Left || p.X > r.Right) return;

        // Ctrl+колесо — зум к курсору, Alt+колесо — растяжение/сжатие
        // по горизонтали, Alt+Shift+колесо — по вертикали, Shift+колесо —
        // влево/вправо, просто колесо — вверх/вниз
        bool ctrl = (ModifierKeys & Keys.Control) != 0;
        bool alt = (ModifierKeys & Keys.Alt) != 0;
        bool shift = (ModifierKeys & Keys.Shift) != 0;
        int dir = e.Delta > 0 ? 1 : -1;

        if (ctrl) ZoomAt(p, dir > 0 ? 0.8 : 1.25);
        else if (alt && shift) StretchY(p, dir > 0 ? 0.8 : 1.25);
        else if (alt) StretchX(p, dir > 0 ? 0.8 : 1.25);
        else if (shift) PanX(dir * 0.15);
        else PanY(dir * 0.15);
    }

    // Растяжение/сжатие по горизонтали вокруг точки под курсором:
    // только ось X — сигнал разворачивается/сжимается по времени
    private void StretchX(Point p, double k)
    {
        var r = PlotRect;

        double x0 = PlotSettings.ViewXMin ?? PlotSettings.LastXMin;
        double x1 = PlotSettings.ViewXMax ?? PlotSettings.LastXMax;
        double spanX = Math.Clamp((x1 - x0) * k, 2, DataHub.HISTORY);
        double fx = Math.Clamp((p.X - r.Left) / (double)r.Width, 0, 1);
        double ax = x0 + fx * (x1 - x0);
        double nx0 = ax - fx * spanX, nx1 = nx0 + spanX;
        if (_channels.XIndex < 0 && nx0 < 0) { nx1 -= nx0; nx0 = 0; } // время не в минус
        PlotSettings.ViewXMin = nx0;
        PlotSettings.ViewXMax = nx1;
    }

    // Растяжение/сжатие по вертикали вокруг значения под курсором:
    // только ось Y, метки подписываются с учётом нового диапазона
    private void StretchY(Point p, double k)
    {
        var r = PlotRect;

        double y0 = PlotSettings.ViewYMin ?? PlotSettings.LastYMin;
        double y1 = PlotSettings.ViewYMax ?? PlotSettings.LastYMax;
        double spanY = Math.Max((y1 - y0) * k, 1e-9);
        double fy = Math.Clamp((r.Bottom - p.Y) / (double)r.Height, 0, 1);
        double ay = y0 + fy * (y1 - y0);
        PlotSettings.ViewYMin = ay - fy * spanY;
        PlotSettings.ViewYMax = PlotSettings.ViewYMin + spanY;
    }

    // Зум обеих осей к курсору: k < 1 — приближение
    private void ZoomAt(Point p, double k)
    {
        var r = PlotRect;

        double x0 = PlotSettings.ViewXMin ?? PlotSettings.LastXMin;
        double x1 = PlotSettings.ViewXMax ?? PlotSettings.LastXMax;
        double spanX = Math.Clamp((x1 - x0) * k, 2, DataHub.HISTORY);
        double fx = Math.Clamp((p.X - r.Left) / (double)r.Width, 0, 1);
        double ax = x0 + fx * (x1 - x0);
        double nx0 = ax - fx * spanX, nx1 = nx0 + spanX;
        if (_channels.XIndex < 0 && nx0 < 0) { nx1 -= nx0; nx0 = 0; } // время не в минус
        PlotSettings.ViewXMin = nx0;
        PlotSettings.ViewXMax = nx1;

        double y0 = PlotSettings.ViewYMin ?? PlotSettings.LastYMin;
        double y1 = PlotSettings.ViewYMax ?? PlotSettings.LastYMax;
        double spanY = Math.Max((y1 - y0) * k, 1e-9);
        double fy = Math.Clamp((r.Bottom - p.Y) / (double)r.Height, 0, 1);
        double ay = y0 + fy * (y1 - y0);
        PlotSettings.ViewYMin = ay - fy * spanY;
        PlotSettings.ViewYMax = PlotSettings.ViewYMin + spanY;
    }

    // Панорама по X: frac — доля видимого диапазона, > 0 — вправо
    private void PanX(double frac)
    {
        InitViewFromCurrent();
        if (PlotSettings.ViewXMin is not { } x0 || PlotSettings.ViewXMax is not { } x1) return;
        double d = (x1 - x0) * frac;
        double nx0 = x0 + d, nx1 = x1 + d;
        if (_channels.XIndex < 0 && nx0 < 0) { nx1 -= nx0; nx0 = 0; }
        PlotSettings.ViewXMin = nx0;
        PlotSettings.ViewXMax = nx1;
    }

    // Панорама по Y: frac > 0 — вверх
    private void PanY(double frac)
    {
        InitViewFromCurrent();
        if (PlotSettings.ViewYMin is not { } y0 || PlotSettings.ViewYMax is not { } y1) return;
        double d = (y1 - y0) * frac;
        PlotSettings.ViewYMin = y0 + d;
        PlotSettings.ViewYMax = y1 + d;
    }

    // Зум к выделенной области: обе оси принимают диапазон рамки
    private void ZoomTo(Rectangle sel)
    {
        double x0 = XAt(sel.Left), x1 = XAt(sel.Right);
        if (x1 - x0 < 1e-9) return;
        PlotSettings.ViewXMin = _channels.XIndex < 0 ? Math.Max(0, x0) : x0;
        PlotSettings.ViewXMax = x1;

        double yLo = YAt(sel.Bottom), yHi = YAt(sel.Top);
        if (yHi < yLo) (yHi, yLo) = (yLo, yHi);
        PlotSettings.ViewYMin = yLo;
        PlotSettings.ViewYMax = yHi;
    }

    private void PanBy(Point loc)
    {
        var r = PlotRect;
        double dx = (loc.X - _lastMouse.X) * (PlotSettings.LastXMax - PlotSettings.LastXMin) / r.Width;
        double dy = (loc.Y - _lastMouse.Y) * (PlotSettings.LastYMax - PlotSettings.LastYMin) / r.Height;
        if (PlotSettings.ViewXMin is { } x0 && PlotSettings.ViewXMax is { } x1)
        {
            PlotSettings.ViewXMin = x0 - dx; // тянем вправо — вид едет влево
            PlotSettings.ViewXMax = x1 - dx;
        }
        if (PlotSettings.ViewYMin is { } y0 && PlotSettings.ViewYMax is { } y1)
        {
            PlotSettings.ViewYMin = y0 + dy; // тянем вниз — вид едет вверх
            PlotSettings.ViewYMax = y1 + dy;
        }
    }

    // Маркер измерений: Shift+клик — A, Ctrl+клик — B; повторный клик рядом снимает
    // Клик по графику ставит линейки по очереди: сначала A, потом B,
    // следующий клик начинает цикл заново. Клик работает в любом месте
    // области графика — попадать по самой линии не нужно.
    private void PlaceMarker(int px)
    {
        var r = PlotRect;
        if (px < r.Left || px > r.Right) return;

        double x = XAt(px);
        if (PlotSettings.MarkerA != null && PlotSettings.MarkerB != null)
        {
            PlotSettings.MarkerA = x; // обе стоят — начинаем заново с A
            PlotSettings.MarkerB = null;
        }
        else if (PlotSettings.MarkerA == null)
        {
            PlotSettings.MarkerA = x;
        }
        else
        {
            PlotSettings.MarkerB = x;
        }
    }

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
    // forExport: снимок без линейки курсора, маркеров и рамки выделения.
    private void RenderPlot(SvgNet.Interfaces.IGraphics g, bool forExport = false)
    {
        var rect = PlotRect;
        var ctx = new PlotContext
        {
            Visible = DataHub.Channels.Where(c => c.Visible).ToList(),
            XIndex = _channels.XIndex,
            RotX = _rotX,
            RotY = _rotY,
            Time3DAxis = _time3dBox.SelectedIndex,
            MouseX = forExport ? -1 : _mousePos.X,
            MouseY = forExport ? -1 : _mousePos.Y,
            ForExport = forExport,
        };

        switch (_mode)
        {
            case MODE_POLAR: PolarPlot.Draw(g, rect, ctx); break;
            case MODE_3D: Plot3D.Draw(g, rect, ctx); break;
            case MODE_FFT: FftPlot.Draw(g, rect, ctx); break;
            default: TimePlot.Draw(g, rect, ctx); break;
        }

        if (!forExport && _selecting && _selRect.Width > 0)
        {
            using var pen = new Pen(Theme.Accent) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, _selRect.Left, _selRect.Top, _selRect.Width - 1, _selRect.Height - 1);
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

        // Время краёв видимого окна — рядом с числом точек
        string timeInfo = "";
        if (_mode == MODE_TIME && _channels.XIndex < 0 && DataHub.Channels.Count > 0)
        {
            double t0 = PlotSettings.LastXMin / PlotSettings.SampleRate;
            double t1 = PlotSettings.LastXMax / PlotSettings.SampleRate;
            timeInfo = $"   {Lang.T("время", "time")}: {t0:0.###}2013{t1:0.###} {Lang.T("с", "s")}";
        }

        g.DrawString($"{_status}   {Lang.T("каналов", "channels")}: {DataHub.Channels.Count}   " +
                     $"{Lang.T("точек", "points")}: {DataHub.Channels.FirstOrDefault()?.Data.Count ?? 0}{timeInfo}" +
                     (_mode == MODE_TIME && !forExport ? Lang.T("   (F1 — справка по навигации)", "   (F1 — navigation help)") : ""),
                     font, textBrush, rect.Left, rect.Bottom + 42);
    }

    // --- Экспорт ---

    // Диалог отрезка (только режим «Время»); true — продолжать экспорт
    private bool ConfigureExportRange()
    {
        if (_mode != MODE_TIME) return true;
        bool timeAxis = _channels.XIndex < 0;
        using var dlg = new ExportForm(timeAxis,
            PlotSettings.LastXMin, PlotSettings.LastXMax,
            PlotSettings.LastYMin, PlotSettings.LastYMax);
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        if (dlg.UseXRange) { PlotSettings.ViewXMin = dlg.XFrom; PlotSettings.ViewXMax = dlg.XTo; }
        if (dlg.UseYRange) { PlotSettings.ViewYMin = dlg.YFrom; PlotSettings.ViewYMax = dlg.YTo; }
        if (dlg.UseXRange || dlg.UseYRange) _pendingViewReset = true;
        return true;
    }

    private void ResetViewIfExported()
    {
        if (!_pendingViewReset) return;
        _pendingViewReset = false;
        PlotSettings.ResetView();
    }

    private void SavePng()
    {
        if (!ConfigureExportRange()) return;

        using var dlg = new SaveFileDialog
        {
            Filter = Lang.T("PNG-изображение|*.png", "PNG image|*.png"),
            FileName = "serialplot.png",
            Title = Lang.T("Сохранить снимок графика", "Save plot snapshot"),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var bmp = new Bitmap(Math.Max(1, _plot.ClientSize.Width), Math.Max(1, _plot.ClientSize.Height));
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var gg = new SvgNet.GdiGraphics(g);
            RenderPlot(gg, forExport: true);
        }
        ResetViewIfExported();
        bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
        _status = $"{Lang.T("снимок сохранён", "snapshot saved")}: {Path.GetFileName(dlg.FileName)}";
        UpdateTitle();
    }

    private void SaveSvg()
    {
        if (!ConfigureExportRange()) return;

        using var dlg = new SaveFileDialog
        {
            Filter = Lang.T("SVG-изображение|*.svg", "SVG image|*.svg"),
            FileName = "serialplot.svg",
            Title = Lang.T("Сохранить снимок графика", "Save plot snapshot"),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // SvgGraphics — Graphics-совместимый векторный холст,
        // тот же код отрисовки рисует сразу в SVG
        using var svg = new SvgNet.SvgGraphics();
        RenderPlot(svg, forExport: true);
        ResetViewIfExported();
        // XML 1.0 не допускает управляющие символы (приходят с устройства
        // в именах каналов); SvgNet пишет их как &#xNN; — чистим ссылки
        File.WriteAllText(dlg.FileName, StripInvalidXmlChars(
            svg.WriteSVGString(_plot.ClientSize.Width, _plot.ClientSize.Height)));
        _status = $"{Lang.T("снимок сохранён", "snapshot saved")}: {Path.GetFileName(dlg.FileName)}";
        UpdateTitle();
    }

    // Оставляем только допустимые в XML 1.0 числовые ссылки:
    // #x9 #xA #D и >= #x20 (остальные — управляющие символы, браузер
    // отклоняет весь файл с «invalid xmlChar value N»)
    private static string StripInvalidXmlChars(string s) =>
        Regex.Replace(s, "&#x([0-9A-Fa-f]+);", m =>
        {
            int v = Convert.ToInt32(m.Groups[1].Value, 16);
            return v is 0x9 or 0xA or 0xD or >= 0x20 ? m.Value : "";
        });

    private void ExportCsv()
    {
        if (DataHub.Channels.Count == 0 || DataHub.Channels.All(c => c.Data.Count == 0))
        {
            MessageBox.Show(this, Lang.T("Нет данных для экспорта.", "No data to export."), Lang.T("Экспорт CSV", "CSV export"),
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Отрезок по оси X: время — индексы точек; XY — значения канала оси X
        bool timeAxis = _channels.XIndex < 0;
        bool useRange = false;
        double xFrom = 0, xTo = 0;
        using (var rangeDlg = new ExportForm(yEnabled: false,
            PlotSettings.LastXMin, PlotSettings.LastXMax, 0, 1))
        {
            if (rangeDlg.ShowDialog(this) != DialogResult.OK) return;
            useRange = rangeDlg.UseXRange;
            xFrom = rangeDlg.XFrom;
            xTo = rangeDlg.XTo;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = Lang.T("CSV (разделитель — запятая)|*.csv", "CSV (comma-separated)|*.csv"),
            FileName = "serialplot.csv",
            Title = Lang.T("Экспорт данных каналов", "Export channel data"),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var arrays = DataHub.Channels.Select(c => c.Data.ToArray()).ToArray();
        int n = arrays.Max(a => a.Length);
        int total = n; // буфер у всех каналов одинаковой глубины: абсолютный
                       // индекс точки = (total - a.Length) + позиция в массиве

        // Диапазон строк: время — по абсолютным индексам; XY — по значениям X
        var xch = timeAxis ? null : arrays[_channels.XIndex];
        bool RowInRange(int row)
        {
            if (!useRange) return true;
            if (timeAxis)
            {
                // абсолютный индекс самой свежей точки = total - 1
                double abs = total - 1 - (n - 1 - row);
                return abs >= xFrom && abs <= xTo;
            }
            return xch != null && row < xch.Length && xch[row] >= xFrom && xch[row] <= xTo;
        }

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", DataHub.Channels.Select(c => CsvCell(c.Name))));
        for (int i = 0; i < n; i++)
        {
            if (!RowInRange(i)) continue;
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

    // --- Настройки ---

    private void OnChannelsChanged()
    {
        // Восстановить выбор оси X из настроек, если каналы уже есть
        if (Settings.Current.XIndex >= 0 &&
            Settings.Current.XIndex < DataHub.Channels.Count &&
            _channels.XIndex != Settings.Current.XIndex)
        {
            _channels.SetXIndex(Settings.Current.XIndex);
        }
        UpdateTitle();
        // Сохранение настроек дебаунсится: события могут идти пачками
        // (мусорный поток при неверном бодрейте), а писать JSON на каждое — дорого
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSettings()
    {
        var s = Settings.Current;
        s.Port = _port ?? "";
        if (int.TryParse(_baudBox.Text.Trim(), out int baud) && baud > 0) s.Baud = baud;
        s.Theme = (int)Theme.Kind;
        s.XIndex = _channels.XIndex;
        s.SampleRate = PlotSettings.SampleRate;
        s.FftLog = PlotSettings.FftLog;
        s.GlobalSmooth = (int)PlotSettings.GlobalSmooth;
        s.GlobalSmoothWindow = PlotSettings.GlobalSmoothWindow;
        s.PolarTrail = PlotSettings.PolarTrail;
        s.Language = Lang.English ? 1 : 0;
        s.Channels = DataHub.Channels.Select(c => new Settings.ChannelInfo
        {
            Name = c.Name,
            ColorArgb = c.Color.ToArgb(),
            Visible = c.Visible,
            LineWidth = c.LineWidth,
            LineStyle = (int)c.LineStyle,
            Smooth = (int)c.Smooth,
            SmoothWindow = c.SmoothWindow,
        }).ToList();
        Settings.Save();
    }
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
