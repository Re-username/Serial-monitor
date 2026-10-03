namespace SerialPlotter;

// Один канал данных: имя, цвет, видимость и кольцевой буфер точек.
public sealed class Channel
{
    public Channel(string name, Color color)
    {
        Name = name;
        Color = color;
    }

    public string Name;
    public Color Color;
    public bool Visible = true;
    public double Last;                     // последнее полученное значение
    public readonly Queue<double> Data = new();
}

// Контекст, передаваемый отрисовщикам графиков.
public sealed class PlotContext
{
    public IReadOnlyList<Channel> Visible = Array.Empty<Channel>();

    // Индекс канала для оси X в DataHub.Channels; -1 — ось X это время.
    public int XIndex = -1;

    public Channel? XChannel =>
        XIndex >= 0 && XIndex < DataHub.Channels.Count ? DataHub.Channels[XIndex] : null;

    // Каналы для оси Y: все видимые, кроме канала, выбранного на ось X.
    public IEnumerable<Channel> YChannels =>
        XChannel is { } xc ? Visible.Where(c => !ReferenceEquals(c, xc)) : Visible;

    // Поворот 3D-сцены (используется только в Plot3D).
    public double RotX, RotY;

    // 3D: на какую ось поставить время — 0 никуда, 1..3 — на ось X/Y/Z
    // (тогда каналов нужно два, а не три: они займут оставшиеся оси).
    public int Time3DAxis;
}
