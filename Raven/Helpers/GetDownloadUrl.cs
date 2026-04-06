using System.Diagnostics;
using StoreListings.Library;
using Raven.Models;

namespace Raven.Helpers;

public static class GetDownloadUrl
{
    private static bool IsLikelyResourceOnlyPackage(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var n = fileName;
        return n.Contains("language-", StringComparison.OrdinalIgnoreCase)
            || n.Contains("_language", StringComparison.OrdinalIgnoreCase)
            || n.Contains("scale-", StringComparison.OrdinalIgnoreCase)
            || n.Contains("_scale", StringComparison.OrdinalIgnoreCase)
            || n.Contains("localization", StringComparison.OrdinalIgnoreCase)
            || n.Contains(".resources", StringComparison.OrdinalIgnoreCase)
            || n.Contains("resources", StringComparison.OrdinalIgnoreCase)
            || n.Contains("resource", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(
        FE3Handler.SyncUpdatesResponse.Update Update,
        string Url
    )> ReduceFrameworkDependencyFiles(
        IReadOnlyList<(
            FE3Handler.SyncUpdatesResponse.Update Update,
            string Url
        )> latestVersionGroup,
        string archRid
    )
    {
        if (latestVersionGroup.Count == 0)
            return latestVersionGroup;

        // Prefer non-resource packages.
        var nonResource = latestVersionGroup
            .Where(a => !IsLikelyResourceOnlyPackage(a.Update.FileName))
            .ToList();

        var candidates = nonResource.Count > 0 ? nonResource : latestVersionGroup.ToList();
        var priorities = Utils.GetArchPriorities(archRid, isPackaged: true);

        foreach (var pref in priorities)
        {
            var matches = candidates
                .Where(a =>
                    Utils.ParseArchString(
                        a.Update.FileName ?? a.Update.PackageIdentityName,
                        isPackaged: true
                    ) == pref
                )
                .ToList();

            if (matches.Any())
            {
                // If multiple remain, pick the shortest filename to avoid edge-case variants
                var best = matches.OrderBy(a => (a.Update.FileName ?? string.Empty).Length).First();
                return new[] { best };
            }
        }

        return new[] { candidates.First() };
    }

    public static async Task<FileEntry?> fetch(
        string productId,
        InstallerType installerType,
        CancellationToken cancellationToken = default,
        DeviceFamily deviceFamily = DeviceFamily.Desktop,
        Market market = Market.US,
        Lang language = Lang.en,
        string flightRing = "Retail",
        string flightingBranchName = "Retail",
        string currentBranch = "ge_release",
        bool ignoreDependencyFilter = false
    )
    {
        var OSVersion = SystemInfo.GetExactWindowsVersion();
        var archRid = SystemInfo.GetOsArchRid();
        Debug.WriteLine($"{installerType}, {OSVersion}, {archRid}, {productId}");
        switch (installerType)
        {
            case InstallerType.Packaged:
            {
                var selectionContext = await VersionCheckService.GetPackagedSelectionContextAsync(
                    productId,
                    cancellationToken,
                    prefetchedPackages: null,
                    deviceFamily,
                    market,
                    language,
                    flightRing,
                    flightingBranchName,
                    currentBranch,
                    OSVersion,
                    archRid
                );

                if (selectionContext is null)
                    return null;

                var packageList = selectionContext.Packages;
                var updates = selectionContext.Updates;
                var priorities = Utils.GetArchPriorities(selectionContext.ArchRid, isPackaged: true);

                foreach (var archPref in priorities)
                {
                    var archCandidates = selectionContext.Candidates.Where(c =>
                        Utils.ParseArchString(c.FileName ?? c.PackageIdentityName, isPackaged: true)
                        == archPref
                    );

                    foreach (var main in archCandidates)
                    {
                        // --- 1. SELECTION ---
                        var dcatMain = packageList.FirstOrDefault(p =>
                            p.PackageIdentityName.Equals(
                                main.PackageIdentityName,
                                StringComparison.OrdinalIgnoreCase
                            )
                            && (
                                p.AppVersion is null
                                || p.AppVersion.ToString() == main.Version.ToString()
                            )
                        );

                        var requiredDepUpdates = new List<FE3Handler.SyncUpdatesResponse.Update>();
                        var allDepsOk = true;

                        if (dcatMain is not null && dcatMain.FrameworkDependencies?.Any() == true)
                        {
                            foreach (var dep in dcatMain.FrameworkDependencies)
                            {
                                var applicable = updates
                                    .Where(d =>
                                        d.PackageIdentityName.Equals(
                                            dep.PackageIdentity,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                        && d.Version >= dep.MinVersion
                                        && d.TargetPlatforms.Any(tp =>
                                            tp.MinVersion <= selectionContext.OsVersion
                                            && (
                                                tp.Family == DeviceFamily.Universal
                                                || tp.Family == deviceFamily
                                            )
                                        )
                                    )
                                    .ToList();

                                if (!applicable.Any())
                                {
                                    allDepsOk = false;
                                    break;
                                }

                                var latestGroup = applicable
                                    .GroupBy(a => a.Version)
                                    .OrderByDescending(g => g.Key)
                                    .First()
                                    .ToList();

                                if (ignoreDependencyFilter)
                                {
                                    requiredDepUpdates.AddRange(latestGroup);
                                }
                                else
                                {
                                    var reduced = ReduceFrameworkDependencyFiles(
                                        latestGroup
                                            .Select(u => (Update: u, Url: string.Empty))
                                            .ToList(),
                                        selectionContext.ArchRid
                                    );

                                    requiredDepUpdates.AddRange(reduced.Select(r => r.Update));
                                }
                            }
                        }

                        if (!allDepsOk)
                            continue;

                        // --- 2. FETCH URLs ---
                        var mainDownloadInfo = await FE3Handler.GetPackageDownloadInfo(
                            selectionContext.NewCookie,
                            main.UpdateID,
                            main.RevisionNumber,
                            main.Digest,
                            language,
                            market,
                            currentBranch,
                            flightRing,
                            flightingBranchName,
                            selectionContext.OsVersion,
                            deviceFamily,
                            cancellationToken,
                            selectionContext.OsArch
                        );

                        if (!mainDownloadInfo.IsSuccess)
                            continue;

                        var depEntries = new List<FileEntry>();
                        var networkDepsOk = true;

                        foreach (var depUpdate in requiredDepUpdates.Distinct())
                        {
                            var depDownloadInfo = await FE3Handler.GetPackageDownloadInfo(
                                selectionContext.NewCookie,
                                depUpdate.UpdateID,
                                depUpdate.RevisionNumber,
                                depUpdate.Digest,
                                language,
                                market,
                                currentBranch,
                                flightRing,
                                flightingBranchName,
                                selectionContext.OsVersion,
                                deviceFamily,
                                cancellationToken,
                                selectionContext.OsArch
                            );

                            if (!depDownloadInfo.IsSuccess)
                            {
                                networkDepsOk = false;
                                break;
                            }

                            depUpdate.SetDownloadInfoPackageDigest(
                                depDownloadInfo.Value.Package.Digest
                            );
                            depUpdate.SetDownloadInfoBlockmapUrl(
                                depDownloadInfo.Value.BlockmapCab?.Url
                            );
                            depUpdate.SetDownloadInfoBlockmapDigest(
                                depDownloadInfo.Value.BlockmapCab?.Digest
                            );

                            depEntries.Add(
                                new FileEntry(
                                    FileName: depUpdate.FileName,
                                    Url: depDownloadInfo.Value.Package.Url,
                                    Dependencies: Array.Empty<FileEntry>(),
                                    Digest: depUpdate.GetDownloadInfoPackageDigest(),
                                    BlockmapUrl: depUpdate.GetDownloadInfoBlockmapUrl(),
                                    BlockmapCabFileDigest: depUpdate.GetDownloadInfoBlockmapDigest()
                                )
                            );
                        }

                        if (!networkDepsOk)
                            continue; // Dependency URL failed, fallback to next main candidate

                        // --- 3. ASSEMBLY ---
                        main.SetDownloadInfoPackageDigest(mainDownloadInfo.Value.Package.Digest);
                        main.SetDownloadInfoBlockmapUrl(mainDownloadInfo.Value.BlockmapCab?.Url);
                        main.SetDownloadInfoBlockmapDigest(
                            mainDownloadInfo.Value.BlockmapCab?.Digest
                        );

                        return new FileEntry(
                            FileName: main.FileName,
                            Url: mainDownloadInfo.Value.Package.Url,
                            Dependencies: depEntries,
                            Digest: main.GetDownloadInfoPackageDigest(),
                            BlockmapUrl: main.GetDownloadInfoBlockmapUrl(),
                            BlockmapCabFileDigest: main.GetDownloadInfoBlockmapDigest()
                        );
                    }
                }
                return null;
            }
            case InstallerType.Unpackaged:
            {
                var selectionContext = await VersionCheckService.GetUnpackagedSelectionContextAsync(
                    productId,
                    cancellationToken,
                    market,
                    language,
                    archRid
                );

                if (selectionContext is null)
                    return null;

                return new FileEntry(
                    FileName: selectionContext.FileName,
                    Url: selectionContext.InstallerUrl,
                    Dependencies: Array.Empty<FileEntry>(),
                    Sha256: selectionContext.InstallerSha256
                );
            }

            default:
                return null;
        }
    }
}
