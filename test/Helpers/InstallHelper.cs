using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace test.Helpers;

public static class InstallHelper
{
    public static string GetFriendlyMsixError(int hresult, string message)
    {
        const int ERROR_INSTALL_CONFLICTING_PACKAGE = unchecked((int)0x80073D06);
        const int ERROR_DEPLOYMENT_IN_PROGRESS = unchecked((int)0x80073D01);
        const int ERROR_INVALID_PACKAGE = unchecked((int)0x80073CF3);
        const int ERROR_PACKAGE_NOT_FOUND = unchecked((int)0x80073CFA);
        const int ERROR_DEPLOYMENT_FAILURE = unchecked((int)0x80073CF9);

        return hresult switch
        {
            ERROR_INSTALL_CONFLICTING_PACKAGE =>
                "A newer or the same version is already installed.",
            ERROR_DEPLOYMENT_IN_PROGRESS =>
                "Another installation is in progress. Wait for it to finish and try again.",
            ERROR_INVALID_PACKAGE =>
                "Invalid or unsupported package. Ensure the package and dependencies are supported for your system.",
            ERROR_PACKAGE_NOT_FOUND =>
                "Package not found. Check the selected/downloaded file path.",
            ERROR_DEPLOYMENT_FAILURE =>
                "Windows deployment failed. Check system policies or try again.",
            _ => $"Windows deployment error (0x{hresult:X8}). {message}",
        };
    }

    public static Task ShowInstallationErrorDialogAsync(
        XamlRoot xamlRoot,
        string title,
        Exception exception
    )
    {
        string content = exception switch
        {
            COMException cex => GetFriendlyMsixError(cex.HResult, cex.Message),
            UnauthorizedAccessException ua =>
                "Failed: Access denied. Try running as administrator or ensure sideloading policy allows app packages. "
                    + ua.Message,
            _ => $"Failed: {exception.Message}",
        };

        return ShowDialogAsync(xamlRoot, title, content);
    }

    public static async Task ShowDialogAsync(XamlRoot xamlRoot, string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
        };
        await dialog.ShowAsync();
    }
}
