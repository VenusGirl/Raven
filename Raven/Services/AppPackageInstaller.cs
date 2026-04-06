using Microsoft.Extensions.Logging;
using Windows.Management.Deployment;

namespace Raven.Services;

public static class AppPackageInstaller
{
    public sealed record InstallProgress(int Percent, string? State, string? Activity);

    private static readonly string[] SupportedExtensions =
    [
        ".msix",
        ".appx",
        ".msixbundle",
        ".appxbundle",
    ];

    private static bool IsPackageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return SupportedExtensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AddPackageAsync(
        PackageManager packageManager,
        string packagePath,
        IProgress<InstallProgress>? progress,
        DeploymentOptions deploymentOptions,
        CancellationToken cancellationToken
    )
    {
        var packageUri = new Uri(packagePath);

        var deploymentOperation = packageManager.AddPackageAsync(
            packageUri,
            Array.Empty<Uri>(),
            deploymentOptions
        );

        deploymentOperation.Progress = (_, p) =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var percent = (int)Math.Clamp(p.percentage, 0, 100);
            progress?.Report(new InstallProgress(percent, p.state.ToString(), "Install"));
        };

        var result = await deploymentOperation.AsTask(cancellationToken);

        if (result.ErrorText is { Length: > 0 })
            throw new InvalidOperationException(result.ErrorText);
    }

    public static async Task InstallAsync(
        string packagePath,
        IEnumerable<string>? dependencyPackagePaths = null,
        IProgress<InstallProgress>? progress = null,
        bool ignoreVersion = false,
        CancellationToken cancellationToken = default,
        ILogger? logger = null
    )
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));

        if (!File.Exists(packagePath))
            throw new FileNotFoundException("Package file not found.", packagePath);

        var deps = (dependencyPackagePaths ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .Where(IsPackageFile)
            .ToList();

        progress?.Report(new InstallProgress(0, "Starting", "Install"));

        var packageManager = new PackageManager();

        var options = DeploymentOptions.ForceApplicationShutdown;
        if (ignoreVersion)
            options |= DeploymentOptions.ForceUpdateFromAnyVersion;

        try
        {
            await AddPackageAsync(
                packageManager,
                packagePath,
                progress,
                options,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            logger?.LogError(
                ex,
                "Main package install failed | Path={PackagePath} | IgnoreVersion={IgnoreVersion}",
                packagePath,
                ignoreVersion
            );
            throw;
        }

        foreach (var dep in deps)
        {
            try
            {
                await AddPackageAsync(packageManager, dep, progress: null, options, cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "Dependency package install failed and was ignored | Path={DependencyPath} | IgnoreVersion={IgnoreVersion}",
                    dep,
                    ignoreVersion
                );
            }
        }

        progress?.Report(new InstallProgress(100, "Completed", "Install"));
    }
}
