using System.Diagnostics;
using Windows.Management.Deployment;

namespace test.Services;

public static class PackagedAppDiscovery
{
    public sealed record InstalledAppInfo(bool IsInstalled, string? InstalledUtc);

    public enum LaunchFailureReason
    {
        None = 0,
        PackageFamilyNameMissing,
        NotInstalled,
        NoAppEntries,
        LaunchFailed,
    }

    public sealed record PackagedLaunchResult(
        bool Success,
        string? InstalledUtc,
        string? LaunchedAppUserModelId,
        LaunchFailureReason FailureReason
    );

    public static bool IsInstalled(string? packageFamilyName)
    {
        var info = GetInstalledInfo(packageFamilyName);
        return info.IsInstalled;
    }

    public static InstalledAppInfo GetInstalledInfo(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
            return new InstalledAppInfo(false, null);

        var installedUtc = GetInstalledUtc(packageFamilyName);
        return installedUtc != null
            ? new InstalledAppInfo(true, installedUtc.Value.ToString("o"))
            : new InstalledAppInfo(false, null);
    }

    public static DateTimeOffset? GetInstalledUtc(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
            return null;

        try
        {
            var pm = new PackageManager();
            var pkg = pm.FindPackagesForUser(string.Empty, packageFamilyName).FirstOrDefault();
            var path = pkg?.InstalledLocation?.Path;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return null;

            var creationUtc = Directory.GetCreationTimeUtc(path);
            return new DateTimeOffset(creationUtc, TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> TryLaunchAsync(string? packageFamilyName)
    {
        var result = await TryLaunchDetailedAsync(packageFamilyName);
        return result.Success;
    }

    public static async Task<PackagedLaunchResult> TryLaunchDetailedAsync(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return new PackagedLaunchResult(
                Success: false,
                InstalledUtc: null,
                LaunchedAppUserModelId: null,
                FailureReason: LaunchFailureReason.PackageFamilyNameMissing
            );
        }

        try
        {
            var pm = new PackageManager();
            var pkg = pm.FindPackagesForUser(string.Empty, packageFamilyName).FirstOrDefault();
            if (pkg == null)
            {
                return new PackagedLaunchResult(
                    Success: false,
                    InstalledUtc: null,
                    LaunchedAppUserModelId: null,
                    FailureReason: LaunchFailureReason.NotInstalled
                );
            }

            var installedUtc = GetInstalledUtc(packageFamilyName);
            var installedUtcString = installedUtc?.ToString("o");

            var entries = await pkg.GetAppListEntriesAsync();
            var entry = entries.FirstOrDefault();
            if (entry == null)
            {
                return new PackagedLaunchResult(
                    Success: false,
                    InstalledUtc: installedUtcString,
                    LaunchedAppUserModelId: null,
                    FailureReason: LaunchFailureReason.NoAppEntries
                );
            }

            await entry.LaunchAsync();
            return new PackagedLaunchResult(
                Success: true,
                InstalledUtc: installedUtcString,
                LaunchedAppUserModelId: entry.AppUserModelId,
                FailureReason: LaunchFailureReason.None
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return new PackagedLaunchResult(
                Success: false,
                InstalledUtc: null,
                LaunchedAppUserModelId: null,
                FailureReason: LaunchFailureReason.LaunchFailed
            );
        }
    }
}
