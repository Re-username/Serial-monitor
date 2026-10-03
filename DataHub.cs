using System.Globalization;

namespace SerialPlotter;

// Общее хранилище входящих данных: сюда пишут и COM-порт, и симулятор.
// Формат строки: "v1,v2,..."; первая нечисловая строка ("X,Y,Z") — имена каналов.
public static class DataHub
{
    public const int HISTORY = 400; // точек на экране

    public static readonly List<Channel> Channels = new();

    // Структура каналов изменилась (добавились каналы / пришёл заголовок имён)
    public static event Action? ChannelsChanged;

    // Сырая строка от источника — для консоли. Вызывается до разбора.
    public static event Action<string>? RawLine;

    public static void Reset()
    {
        Channels.Clear();
        ChannelsChanged?.Invoke();
    }

    public static void PushLine(string line)
    {
        RawLine?.Invoke(line);

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
        else if (Channels.Count == 0 || Channels.All(c => c.Data.Count == 0))
        {
            // Строка-заголовок: "X,Y,Z" -> имена каналов
            EnsureChannels(parts.Length);
            for (int i = 0; i < parts.Length && i < Channels.Count; i++)
                Channels[i].Name = parts[i].Trim();
            ChannelsChanged?.Invoke();
        }
        // иначе битая строка — пропускаем
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
            Channels.Add(new Channel($"Канал {Channels.Count + 1}",
                                     Theme.Palette[Channels.Count % Theme.Palette.Length]));
            added = true;
        }
        return added;
    }
}
