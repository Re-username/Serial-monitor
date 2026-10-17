namespace SerialPlotter;

// Диалог диапазонов графика (режим «Время»): автоматика или ручные
// пределы по амплитуде (Y) и по времени/оси X, плюс хвост полярного графика.
public sealed class RangeForm : Form
{
    public RangeForm()
    {
        Text = Lang.T("Диапазоны графика", "Plot ranges");
        ClientSize = new Size(320, 386);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        // --- Ось Y (амплитуда) ---
        var yGroup = new GroupBox
        {
            Text = Lang.T("Ось Y (амплитуда)", "Axis Y (amplitude)"),
            Left = 10, Top = 8, Width = 300, Height = 100,
        };

        var yAuto = new CheckBox
        {
            Text = Lang.T("Автоматически", "Automatic"),
            Left = 12, Top = 18, Width = 150, Checked = PlotSettings.YAuto,
        };
        var yMinNum = MakeNum(12, 62, PlotSettings.YMin);
        var yMaxNum = MakeNum(158, 62, PlotSettings.YMax);
        yGroup.Controls.Add(yAuto);
        yGroup.Controls.Add(yMinNum);
        yGroup.Controls.Add(yMaxNum);
        yGroup.Controls.Add(new Label { Text = Lang.T("мин:", "min:"), Left = 12, Top = 44, Width = 130 });
        yGroup.Controls.Add(new Label { Text = Lang.T("макс:", "max:"), Left = 158, Top = 44, Width = 130 });

        // --- Ось X (время / ось X) ---
        var xGroup = new GroupBox
        {
            Text = Lang.T("Ось X (время / ось X)", "Axis X (time / axis X)"),
            Left = 10, Top = 116, Width = 300, Height = 100,
        };

        var xAuto = new CheckBox
        {
            Text = Lang.T("Автоматически", "Automatic"),
            Left = 12, Top = 18, Width = 150, Checked = PlotSettings.XAuto,
        };
        var xMinNum = MakeNum(12, 62, PlotSettings.XMin);
        var xMaxNum = MakeNum(158, 62, PlotSettings.XMax);
        xGroup.Controls.Add(xAuto);
        xGroup.Controls.Add(xMinNum);
        xGroup.Controls.Add(xMaxNum);
        xGroup.Controls.Add(new Label { Text = Lang.T("мин:", "min:"), Left = 12, Top = 44, Width = 130 });
        xGroup.Controls.Add(new Label { Text = Lang.T("макс:", "max:"), Left = 158, Top = 44, Width = 130 });

        // --- Полярный график: длина хвоста траектории ---
        var polarGroup = new GroupBox
        {
            Text = Lang.T("Полярный график", "Polar plot"),
            Left = 10, Top = 224, Width = 300, Height = 62,
        };
        var polarNum = new NumericUpDown
        {
            Left = 12, Top = 22, Width = 130,
            Minimum = 10, Maximum = 10000, Increment = 10,
            Value = Math.Clamp(PlotSettings.PolarTrail, 10, 10000),
        };
        polarGroup.Controls.Add(polarNum);
        polarGroup.Controls.Add(new Label
        {
            Text = Lang.T("Хвост траектории, точек:", "Trail length, points:"),
            Left = 150, Top = 26, Width = 145,
        });

        // В режиме времени ось X — это окно: показываются последние N точек
        var hint = new Label
        {
            Text = Lang.T(
                "Режим «Время»: «макс» — ширина окна в точках\r\n(показываются последние N точек, от 2 до 10000).",
                "Time mode: «max» is the window width in points\r\n(shows the last N points, from 2 to 10000)."),
            Left = 12, Top = 292, Width = 296, Height = 40,
        };

        var okBtn = new Button { Text = "OK", Left = 138, Top = 340, Width = 80, Height = 28, DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = Lang.T("Отмена", "Cancel"), Left = 228, Top = 340, Width = 80, Height = 28, DialogResult = DialogResult.Cancel };

        void SyncEnabled()
        {
            yMinNum.Enabled = yMaxNum.Enabled = !yAuto.Checked;
            xMinNum.Enabled = xMaxNum.Enabled = !xAuto.Checked;
        }
        yAuto.CheckedChanged += (_, _) => SyncEnabled();
        xAuto.CheckedChanged += (_, _) => SyncEnabled();
        SyncEnabled();

        okBtn.Click += (_, _) =>
        {
            PlotSettings.YAuto = yAuto.Checked;
            PlotSettings.YMin = (double)yMinNum.Value;
            PlotSettings.YMax = (double)yMaxNum.Value;
            PlotSettings.XAuto = xAuto.Checked;
            PlotSettings.XMin = (double)xMinNum.Value;
            PlotSettings.XMax = (double)xMaxNum.Value;
            PlotSettings.PolarTrail = (int)polarNum.Value;
        };

        Controls.AddRange(new Control[] { yGroup, xGroup, polarGroup, hint, okBtn, cancelBtn });
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        HandleCreated += (_, _) => Theme.ApplyTitleBar(this);
        Theme.Apply(this);
    }

    private static NumericUpDown MakeNum(int left, int top, double value) => new()
    {
        Left = left, Top = top, Width = 130,
        Minimum = -1000000, Maximum = 1000000,
        DecimalPlaces = 2, Increment = 0.5m,
        Value = (decimal)Math.Clamp(value, -1000000, 1000000),
    };
}
