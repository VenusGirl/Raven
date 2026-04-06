using System.Diagnostics;
using Windows.Management.Deployment;

namespace Raven.Services;

public static class PackagedAppDiscovery
{
    public enum LaunchFailureReason
    {
        None = 0,
        PackageFamilyNameMissing,
        NotInstalled,
        NoAppEntries,
        LaunchFailed,
    }

    public static string? GetInstalledVersion(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
            return null;

        var pkg = FindPackageByFamilyName(packageFamilyName);
        var v = pkg?.Id?.Version;
        return v == null ? null : FormatVersion(v.Value);
    }

    public sealed record PackagedLaunchResult(
        bool Success,
        string? LaunchedAppUserModelId,
        LaunchFailureReason FailureReason
    );

    public static bool IsInstalled(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
            return false;

        return FindPackageByFamilyName(packageFamilyName) != null;
    }

    public static List<(string PackageFamilyName, string InstalledVersion, string DisplayName)>
        GetAllInstalledStoreApps()
    {
        var pm = new PackageManager();
        var results = new List<(string PackageFamilyName, string InstalledVersion, string DisplayName)>();

        IEnumerable<Windows.ApplicationModel.Package>? packages = null;
        try
        {
            packages = pm.FindPackagesForUser(string.Empty);
        }
        catch
        {
            return results;
        }

        foreach (var pkg in packages)
        {
            try
            {
                if (pkg.IsFramework) continue;
                if (pkg.IsResourcePackage) continue;
                if (pkg.SignatureKind != Windows.ApplicationModel.PackageSignatureKind.Store) continue;

                var pfn = pkg.Id?.FamilyName;
                if (string.IsNullOrWhiteSpace(pfn)) continue;

                var v = pkg.Id?.Version;
                var versionStr = v == null ? "0.0.0.0" : FormatVersion(v.Value);

                string displayName;
                try { displayName = pkg.DisplayName; }
                catch { displayName = pkg.Id?.Name ?? pfn; }

                results.Add((pfn, versionStr, displayName));
            }
            catch
            {
                continue;
            }
        }

        return results
            .GroupBy(r => r.PackageFamilyName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.InstalledVersion).First())
            .ToList();
    }

    public static async Task<PackagedLaunchResult> TryLaunchDetailedAsync(string? packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName))
        {
            return new PackagedLaunchResult(
                Success: false,
                LaunchedAppUserModelId: null,
                FailureReason: LaunchFailureReason.PackageFamilyNameMissing
            );
        }

        try
        {
            var pkg = FindPackageByFamilyName(packageFamilyName);
            if (pkg == null)
            {
                return new PackagedLaunchResult(
                    Success: false,
                    LaunchedAppUserModelId: null,
                    FailureReason: LaunchFailureReason.NotInstalled
                );
            }

            var entries = await pkg.GetAppListEntriesAsync();
            var entry = entries.FirstOrDefault();
            if (entry == null)
            {
                return new PackagedLaunchResult(
                    Success: false,
                    LaunchedAppUserModelId: null,
                    FailureReason: LaunchFailureReason.NoAppEntries
                );
            }

            await entry.LaunchAsync();
            return new PackagedLaunchResult(
                Success: true,
                LaunchedAppUserModelId: entry.AppUserModelId,
                FailureReason: LaunchFailureReason.None
            );
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return new PackagedLaunchResult(
                Success: false,
                LaunchedAppUserModelId: null,
                FailureReason: LaunchFailureReason.LaunchFailed
            );
        }
    }

    private static Windows.ApplicationModel.Package? FindPackageByFamilyName(string packageFamilyName)
    {
        try
        {
            var pm = new PackageManager();
            return pm.FindPackagesForUser(string.Empty, packageFamilyName).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string FormatVersion(Windows.ApplicationModel.PackageVersion version) =>
        $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
}
