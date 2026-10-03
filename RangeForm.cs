namespace SerialPlotter;

// Диалог диапазонов графика (режим «Время»): автоматика или ручные
// пределы по амплитуде (Y) и по времени/оси X.
public sealed class RangeForm : Form
{
    public RangeForm()
    {
        Text = "Диапазоны графика";
        ClientSize = new Size(320, 322);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        // --- Ось Y (амплитуда) ---
        var yGroup = new GroupBox { Text = "Ось Y (амплитуда)", Left = 10, Top = 8, Width = 300, Height = 100 };

        var yAuto = new CheckBox { Text = "Автоматически", Left = 12, Top = 18, Width = 150, Checked = PlotSettings.YAuto };
        var yMinNum = MakeNum(12, 62, PlotSettings.YMin);
        var yMaxNum = MakeNum(158, 62, PlotSettings.YMax);
        yGroup.Controls.Add(yAuto);
        yGroup.Controls.Add(yMinNum);
        yGroup.Controls.Add(yMaxNum);
        yGroup.Controls.Add(new Label { Text = "мин:", Left = 12, Top = 44, Width = 130 });
        yGroup.Controls.Add(new Label { Text = "макс:", Left = 158, Top = 44, Width = 130 });

        // --- Ось X (время / ось X) ---
        var xGroup = new GroupBox { Text = "Ось X (время / ось X)", Left = 10, Top = 116, Width = 300, Height = 100 };

        var xAuto = new CheckBox { Text = "Автоматически", Left = 12, Top = 18, Width = 150, Checked = PlotSettings.XAuto };
        var xMinNum = MakeNum(12, 62, PlotSettings.XMin);
        var xMaxNum = MakeNum(158, 62, PlotSettings.XMax);
        xGroup.Controls.Add(xAuto);
        xGroup.Controls.Add(xMinNum);
        xGroup.Controls.Add(xMaxNum);
        xGroup.Controls.Add(new Label { Text = "мин:", Left = 12, Top = 44, Width = 130 });
        xGroup.Controls.Add(new Label { Text = "макс:", Left = 158, Top = 44, Width = 130 });

        // В режиме времени ось X — это окно: показываются последние N точек
        var hint = new Label
        {
            Text = "Режим «Время»: «макс» — ширина окна в точках\r\n(показываются последние N точек, от 2 до 400).",
            Left = 12, Top = 224, Width = 296, Height = 40,
        };

        var okBtn = new Button { Text = "OK", Left = 138, Top = 276, Width = 80, Height = 28, DialogResult = DialogResult.OK };
        var cancelBtn = new Button { Text = "Отмена", Left = 228, Top = 276, Width = 80, Height = 28, DialogResult = DialogResult.Cancel };

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
        };

        Controls.AddRange(new Control[] { yGroup, xGroup, hint, okBtn, cancelBtn });
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
