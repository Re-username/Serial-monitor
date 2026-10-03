using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Ports;

namespace JoystickPlot;

// Живой график джойстика ESP32 через COM-порт
// Формат данных: "v1,v2,..." — любое число каналов; первая нечисловая строка ("X,Y,Z") — имена каналов

internal static class Program
{
    private const int HISTORY = 400;          // точек на экране
    private const int MODE_TIME = 0, MODE_POLAR = 1, MODE_3D = 2;
    private static readonly int[] BAUDS = { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
    private static readonly string[] MODE_NAMES = { "график по времени", "полярный режим", "3D-режим" };

    // Палитра каналов по умолчанию
    private static readonly Color[] PALETTE =
    {
        ColorTranslator.FromHtml("#1E6FBA"), ColorTranslator.FromHtml("#298D49"),
        ColorTranslator.FromHtml("#D64541"), ColorTranslator.FromHtml("#E67E22"),
        ColorTranslator.FromHtml("#8E44AD"), ColorTranslator.FromHtml("#16A085"),
        ColorTranslator.FromHtml("#F1C40F"), ColorTranslator.FromHtml("#E84393"),
    };

    private sealed class Channel
    {
        public Channel(string name, Color color) { Name = name; Color = color; }
        public string Name;
        public Color Color;
        public bool Visible = true;
        public readonly Queue<double> Data = new();
    }

    // Панель графика с двойной буферизацией — без мерцания при перерисовке
    private sealed class PlotPanel : Panel
    {
        public PlotPanel() => DoubleBuffered = true;
    }

    private static readonly List<Channel> _channels = new();
    private static SerialPort? _port;
    private static int _mode = MODE_TIME;
    private static bool _dark = true;
    private static string _status = "не подключено";

    // Поворот 3D-сцены
    private static double _rotY = -0.7, _rotX = 0.5;
    private static bool _dragging;
    private static Point _lastMouse;

    private static Form _form = null!;
    private static PlotPanel _plot = null!;
    private static Panel _channelsPanel = null!;
    private static ComboBox _portBox = null!;
    private static ComboBox _baudBox = null!;
    private static Button _connectBtn = null!;
    private static Button _modeBtn = null!;
    private static Button _themeBtn = null!;

    // Тема (пересчитывается в ApplyTheme)
    private static Color _bg = Color.White;
    private static Color _plotBg = Color.FromArgb(250, 250, 250);
    private static Color _grid = Color.FromArgb(230, 230, 230);
    private static Color _text = Color.Gray;
    private static Color _axis = ColorTranslator.FromHtml("#AAB7C6");

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        _form = new Form
        {
            Text = "ESP32 Joystick — монитор",
            ClientSize = new Size(1180, 640),
        };

        _plot = new PlotPanel { Dock = DockStyle.Fill };
        _plot.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { _dragging = true; _lastMouse = e.Location; } };
        _plot.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            _rotY += (e.X - _lastMouse.X) * 0.01;
            _rotX = Math.Clamp(_rotX + (e.Y - _lastMouse.Y) * 0.01, -1.5, 1.5);
            _lastMouse = e.Location;
        };
        _plot.MouseUp += (_, _) => _dragging = false;
        _form.Controls.Add(_plot);

        BuildToolbar();
        BuildChannelsPanel();

        // Таймер чтения порта
        var readTimer = new System.Windows.Forms.Timer { Interval = 10 };
        readTimer.Tick += (_, _) => ReadPort();
        readTimer.Start();

        // Таймер перерисовки (~30 FPS), в 3D — медленный автоповорот
        var drawTimer = new System.Windows.Forms.Timer { Interval = 33 };
        drawTimer.Tick += (_, _) =>
        {
            if (_mode == MODE_3D && !_dragging) _rotY += 0.004;
            _plot.Invalidate();
        };
        drawTimer.Start();

        _plot.Paint += OnPaint;
        _form.FormClosed += (_, _) => { _port?.Close(); _port?.Dispose(); };

        ApplyTheme();
        UpdateTitle();
        Application.Run(_form);
    }

    // --- Панель настроек порта ---

    private static void BuildToolbar()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 46 };

        panel.Controls.Add(new Label { Text = "Порт:", Left = 12, Top = 14, Width = 40 });

        _portBox = new ComboBox { Left = 55, Top = 11, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };

        panel.Controls.Add(new Label { Text = "Бодрейт:", Left = 155, Top = 14, Width = 60 });

        _baudBox = new ComboBox { Left = 218, Top = 11, Width = 90 };
        foreach (int b in BAUDS) _baudBox.Items.Add(b);
        _baudBox.Text = "115200";

        var refreshBtn = new Button { Text = "Обновить", Left = 316, Top = 9, Width = 80 };
        refreshBtn.Click += (_, _) => RefreshPorts();

        _connectBtn = new Button { Text = "Подключить", Left = 404, Top = 9, Width = 110 };
        _connectBtn.Click += (_, _) => ToggleConnect();

        _modeBtn = new Button { Text = "Режим: Время", Left = 524, Top = 9, Width = 150, Enabled = false };
        _modeBtn.Click += (_, _) =>
        {
            _mode = (_mode + 1) % 3;
            _modeBtn.Text = $"Режим: {_mode switch { MODE_TIME => "Время", MODE_POLAR => "Полярный", _ => "3D" }}";
            UpdateTitle();
        };

        _themeBtn = new Button { Text = "Светлая тема", Left = 684, Top = 9, Width = 120 };
        _themeBtn.Click += (_, _) =>
        {
            _dark = !_dark;
            _themeBtn.Text = _dark ? "Светлая тема" : "Тёмная тема";
            ApplyTheme();
        };

        panel.Controls.AddRange(new Control[] { _portBox, _baudBox, refreshBtn, _connectBtn, _modeBtn, _themeBtn });
        _form.Controls.Add(panel);

        RefreshPorts();
    }

    // --- Панель каналов: видимость, цвет, имя ---

    private static void BuildChannelsPanel()
    {
        _channelsPanel = new Panel { Dock = DockStyle.Right, Width = 200, AutoScroll = true, Padding = new Padding(8) };
        var header = new Label
        {
            Text = "Каналы",
            Dock = DockStyle.Top,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
        };
        _channelsPanel.Controls.Add(header);
        _form.Controls.Add(_channelsPanel);
        RebuildChannelsPanel();
    }

    private static void RebuildChannelsPanel()
    {
        // Удаляем старые строки, оставляем заголовок
        for (int i = _channelsPanel.Controls.Count - 1; i >= 0; i--)
            if (_channelsPanel.Controls[i] is not Label || _channelsPanel.Controls[i].Dock != DockStyle.Top || i > 0)
                _channelsPanel.Controls.RemoveAt(i);

        int y = 36;
        if (_channels.Count == 0)
        {
            var empty = new Label
            {
                Text = "Нет данных — подключись к порту",
                Top = y, Left = 8, Width = 180, Height = 40,
            };
            _channelsPanel.Controls.Add(empty);
            return;
        }

        foreach (var ch in _channels)
        {
            var swatch = new Button
            {
                Left = 8, Top = y + 2, Width = 24, Height = 24,
                BackColor = ch.Color, FlatStyle = FlatStyle.Flat,
                Tag = ch,
            };
            swatch.Click += (_, _) =>
            {
                using var dlg = new ColorDialog { FullOpen = true, Color = ch.Color };
                if (dlg.ShowDialog(_form) == DialogResult.OK)
                {
                    ch.Color = dlg.Color;
                    swatch.BackColor = dlg.Color;
                }
            };

            var check = new CheckBox
            {
                Left = 40, Top = y + 4, Width = 148, Height = 22,
                Text = ch.Name, Checked = ch.Visible,
            };
            check.CheckedChanged += (_, _) => ch.Visible = check.Checked;

            _channelsPanel.Controls.Add(swatch);
            _channelsPanel.Controls.Add(check);
            y += 30;
        }
    }

    private static void ApplyTheme()
    {
        _bg = _dark ? Color.FromArgb(30, 31, 34) : Color.White;
        _plotBg = _dark ? Color.FromArgb(38, 40, 44) : Color.FromArgb(250, 250, 250);
        _grid = _dark ? Color.FromArgb(58, 61, 66) : Color.FromArgb(230, 230, 230);
        _text = _dark ? Color.FromArgb(200, 203, 210) : Color.Gray;
        _axis = _dark ? Color.FromArgb(95, 99, 110) : ColorTranslator.FromHtml("#AAB7C6");

        _form.BackColor = _bg;
        StyleChildren(_form);
        _plot.BackColor = _bg;
    }

    private static void StyleChildren(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Button btn when btn.Tag is not Channel:
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.BackColor = _dark ? Color.FromArgb(45, 47, 52) : Color.FromArgb(240, 240, 240);
                    btn.ForeColor = _text;
                    break;
                case Button swatch when swatch.Tag is Channel:
                    swatch.FlatStyle = FlatStyle.Flat; // цвет задаёт канал, не трогаем
                    break;
                case ComboBox box:
                    box.BackColor = _plotBg;
                    box.ForeColor = _text;
                    break;
                case Label or CheckBox:
                    c.ForeColor = _text;
                    break;
                case Panel p:
                    p.BackColor = _bg;
                    StyleChildren(p);
                    break;
            }
        }
    }

    private static void RefreshPorts()
    {
        string? prev = (string?)_portBox.SelectedItem;
        _portBox.Items.Clear();
        _portBox.Items.AddRange(SerialPort.GetPortNames());
        if (prev != null && _portBox.Items.Contains(prev)) _portBox.SelectedItem = prev;
        else if (_portBox.Items.Count > 0) _portBox.SelectedIndex = 0;
    }

    private static void ToggleConnect()
    {
        if (_port is { IsOpen: true })
        {
            _port.Close();
            _port.Dispose();
            _port = null;
            _status = "отключено";
            _connectBtn.Text = "Подключить";
            _modeBtn.Enabled = false;
            UpdateTitle();
            return;
        }

        if (_portBox.SelectedItem is not string portName)
        {
            MessageBox.Show("Нет доступных COM-портов.\nПодключи ESP32 и нажми «Обновить».",
                            "Ошибка порта", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!int.TryParse(_baudBox.Text.Trim(), out int baud) || baud <= 0)
        {
            MessageBox.Show("Неверное значение бодрейта.", "Ошибка",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _port = new SerialPort(portName, baud) { ReadTimeout = 500, NewLine = "\n" };
            _port.Open();
            _status = $"{portName} @ {baud}";
            _connectBtn.Text = "Отключить";
            _modeBtn.Enabled = true;
        }
        catch (Exception ex)
        {
            _port?.Dispose();
            _port = null;
            _status = "не подключено";
            MessageBox.Show($"Не удалось открыть {portName}:\n{ex.Message}\n\n" +
                            "Закрой монитор PlatformIO и плоттеры, проверь порт и бодрейт.",
                            "Ошибка порта", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        UpdateTitle();
    }

    private static void UpdateTitle() =>
        _form.Text = $"ESP32 Joystick — {_status} — {MODE_NAMES[_mode]}";

    // --- Чтение порта ---

    private static bool TryParseValues(string[] parts, out double[] values)
    {
        values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        return true;
    }

    private static void ReadPort()
    {
        try
        {
            while (_port is { IsOpen: true } && _port.BytesToRead > 0)
            {
                string? line = _port.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line) || !line.Contains(','))
                    continue; // мусор без запятых пропускаем

                var parts = line.Split(',');
                if (TryParseValues(parts, out var values))
                {
                    EnsureChannels(values.Length);
                    for (int i = 0; i < values.Length; i++)
                    {
                        _channels[i].Data.Enqueue(values[i]);
                        while (_channels[i].Data.Count > HISTORY) _channels[i].Data.Dequeue();
                    }
                }
                else if (_channels.Count == 0 || _channels.All(c => c.Data.Count == 0))
                {
                    // Строка-заголовок: "X,Y,Z" -> имена каналов
                    EnsureChannels(parts.Length);
                    for (int i = 0; i < parts.Length; i++)
                        _channels[i].Name = parts[i].Trim();
                    RebuildChannelsPanel();
                }
                // иначе битая строка — пропускаем
            }
        }
        catch (TimeoutException) { /* нет данных — не страшно */ }
    }

    private static void EnsureChannels(int count)
    {
        bool added = false;
        while (_channels.Count < count)
        {
            _channels.Add(new Channel($"Канал {_channels.Count + 1}", PALETTE[_channels.Count % PALETTE.Length]));
            added = true;
        }
        if (added) RebuildChannelsPanel();
    }

    private static List<Channel> VisibleChannels() => _channels.Where(c => c.Visible).ToList();

    private static void Range(IEnumerable<double> data, out double min, out double max)
    {
        min = double.MaxValue; max = double.MinValue;
        foreach (var v in data) { if (v < min) min = v; if (v > max) max = v; }
        if (min == double.MaxValue) { min = 0; max = 1; }
        if (max - min < 1e-9) { min -= 1; max += 1; } // не делим на ноль
    }

    // --- Отрисовка ---

    private static void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(40, 40, _plot.ClientSize.Width - 60, _plot.ClientSize.Height - 80);
        var vis = VisibleChannels();

        switch (_mode)
        {
            case MODE_POLAR: DrawPolar(g, rect, vis); break;
            case MODE_3D: Draw3D(g, rect, vis); break;
            default: DrawTime(g, rect, vis); break;
        }

        using var font = new Font("Segoe UI", 10);
        using var textBrush = new SolidBrush(_text);
        g.DrawString($"{_status}   каналов: {_channels.Count}   точек: {_channels.FirstOrDefault()?.Data.Count ?? 0}",
                     font, textBrush, rect.Left, rect.Bottom + 8);
    }

    private static void DrawHint(Graphics g, Rectangle rect, string message)
    {
        using var font = new Font("Segoe UI", 12);
        using var brush = new SolidBrush(_text);
        var size = g.MeasureString(message, font);
        g.DrawString(message, font, brush, rect.Left + (rect.Width - size.Width) / 2, rect.Top + rect.Height / 2 - 20);
    }

    // Режим 1: графики по времени

    private static void DrawTime(Graphics g, Rectangle rect, List<Channel> vis)
    {
        using (var brush = new SolidBrush(_plotBg))
            g.FillRectangle(brush, rect);
        using var gridPen = new Pen(_grid);
        using var textBrush = new SolidBrush(_text);

        for (int i = 0; i <= 4; i++)
        {
            int gy = rect.Top + rect.Height * i / 4;
            g.DrawLine(gridPen, rect.Left, gy, rect.Right, gy);
        }
        for (int i = 0; i <= 8; i++)
        {
            int gx = rect.Left + rect.Width * i / 8;
            g.DrawLine(gridPen, gx, rect.Top, gx, rect.Bottom);
        }

        if (vis.Count == 0)
        {
            DrawHint(g, rect, "Нет видимых каналов — отметь их в панели справа");
            return;
        }

        // Общий масштаб по всем видимым каналам
        double min = double.MaxValue, max = double.MinValue;
        foreach (var ch in vis)
        {
            Range(ch.Data, out double cmin, out double cmax);
            if (cmin < min) min = cmin;
            if (cmax > max) max = cmax;
        }

        // Линия середины
        using var centerPen = new Pen(_axis) { DashStyle = DashStyle.Dash };
        double mid = (min + max) / 2;
        int cy = rect.Bottom - (int)(rect.Height * (mid - min) / (max - min));
        g.DrawLine(centerPen, rect.Left, cy, rect.Right, cy);

        using var font = new Font("Segoe UI", 10);
        g.DrawString(max.ToString("0.##"), font, textBrush, rect.Left - 45, rect.Top - 6);
        g.DrawString(mid.ToString("0.##"), font, textBrush, rect.Left - 45, cy - 8);
        g.DrawString(min.ToString("0.##"), font, textBrush, rect.Left - 45, rect.Bottom - 10);

        // Легенда с текущими значениями
        int lx = rect.Left + 10;
        foreach (var ch in vis)
        {
            using var dotBrush = new SolidBrush(ch.Color);
            g.FillRectangle(dotBrush, lx, rect.Top + 10, 10, 10);
            double last = ch.Data.Count > 0 ? ch.Data.ToArray()[^1] : 0;
            string label = $"{ch.Name} = {last:0.##}";
            using var legendFont = new Font("Segoe UI", 10, FontStyle.Bold);
            g.DrawString(label, legendFont, new SolidBrush(ch.Color), lx + 14, rect.Top + 6);
            lx += (int)g.MeasureString(label, legendFont).Width + 34;
        }

        foreach (var ch in vis)
        {
            var data = ch.Data.ToArray();
            if (data.Length < 2) continue;
            using var pen = new Pen(ch.Color, 2);
            var points = new Point[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                int px = rect.Left + rect.Width * i / (HISTORY - 1);
                int py = rect.Bottom - (int)(rect.Height * (data[i] - min) / (max - min));
                points[i] = new Point(px, Math.Clamp(py, rect.Top, rect.Bottom));
            }
            g.DrawLines(pen, points);
        }
    }

    // Режим 2: полярный (первые два видимых канала)

    private static void DrawPolar(Graphics g, Rectangle rect, List<Channel> vis)
    {
        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        int R = Math.Min(rect.Width, rect.Height) / 2 - 12;

        using (var brush = new SolidBrush(_plotBg))
            g.FillRectangle(brush, rect);
        using var gridPen = new Pen(_grid);
        using var axisPen = new Pen(_axis) { DashStyle = DashStyle.Dash };
        using var textBrush = new SolidBrush(_text);

        for (int i = 1; i <= 4; i++)
        {
            int r = R * i / 4;
            g.DrawEllipse(gridPen, cx - r, cy - r, 2 * r, 2 * r);
        }
        for (int a = 0; a < 360; a += 30)
        {
            double rad = a * Math.PI / 180.0;
            g.DrawLine(gridPen, cx, cy, cx + (int)(R * Math.Cos(rad)), cy - (int)(R * Math.Sin(rad)));
        }
        g.DrawLine(axisPen, cx - R, cy, cx + R, cy);
        g.DrawLine(axisPen, cx, cy - R, cx, cy + R);

        if (vis.Count < 2)
        {
            DrawHint(g, rect, "Для полярного режима нужно минимум 2 видимых канала");
            return;
        }

        var chX = vis[0];
        var chY = vis[1];
        Range(chX.Data, out double minX, out double maxX);
        Range(chY.Data, out double minY, out double maxY);
        double cX = (minX + maxX) / 2, halfX = (maxX - minX) / 2;
        double cY = (minY + maxY) / 2, halfY = (maxY - minY) / 2;
        double half = Math.Max(halfX, halfY);

        using var font = new Font("Segoe UI", 10);
        g.DrawString(half.ToString("0.##"), font, textBrush, cx + R + 4, cy - 8);
        g.DrawString($"Центр = ({cX:0.##}, {cY:0.##}),   R — отклонение от центра,   φ — от оси +{chX.Name}",
                     font, textBrush, rect.Left + 8, rect.Bottom - 22);

        int n = Math.Min(chX.Data.Count, chY.Data.Count);
        if (n == 0) return;

        var xd = chX.Data.ToArray();
        var yd = chY.Data.ToArray();

        PointF ToScreen(double x, double y)
        {
            double dx = (x - cX) / half, dy = (cY - y) / half;
            double r = Math.Min(Math.Sqrt(dx * dx + dy * dy), 1);
            double a = Math.Atan2(dy, dx);
            return new PointF((float)(cx + r * Math.Cos(a) * R), (float)(cy - r * Math.Sin(a) * R));
        }

        if (n > 1)
        {
            using var trailPen = new Pen(Color.FromArgb(110, chY.Color)) { Width = 2 };
            var pts = new PointF[n];
            for (int i = 0; i < n; i++) pts[i] = ToScreen(xd[i], yd[i]);
            g.DrawLines(trailPen, pts);
        }

        var cur = ToScreen(xd[^1], yd[^1]);
        using var vecPen = new Pen(chX.Color, 2);
        g.DrawLine(vecPen, cx, cy, cur.X, cur.Y);
        using var dotBrush = new SolidBrush(chY.Color);
        g.FillEllipse(dotBrush, cur.X - 6, cur.Y - 6, 12, 12);
        using var dotPen = new Pen(_plotBg, 2);
        g.DrawEllipse(dotPen, cur.X - 6, cur.Y - 6, 12, 12);
        using var centerBrush = new SolidBrush(_axis);
        g.FillEllipse(centerBrush, cx - 3, cy - 3, 6, 6);

        double ddx = xd[^1] - cX, ddy = cY - yd[^1];
        double rr = Math.Min(Math.Sqrt(ddx * ddx + ddy * ddy), half);
        double phi = (Math.Atan2(ddy, ddx) * 180.0 / Math.PI + 360) % 360;

        using var legendFont = new Font("Segoe UI", 11, FontStyle.Bold);
        g.DrawString($"R = {rr:0.##}", legendFont, new SolidBrush(chX.Color), rect.Left + 10, rect.Top + 8);
        g.DrawString($"φ = {phi:F0}°", legendFont, new SolidBrush(chY.Color), rect.Left + 110, rect.Top + 8);
    }

    // Режим 3: 3D-траектория (первые три видимых канала), вращение мышью

    private static PointF Project3D(double x, double y, double z,
                                    double[] mid, double[] half, int cx, int cy, double R)
    {
        double nx = (x - mid[0]) / half[0];
        double ny = (y - mid[1]) / half[1];
        double nz = (z - mid[2]) / half[2];

        double cosY = Math.Cos(_rotY), sinY = Math.Sin(_rotY);
        double rx = nx * cosY + nz * sinY;
        double rz = -nx * sinY + nz * cosY;
        double cosX = Math.Cos(_rotX), sinX = Math.Sin(_rotX);
        double ry = ny * cosX - rz * sinX;

        return new PointF((float)(cx + rx * R), (float)(cy - ry * R));
    }

    private static void Draw3D(Graphics g, Rectangle rect, List<Channel> vis)
    {
        using (var brush = new SolidBrush(_plotBg))
            g.FillRectangle(brush, rect);
        using var gridPen = new Pen(_grid);
        using var textBrush = new SolidBrush(_text);

        if (vis.Count < 3)
        {
            DrawHint(g, rect, "Для 3D нужно минимум 3 видимых канала (X, Y, Z)");
            return;
        }

        var chans = vis.Take(3).ToArray();
        var data = chans.Select(c => c.Data.ToArray()).ToArray();
        var mid = new double[3];
        var half = new double[3];
        for (int i = 0; i < 3; i++)
        {
            Range(data[i], out double min, out double max);
            mid[i] = (min + max) / 2;
            half[i] = Math.Max((max - min) / 2, 1e-9);
        }

        int cx = rect.Left + rect.Width / 2;
        int cy = rect.Top + rect.Height / 2;
        double R = Math.Min(rect.Width, rect.Height) / 2 - 30;

        // Границы куба [-1,1]^3
        var corners = new PointF[8];
        for (int i = 0; i < 8; i++)
            corners[i] = Project3D(mid[0] + ((i & 1) != 0 ? 1 : -1) * half[0],
                                   mid[1] + ((i & 2) != 0 ? 1 : -1) * half[1],
                                   mid[2] + ((i & 4) != 0 ? 1 : -1) * half[2], mid, half, cx, cy, R);
        for (int i = 0; i < 8; i++)
            for (int b = 1; b < 8; b <<= 1)
                if ((i & b) == 0)
                    g.DrawLine(gridPen, corners[i], corners[i | b]);

        // Оси с именами каналов
        var origin = Project3D(mid[0], mid[1], mid[2], mid, half, cx, cy, R);
        using var axisFont = new Font("Segoe UI", 10, FontStyle.Bold);
        for (int i = 0; i < 3; i++)
        {
            var tip = new double[] { mid[0], mid[1], mid[2] };
            tip[i] += half[i];
            var p = Project3D(tip[0], tip[1], tip[2], mid, half, cx, cy, R);
            using var axisPen = new Pen(chans[i].Color, 2);
            g.DrawLine(axisPen, origin, p);
            g.DrawString(chans[i].Name, axisFont, new SolidBrush(chans[i].Color), p.X + 4, p.Y - 8);
        }

        // Траектория
        int n = Math.Min(Math.Min(data[0].Length, data[1].Length), data[2].Length);
        if (n > 1)
        {
            using var trailPen = new Pen(Color.FromArgb(110, chans[2].Color)) { Width = 2 };
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
                pts[i] = Project3D(data[0][i], data[1][i], data[2][i], mid, half, cx, cy, R);
            g.DrawLines(trailPen, pts);

            // Текущая точка
            var cur = pts[^1];
            using var glowBrush = new SolidBrush(Color.FromArgb(70, chans[2].Color));
            g.FillEllipse(glowBrush, cur.X - 12, cur.Y - 12, 24, 24);
            using var dotBrush = new SolidBrush(chans[2].Color);
            g.FillEllipse(dotBrush, cur.X - 5, cur.Y - 5, 10, 10);
        }

        // Легенда со значениями
        using var legendFont = new Font("Segoe UI", 10, FontStyle.Bold);
        int lx = rect.Left + 10;
        for (int i = 0; i < 3; i++)
        {
            string label = $"{chans[i].Name} = {(data[i].Length > 0 ? data[i][^1] : 0):0.##}";
            g.DrawString(label, legendFont, new SolidBrush(chans[i].Color), lx, rect.Top + 8);
            lx += (int)g.MeasureString(label, legendFont).Width + 24;
        }

        using var hintFont = new Font("Segoe UI", 9);
        g.DrawString("Перетаскивай мышью, чтобы вращать", hintFont, textBrush,
                     rect.Right - 230, rect.Bottom - 20);
    }
}
