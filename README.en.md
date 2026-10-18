# Serial Plotter (COM port monitor)

A live plot for joystick/sensor data from an **ESP32 over a COM port**, built with WinForms (.NET 8).
The app reads lines like `v1,v2,...` from the serial port and plots the data in real time:
a regular time plot, a polar mode and a 3D trajectory.

*[Русская версия](README.md)*

## Requirements

- Windows (WinForms app, `net8.0-windows`)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build and run

```bash
dotnet build          # build
dotnet run            # build and run
```

A single-file build (no .NET required on the target machine, no language
folders or loose DLLs — everything inside the exe, ~70 MB):

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

In PowerShell the backslash `\` does not work as a line continuation character —
use a single line or the backtick `` ` ``:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

The result is `publish/SerialPlotter.exe` — copy it to any Windows x64 machine and run.

Alternatively, open the project folder in Visual Studio / Rider and run as usual.
Dependencies: `System.IO.Ports` (COM port access) and `SvgNet` (SVG export) —
both are restored by NuGet automatically.

## How to use

1. Connect the ESP32 to the PC via USB.
2. Press **«Port ▾» → «Refresh port list»**.
3. Pick the port and baud rate (default `115200`), press **«Connect»**.
4. Data appears on the plot automatically.

> If the port fails to open — close the serial monitor in Arduino IDE / PlatformIO
> and any other program that might be holding the port.

### Data format from the ESP32

Each line is comma-separated numbers, any number of channels:

```
12.4,-3.1,0.85
12.6,-3.0,0.87
...
```

The first non-numeric line with commas is treated as a **header — channel names**:

```
X,Y,Z
```

After it, the channels in the right panel get these names.

Important: the decimal separator is a **dot** (invariant culture).
Example for ESP32 (Arduino):

```cpp
void setup() {
  Serial.begin(115200);
  Serial.println("X,Y");        // once — channel names
}

void loop() {
  float x = analogRead(34) / 4095.0f;
  float y = analogRead(35) / 4095.0f;
  Serial.printf("%.3f,%.3f\n", x, y);
  delay(10);
}
```

### Display modes

The **«Mode ▾»** button switches the rendering:

| Mode | What it shows |
|---|---|
| **Time** | Channel plots over time (shared scale), or an XY plot if a channel is assigned to axis X |
| **Polar** | The first two visible channels as a vector: R (deviation) and φ (angle) |
| **3D** | The first three visible channels as a trajectory in space; rotate by dragging with the mouse. In this mode the toolbar shows extra settings: the **«3D: no time / t → X / t → Y / t → Z»** selector replaces the chosen axis with time — then two visible channels are enough (they take the remaining axes); the **«Spin»** checkbox toggles scene auto-rotation |
| **FFT** | Spectrum of the first visible channel (FFT up to 4096 points). In the toolbar: **«Sample rate, Hz»** (default 50 Hz — the simulator rate; set the actual rate for real hardware), **«Log (dB)»** — logarithmic (dB) or linear amplitude scale, **«Window»** — window function: rectangular (max frequency resolution, strong spectral leakage), Hann (balanced, default), Hamming, Blackman, Blackman-Harris (max side-lobe suppression). The plot header shows the peak frequency and level |

### Zoom, pan, markers (Time mode)

- **Left-drag** — zoom-to-region rectangle: both axes take the range of the
  selected area (a plain click without dragging places marker A/B).
- **Middle button** — panning.
- **Mouse wheel**:
  - **Ctrl + wheel** — zoom both axes to the cursor;
  - **Alt + wheel** — stretch/compress horizontally;
  - **Alt + Shift + wheel** — stretch/compress vertically;
  - **Shift + wheel** — navigate left/right;
  - plain wheel — navigate up/down.
- **Double click** — reset view and markers.
- **Click on the plot** places measurement markers in turn: first click —
  marker **A**, second — **B** (between them **Δt** in points and milliseconds
  and **ΔV** of each visible channel are shown), third starts the cycle over.
  The click works anywhere on the plot area — you don't need to hit the line.

The auto window shows the last 400 points and is pinned to the right edge:
new points appear on the right, and a filled window **scrolls left**.
The 10000-point history is available via zoom and pan.

### Channel manager (right)

- **Checkbox** — show/hide the channel.
- **Color swatch** — pick the channel color.
- **«…»** — rename the channel, set line width and line style
  (solid, dash, dot, dash-dot), and **smoothing**:
  moving average, median, Gaussian or exponential filter with an adjustable window.
  Smoothing is drawn as a separate translucent layer over the original line.
- **«X axis»** — what to plot against: **«Time»** or any channel.
  If a channel is selected, Time mode becomes an XY plot: the selected channel
  on the horizontal axis, the other visible channels on the vertical axis.
  The channel on axis X is marked with «— X axis».

### Toolbar

The first row is buttons with dropdown menus:

- **«Port ▾»** — COM port selection (checkmark on the current one) and
  **«Refresh port list»**. Next to it the **«Baud rate»** field — pick from
  the list or type any value (persisted between runs).
