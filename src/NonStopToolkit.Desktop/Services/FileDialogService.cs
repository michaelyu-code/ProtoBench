using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
namespace NonStopToolkit.Desktop.Services;

public sealed class FileDialogService
{
    public Window Window { get; set; } = null!;
    public async Task<string?> OpenAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(Window));
        return (await picker.PickSingleFileAsync())?.Path;
    }
    public async Task<string?> SaveAsync()
    {
        var picker = new FileSavePicker { SuggestedFileName = "synthetic-output" };
        picker.FileTypeChoices.Add("Data file", new List<string> { ".dat" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(Window));
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
