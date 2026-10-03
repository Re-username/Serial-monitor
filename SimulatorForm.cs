namespace SerialPlotter;

// Окно настройки симулятора: виртуальные каналы, их формы сигнала,
// частота, амплитуда и смещение. Открывается немодально — график виден рядом.
public sealed class SimulatorForm : Form
{
    private readonly Simulator _sim;
    private readonly Button _startBtn;

    public SimulatorForm(Simulator sim)
    {
        _sim = sim;
        Text = "Симулятор данных";
        ClientSize = new Size(620, 380);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            EditMode = DataGridViewEditMode.EditOnEnter,
            RowHeadersVisible = false,
            DataSource = _sim.Channels,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Имя", DataPropertyName = nameof(VChannel.Name),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            HeaderText = "Форма", DataPropertyName = nameof(VChannel.Wave),
            DataSource = Enum.GetValues(typeof(WaveKind)), Width = 130,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Частота, Гц", DataPropertyName = nameof(VChannel.Freq), Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Амплитуда", DataPropertyName = nameof(VChannel.Amplitude), Width = 90 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Смещение", DataPropertyName = nameof(VChannel.Offset), Width = 90 });
        grid.BackgroundColor = Theme.PlotBg;
        grid.ForeColor = Theme.Text;
        grid.GridColor = Theme.Grid;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Theme.Bg };

        var addBtn = new Button { Text = "Добавить", Left = 8, Top = 9, Width = 90 };
        addBtn.Click += (_, _) =>
            _sim.Channels.Add(new VChannel { Name = $"CH{_sim.Channels.Count + 1}" });

        var delBtn = new Button { Text = "Удалить", Left = 106, Top = 9, Width = 90 };
        delBtn.Click += (_, _) =>
        {
            if (_sim.Running) return; // нельзя удалять на ходу — сдвинутся колонки данных
            if (grid.CurrentRow is { } row && row.Index >= 0 && row.Index < _sim.Channels.Count)
                _sim.Channels.RemoveAt(row.Index);
        };

        _startBtn = new Button { Text = "Старт", Left = 240, Top = 9, Width = 110 };
        _startBtn.Click += (_, _) => ToggleStart();

        var closeBtn = new Button { Text = "Закрыть", Left = 358, Top = 9, Width = 90 };
        closeBtn.Click += (_, _) => Close();

        bottom.Controls.AddRange(new Control[] { addBtn, delBtn, _startBtn, closeBtn });

        Controls.Add(grid);   // Fill — первым, докируется последним
        Controls.Add(bottom);

        _sim.Started += OnRunningChanged;
        _sim.Stopped += OnRunningChanged;
        FormClosing += (_, _) => _sim.Stop();
        FormClosed += (_, _) => { _sim.Started -= OnRunningChanged; _sim.Stopped -= OnRunningChanged; };

        BackColor = Theme.Bg;
        foreach (Button b in new[] { addBtn, delBtn, _startBtn, closeBtn })
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Theme.ButtonBg;
            b.ForeColor = Theme.Text;
        }
    }

    private void OnRunningChanged() => _startBtn.Text = _sim.Running ? "Остановить" : "Старт";

    private void ToggleStart()
    {
        if (_sim.Running)
        {
            _sim.Stop();
            return;
        }
        try
        {
            _sim.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Симулятор",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
