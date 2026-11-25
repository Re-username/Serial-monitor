using System.Globalization;
using System.Text;

namespace SerialPlotter.Blazor.Core;

public static class DataHub
{
    public const int HISTORY = 10000;

    private static readonly object Sync = new();
    private static readonly List<Channel> _channels = new();

    public static long TotalLines { get; private set; }
    public static bool Paused;

    public static event Action? ChannelsChanged;
    public static event Action<string>? RawLine;

    public static List<SavedChannel>? SeedChannels;

    public sealed class SavedChannel
    {
        public string Name = "";
        public string Color = "";
        public bool Visible = true;
        public float LineWidth = 2;
        public LineKind LineStyle = LineKind.Solid;
        public SmoothKind Smooth = SmoothKind.None;
        public int SmoothWindow = 5;
    }

    public static int Count
    {
        get { lock (Sync) return _channels.Count; }
    }

    public static IReadOnlyList<Channel> Channels
    {
        get { lock (Sync) return _channels.ToList(); }
    }

    public static Channel? ChannelAt(int index)
    {
        lock (Sync)
            return index >= 0 && index < _channels.Count ? _channels[index] : null;
    }

    public static double[] Snapshot(Channel channel)
    {
        lock (Sync)
            return channel.Snapshot();
    }

    public static void Reset()
    {
        lock (Sync)
        {
            SeedChannels = _channels.Select(SnapshotSettings).ToList();
            _channels.Clear();
            TotalLines = 0;
        }
        ChannelsChanged?.Invoke();
    }

    public static SavedChannel SnapshotSettings(Channel c) => new()
    {
        Name = c.Name,
        Color = c.Color,
        Visible = c.Visible,
        LineWidth = c.LineWidth,
        LineStyle = c.LineStyle,
        Smooth = c.Smooth,
        SmoothWindow = c.SmoothWindow,
    };

    public static void NotifyChannelsChanged() => ChannelsChanged?.Invoke();

    public static void ClearChannel(int index)
    {
        lock (Sync)
        {
            if (index >= 0 && index < _channels.Count)
                _channels[index].Data.Clear();
        }
    }

    public static void RemoveChannelsFrom(int index)
    {
        lock (Sync)
        {
            if (index >= 0 && index < _channels.Count)
                _channels.RemoveRange(index, _channels.Count - index);
        }
        ChannelsChanged?.Invoke();
    }

    public static void PushLine(string line, int channelOffset = 0)
    {
        RawLine?.Invoke(line);

        if (Paused)
            return;

        line = line.Trim();
        if (line.Length == 0)
            return;

        var parts = line.Contains(',') ? line.Split(',') : new[] { line };
        bool changed = false;

        lock (Sync)
        {
            if (TryParseValues(parts, out var values))
            {
                changed |= EnsureChannels(channelOffset + values.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    var channel = _channels[channelOffset + i];
                    channel.Data.Enqueue(values[i]);
                    while (channel.Data.Count > HISTORY)
                        channel.Data.Dequeue();
                    channel.Last = values[i];
                }
                TotalLines++;
            }
            else if (parts.All(p => !double.TryParse(p.Trim(), NumberStyles.Float,
                                                       CultureInfo.InvariantCulture, out _)))
            {
                changed |= EnsureChannels(channelOffset + parts.Length);
                for (int i = 0; i < parts.Length && channelOffset + i < _channels.Count; i++)
                {
                    string name = CleanName(parts[i]);
                    var channel = _channels[channelOffset + i];
                    if (channel.Name != name)
                    {
                        channel.Name = name;
                        changed = true;
                    }
                }
            }
        }

        if (changed)
            ChannelsChanged?.Invoke();
    }

    private static string CleanName(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Trim())
            if (c >= ' ' && c != '\x7F')
                sb.Append(c);
        return sb.Length > 0 ? sb.ToString() : "CH";
    }

    private static bool TryParseValues(string[] parts, out double[] values)
    {
        values = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out values[i]))
                return false;
        return true;
    }

    private static bool EnsureChannels(int count)
    {
        bool added = false;
        while (_channels.Count < count)
        {
            var channel = new Channel(
                Lang.T("Канал", "Channel") + $" {_channels.Count + 1}",
                Theme.Palette[_channels.Count % Theme.Palette.Length]);

            if (SeedChannels != null && _channels.Count < SeedChannels.Count)
            {
                var saved = SeedChannels[_channels.Count];
                if (!string.IsNullOrWhiteSpace(saved.Name))
                    channel.Name = saved.Name;
                if (!string.IsNullOrWhiteSpace(saved.Color))
                    channel.Color = saved.Color;
                channel.Visible = saved.Visible;
                channel.LineWidth = saved.LineWidth;
                channel.LineStyle = saved.LineStyle;
                channel.Smooth = saved.Smooth;
                channel.SmoothWindow = saved.SmoothWindow;
            }

            _channels.Add(channel);
            added = true;
        }
        return added;
    }
}
