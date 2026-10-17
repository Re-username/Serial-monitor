using System.Globalization;
using System.Text;

namespace SerialPlotter;

// Общее хранилище входящих данных: сюда пишут и COM-порт, и симулятор.
// Формат строки: "v1,v2,..."; первая нечисловая строка ("X,Y,Z") — имена каналов.
public static class DataHub
{
    public const int HISTORY = 10000; // глубина буфера на канал (макс. окно по оси X)

    public static readonly List<Channel> Channels = new();

    // Пауза записи: сырые строки уходят в консоль, но в каналы не добавляются
    public static bool Paused;

    // Структура каналов изменилась (добавились каналы / пришёл заголовок имён)
    public static event Action? ChannelsChanged;

    // Конфигурация каналов, применяемая к вновь созданным (по индексу):
    // при переподключении имена/цвета не теряются, пока устройство не
    // пришлёт новый заголовок. Сидируется из файла настроек при старте.
    public static List<SavedChannel>? SeedChannels;

    public sealed class SavedChannel
    {
        public string Name = "";
        public Color Color;
        public bool Visible = true;
        public float LineWidth = 2;
        public LineKind LineStyle = LineKind.Solid;
        public SmoothKind Smooth = SmoothKind.None;
        public int SmoothWindow = 5;
    }

    // Сырая строка от источника — для консоли. Вызывается до разбора.
    public static event Action<string>? RawLine;

    public static void Reset()
    {
        // Запоминаем конфигурацию: при повторном подключении каналы
        // пересоздадутся с теми же именами/цветами, пока не придёт заголовок
        SeedChannels = Channels.Select(Snapshot).ToList();
        Channels.Clear();
        ChannelsChanged?.Invoke();
    }

    public static SavedChannel Snapshot(Channel c) => new()
    {
        Name = c.Name, Color = c.Color, Visible = c.Visible,
        LineWidth = c.LineWidth, LineStyle = c.LineStyle,
        Smooth = c.Smooth, SmoothWindow = c.SmoothWindow,
    };

    // Каналы не добавились/не удалились, но их свойства изменились
    // (пользователь переименовал канал) — просим интерфейс перестроиться.
    public static void NotifyChannelsChanged() => ChannelsChanged?.Invoke();

    public static void PushLine(string line)
    {
        RawLine?.Invoke(line);

        if (Paused)
            return; // запись на паузе — данные только в консоль

        line = line.Trim();
        if (line.Length == 0 || !line.Contains(','))
            return; // мусор без запятых пропускаем

        var parts = line.Split(',');
        if (TryParseValues(parts, out var values))
        {
            if (EnsureChannels(values.Length))
                ChannelsChanged?.Invoke();

            for (int i = 0; i < values.Length; i++)
            {
                var ch = Channels[i];
                ch.Data.Enqueue(values[i]);
                while (ch.Data.Count > HISTORY)
                    ch.Data.Dequeue();
                ch.Last = values[i];
            }
        }
        // Строка целиком нечисловая — заголовок с именами каналов.
        // Принимаем всегда: после перезагрузки контроллер шлёт его заново,
        // и имена нужно обновить даже если каналы уже наполнены данными.
        // Событие — только если имена реально изменились: при неверном
        // бодрейте каждая мусорная строка была бы «заголовком», и частый
        // ChannelsChanged пересоздавал бы интерфейс на каждую строку.
        else if (parts.All(p => !double.TryParse(p.Trim(), NumberStyles.Float,
                                                 CultureInfo.InvariantCulture, out _)))
        {
            bool changed = EnsureChannels(parts.Length);
            for (int i = 0; i < parts.Length && i < Channels.Count; i++)
            {
                string name = CleanName(parts[i]);
                if (Channels[i].Name != name) { Channels[i].Name = name; changed = true; }
            }
            if (changed) ChannelsChanged?.Invoke();
        }
        // иначе битая строка — пропускаем
    }

    // Имена попадают в подписи графика и в SVG — управляющие символы
    // (мусор с порта при неверном бодрейте) недопустимы в XML 1.0
    private static string CleanName(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Trim())
            if (c >= ' ' && c != '\x7F') sb.Append(c);
        return sb.Length > 0 ? sb.ToString() : "CH";
    }

    private static bool TryParseValues(string[] parts, out double[] values)
    {
        values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return false;
        return true;
    }

    private static bool EnsureChannels(int count)
    {
        bool added = false;
        while (Channels.Count < count)
        {
            var ch = new Channel($"Канал {Channels.Count + 1}",
                                 Theme.Palette[Channels.Count % Theme.Palette.Length]);
            if (SeedChannels != null && Channels.Count < SeedChannels.Count)
            {
                // Восстанавливаем прошлую конфигурацию; заголовок от
                // устройства при желании перезапишет имена
                var s = SeedChannels[Channels.Count];
                if (!string.IsNullOrWhiteSpace(s.Name)) ch.Name = s.Name;
                if (s.Color.A > 0) ch.Color = s.Color;
                ch.Visible = s.Visible;
                ch.LineWidth = s.LineWidth;
                ch.LineStyle = s.LineStyle;
                ch.Smooth = s.Smooth;
                ch.SmoothWindow = s.SmoothWindow;
            }
            Channels.Add(ch);
            added = true;
        }
        return added;
    }
}
