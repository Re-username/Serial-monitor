using Microsoft.Maui.Platform;
using Windows.Storage.Pickers;

namespace SerialPlotter.Blazor.Services;

public static class ExportService
{
    public static async Task<string?> SaveTextAsync(string title, string fileName,
        string filterName, IReadOnlyList<string> extensions, string text)
    {
#if WINDOWS
        var picker = CreatePicker(fileName, extensions);
        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return null;
        await File.WriteAllTextAsync(file.Path, text);
        return file.Name;
#else
        string path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        await File.WriteAllTextAsync(path, text);
        return path;
#endif
    }

    public static async Task<string?> SaveBytesAsync(string title, string fileName,
        string filterName, IReadOnlyList<string> extensions, byte[] bytes)
    {
#if WINDOWS
        var picker = CreatePicker(fileName, extensions);
        var file = await picker.PickSaveFileAsync();
        if (file == null)
            return null;
        await File.WriteAllBytesAsync(file.Path, bytes);
        return file.Name;
#else
        string path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
#endif
    }

#if WINDOWS
    private static FileSavePicker CreatePicker(string fileName, IReadOnlyList<string> extensions)
    {
        string extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
            extension = extensions.FirstOrDefault() ?? ".dat";

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(fileName),
            DefaultFileExtension = extension,
        };
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), extensions.ToList());

        var window = MauiWinUIApplication.Current.Application.Windows
            .Select(w => w.Handler?.PlatformView)
            .OfType<MauiWinUIWindow>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Window not found");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window.GetWindowHandle());
        return picker;
    }
#endif
}