- **«Connect»** — open/close the port.
- **«Mode ▾»** — pick the rendering: Time / Polar / 3D / FFT.
- **«Simulator»**, **«Pause»**.
- **«Tools ▾»** — **«Plot ranges…»**: automatic-range checkboxes (default —
  fit to data) and manual min/max for amplitude (Y) and for time/axis X.
  In Time mode the «max» of axis X is the window width in points (the last
  N of 10000 points are shown). Also **«Reset view and markers»**,
  **«Full reset»** (port, plots, output), **«Smoothing»** — a global filter
  for all channels: off (default) / moving average / median / Gaussian /
  exponential with a window choice; a channel with its own smoothing (the
  «…» dialog) overrides the global mode. Plus the **«Theme»** submenu
  (dark / light / cyberpunk) and the **«Language»** submenu (Русский / English).

The second row shows the active mode's settings (visible only in their modes):
time axis and auto-rotation in 3D, sample rate and scale in FFT.

### Ruler and legend

- Hovering the cursor over the plot shows a **ruler**: dashed vertical and
  horizontal lines; the amplitude of the horizontal line on the left,
  the time (point index) or the X axis value at the bottom, and the values
  of all visible channels at the cursor.
- The **legend** (name and current value of each channel) is drawn below the
  plot so it doesn't cover the lines.

### Console (bottom)

An always-visible log of raw lines from the source (port or simulator)
with timestamps. **«Pause»** and **«Clear»** buttons. An input box +
**«Send»** (or Enter) — send data to the open port. The **Incoming** tab
shows received lines, **Outgoing** — what you sent.

### Simulator

The **«Simulator»** button opens the virtual channel window — test the plots
without hardware. Each channel has a name, waveform (sine, square, sawtooth,
noise, constant, random walk), frequency, amplitude and offset. **«Start»** —
data flows into the same pipeline as from the COM port (50 lines/s): visible
both on the plot and in the console. The simulator runs **together with a
connected port**: virtual channels are drawn after the port's channels (or
vice versa), and neither source overwrites the other's data. When both are
active the simulator is **clocked by the port** — one sample per received
line, so the signals move strictly in sync; on start the simulator
back-generates its history so both signals begin at the same moment.

> If you need output to a **real virtual COM port** (so any third-party
> program can see the data), install the [com0com](https://com0com.sourceforge.net/)
> driver and connect to its pair like a normal port — the built-in simulator
> covers this scenario inside the app without installing drivers.

### Miscellaneous

- The **«Pause»** toolbar button stops writing to channels (the plot freezes
  while raw lines keep flowing to the console). Click again — «Resume».
- **Right-click on the plot** — export: a **PNG** snapshot or vector **SVG**
  (what's on screen, without the cursor ruler) and **CSV data export**
  (columns are channels, decimal separator is a dot). Before export you get
  a **segment selection «from .. to ..»** on axis X; if axis X is time,
  Y cropping is also available (the frame is limited to the given range).
- **«Full reset»** (Tools menu) — reset port buffers, clear all channels
  and the console.
- **«Theme»** cycles through: dark → light → **cyberpunk** (neon: black
  background, cyan/magenta, inspired by MathArt). The theme applies to the
  whole UI including the channel palette; cyberpunk is the default.
- **Settings persist** between runs (`%AppData%/SerialPlotter/settings.json`):
  last port and baud rate, theme, language, X axis selection, sample rate,
  as well as channel names, colors, visibility, line styles and smoothing.
  On reconnect the channels are recreated with the saved configuration until
  the device sends a new header; after a **controller reboot** the repeated
  header updates the names automatically — no need to restart the app.
- The on-screen history is **10000 points** per channel.
- Status (port, baud rate, channel and point counts) — in the window title
  and below the plot.

## Project structure

```
SerialPlotter.csproj — project (.NET 8, WinForms, System.IO.Ports)
app.ico              — application icon
Program.cs           — entry point
MainForm.cs          — main window: toolbar, layout, timers, render dispatcher
Lang.cs              — bilingual UI strings (Russian / English)
Theme.cs             — dark/light theme, channel palette
Channel.cs           — channel model + render context (PlotContext)
DataHub.cs           — receives lines from any source, parsing, channel storage
SerialLink.cs        — COM port I/O
Simulator.cs         — virtual channel generator (WaveKind, VChannel, Simulator)
SimulatorForm.cs     — simulator settings window
ChannelsPanel.cs     — channel manager: visibility, color, edit dialog, X axis
ConsolePanel.cs      — raw port data console
TimePlot.cs          — time plot / XY plot: ruler, markers, zoom view, smoothing layer
FftPlot.cs           — FFT mode: channel spectrum, linear/dB scale
Fft.cs               — iterative radix-2 FFT, window functions
ExportForm.cs        — export segment selection dialog
Settings.cs          — settings persistence (JSON in %AppData%)
RangeForm.cs         — plot ranges dialog
PolarPlot.cs         — polar mode
Plot3D.cs            — 3D trajectory
PlotUtil.cs          — shared render helpers (filters, measure, hints)
HelpForm.cs          — F1 help
```

## Troubleshooting

- **«Failed to open port»** — the port is held by another program (Arduino IDE
  monitor, PlatformIO, etc.) or the baud rate is wrong.
- **No data but the port is open** — check that the ESP32 actually sends
  comma-separated lines and that the baud rate matches.
- **Channel names not picked up** — the header (`X,Y,Z`) can arrive at any
  moment: it updates the names both at the start and after a controller reboot.
