using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
namespace ProtoBench.Desktop.Services;

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
    public async Task<string?> SaveAsync(string suggestedFileName = "synthetic-output", string extension = ".dat", string fileType = "Data file")
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedFileName };
        picker.FileTypeChoices.Add(fileType, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(Window));
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
