using System.Drawing.Drawing2D;

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
    public float LineWidth = 2;             // толщина линии на графике
    public LineKind LineStyle = LineKind.Solid;
    public SmoothKind Smooth = SmoothKind.None; // сглаживание (слой поверх линии)
    public int SmoothWindow = 5;            // окно сглаживания, точек (нечётное)
    public double Last;                     // последнее полученное значение
    public readonly Queue<double> Data = new();
}

// Тип линии канала.
public enum LineKind
{
    Solid,      // сплошная
    Dash,       // штриховая
    Dot,        // пунктирная
    DashDot,    // штрих-пунктир
}

// Сглаживание канала: рисуется отдельным слоем поверх исходной линии.
public enum SmoothKind
{
    None,          // без сглаживания
    MovingAverage, // скользящее среднее
    Median,        // медианный фильтр
}

public static class LineKindExt
{
    public static DashStyle ToDashStyle(this LineKind kind) => kind switch
    {
        LineKind.Dash => DashStyle.Dash,
        LineKind.Dot => DashStyle.Dot,
        LineKind.DashDot => DashStyle.DashDot,
        _ => DashStyle.Solid,
    };
}

// Заданные пользователем диапазоны графика (режим «Время»).
// Когда автоматика включена — диапазон считается по данным, как раньше.
// ViewX/ViewY — зум и панорама мышью: когда заданы, перекрывают всё остальное.
public static class PlotSettings
{
    public static bool YAuto = true;    // автомасштаб по амплитуде
    public static bool XAuto = true;    // автомасштаб по времени/оси X

    public static double YMin = -1, YMax = 1;
    public static double XMin = 0, XMax = DataHub.HISTORY;

    // Автоширина окна по времени: новые точки появляются справа,
    // заполненное окно прокручивается влево. Внутри окна — последние
    // AutoWindow точек буфера (история HISTORY доступна зумом/панорамой).
    public const int AutoWindow = 400;

    // Вид (зум/панорама) в единицах оси X и Y; null — не задан
    public static double? ViewXMin, ViewXMax, ViewYMin, ViewYMax;

    // Маркеры измерений (в единицах оси X); null — не установлен
    public static double? MarkerA, MarkerB;

    // Частота дискретизации для FFT/Δt, Гц (зависит от устройства)
    public static double SampleRate = 50;

    public static bool FftLog = true;   // FFT: логарифмическая шкала (дБ)

    // Глобальное сглаживание (меню «Инструменты»): по умолчанию выключено.
    // Применяется к каналам, у которых не задано своё сглаживание.
    public static SmoothKind GlobalSmooth = SmoothKind.None;
    public static int GlobalSmoothWindow = 9;

    // Пределы, которые реально использовались в последней отрисовке —
    // MainForm пересчитывает по ним пиксели мыши в значения данных
    public static double LastXMin, LastXMax, LastYMin, LastYMax;

    public static void ResetView()
    {
        ViewXMin = ViewXMax = ViewYMin = ViewYMax = null;
        MarkerA = MarkerB = null;
    }

    // Диапазон Y с учётом автофлага; из данных берётся только при автоматике.
    public static (double Min, double Max) ResolveY(bool auto, double dataMin, double dataMax)
    {
        if (auto) return (dataMin, dataMax);
        double min = Math.Min(YMin, YMax), max = Math.Max(YMin, YMax);
        if (max - min < 1e-9) { min -= 1; max += 1; }
        return (min, max);
    }

    // Диапазон X. В режиме времени XMax — ширина окна в точках
    // (показываются последние N точек); в XY-режиме — значения оси X.
    public static (double Min, double Max) ResolveX(bool auto, double dataMin, double dataMax, bool timeMode)
    {
        if (auto) return (dataMin, dataMax);
        if (timeMode)
        {
            int window = (int)Math.Clamp(Math.Max(XMax, XMin), 2, DataHub.HISTORY);
            return (0, window - 1);
        }
        double min = Math.Min(XMin, XMax), max = Math.Max(XMin, XMax);
        if (max - min < 1e-9) { min -= 1; max += 1; }
        return (min, max);
    }
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

    // Позиция курсора на панели графика для линейки; -1 — курсор вне панели.
    public int MouseX = -1, MouseY = -1;

    // true при экспорте PNG/SVG: без линейки, маркеров и рамки выделения
    public bool ForExport;
}
