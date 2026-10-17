namespace SerialPlotter;

// Диалог выбора отрезка для экспорта (PNG/SVG/CSV): «с .. по ..» по оси X.
// Когда ось X — время, доступен и диапазон по Y (обрезка кадра по вертикали).
// По умолчанию — весь диапазон (как раньше).
public sealed class ExportForm : Form
{
    public bool UseXRange;                      // экспортировать отрезок по X
    public double XFrom, XTo;
    public bool UseYRange;                      // (только время) обрезка по Y
    public double YFrom, YTo;

    private readonly CheckBox _xCheck;
    private readonly NumericUpDown _xFrom, _xTo;
    private readonly CheckBox _yCheck;
    private readonly NumericUpDown _yFrom, _yTo;

    public ExportForm(bool yEnabled, double xMin, double xMax, double yMin, double yMax)
    {
        Text = Lang.T("Что экспортировать", "What to export");
        ClientSize = new Size(300, yEnabled ? 232 : 172);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        _xCheck = new CheckBox { Text = Lang.T("Отрезок по оси X (с .. по ..)", "X-axis segment (from .. to ..)"), Left = 12, Top = 12, Width = 270 };
        _xFrom = MakeNum(12, 38, xMin);
        _xTo = MakeNum(158, 38, xMax);

        var yGroup = new GroupBox { Text = Lang.T("Диапазон по Y (только ось X — время)", "Y range (only when axis X is time)"), Left = 8, Top = 74, Width = 284, Height = 86 };
        _yCheck = new CheckBox { Text = Lang.T("Обрезать по Y", "Crop by Y"), Left = 12, Top = 22, Width = 260, Enabled = yEnabled };
        _yFrom = MakeNum(12, 48, yMin);
        _yTo = MakeNum(158, 48, yMax);
        yGroup.Controls.Add(_yCheck);
        yGroup.Controls.Add(_yFrom);
        yGroup.Controls.Add(_yTo);

        var okBtn = new Button { Text = "OK", Left = 118, Top = ClientSize.Height - 40, Width = 80, DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = Lang.T("Отмена", "Cancel"), Left = 206, Top = ClientSize.Height - 40, Width = 80, DialogResult = DialogResult.Cancel };

        void Sync()
        {
            _xFrom.Enabled = _xTo.Enabled = _xCheck.Checked;
            _yCheck.Enabled = yEnabled && _xCheck.Checked;
            _yFrom.Enabled = _yTo.Enabled = yEnabled && _xCheck.Checked && _yCheck.Checked;
        }
        _xCheck.CheckedChanged += (_, _) => Sync();
        _yCheck.CheckedChanged += (_, _) => Sync();
        Sync();

        okBtn.Click += (_, _) =>
        {
            UseXRange = _xCheck.Checked;
            XFrom = Math.Min((double)_xFrom.Value, (double)_xTo.Value);
            XTo = Math.Max((double)_xFrom.Value, (double)_xTo.Value);
            UseYRange = yEnabled && _xCheck.Checked && _yCheck.Checked;
            YFrom = Math.Min((double)_yFrom.Value, (double)_yTo.Value);
            YTo = Math.Max((double)_yFrom.Value, (double)_yTo.Value);
        };

        Controls.AddRange(new Control[] { _xCheck, _xFrom, _xTo, yGroup, okBtn, cancelBtn });
        AcceptButton = okBtn;
        CancelButton = cancelBtn;

        HandleCreated += (_, _) => Theme.ApplyTitleBar(this);
        Theme.Apply(this);
    }

    private static NumericUpDown MakeNum(int left, int top, double value) => new()
    {
        Left = left, Top = top, Width = 130,
        Minimum = -1000000, Maximum = 1000000,
        DecimalPlaces = 2, Increment = 1m,
        Value = (decimal)Math.Clamp(value, -1000000, 1000000),
    };
}
