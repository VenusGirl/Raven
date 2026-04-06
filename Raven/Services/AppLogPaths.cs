namespace Raven.Services;

public static class AppLogPaths
{
    private static readonly string _baseLogRoot = ResolveBaseLogRoot();

    public static string LogDirectory { get; } = Path.Combine(_baseLogRoot, "Raven", "Logs");

    public static string RuntimeLogFilePath { get; } = Path.Combine(LogDirectory, "runtime-.log");
    public static string CrashLogFilePath { get; } = Path.Combine(LogDirectory, "crash-.log");
    public static string InstallLogFilePath { get; } = Path.Combine(LogDirectory, "install-.log");

    public static string EnsureLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        return LogDirectory;
    }

    private static string ResolveBaseLogRoot()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify
        );

        return string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData;
    }
}
