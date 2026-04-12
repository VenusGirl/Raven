using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Raven.Contracts.Services;
using Raven.Helpers;
using Raven.Views.Dialogs;

namespace Raven.Services;

public sealed class AppUpdatePromptService
{
    private const string CheckUpdatesOnStartupKey = "CheckForAppUpdatesOnStartup";

    private readonly GitHubUpdaterService _gitHubUpdaterService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;

    public AppUpdatePromptService(
        GitHubUpdaterService gitHubUpdaterService,
        ILocalSettingsService localSettingsService,
        ILoggerFactory loggerFactory
    )
    {
        _gitHubUpdaterService = gitHubUpdaterService;
        _localSettingsService = localSettingsService;
        _logger = loggerFactory.CreateLogger("Raven.Runtime");
    }

    public async Task ShowManualUpdateDialogAsync(
        XamlRoot xamlRoot,
        CancellationToken cancellationToken = default
    )
    {
        var dialogContent = new UpdateDialogContent();
        dialogContent.StartupPreferenceCheckBoxControl.IsChecked = await GetCheckOnStartupEnabledAsync();

        var messageTextBlock = dialogContent.StatusMessageTextBlockControl;
        var checkOnStartup = dialogContent.StartupPreferenceCheckBoxControl;
        var progressBar = dialogContent.StatusProgressBarControl;
        var progressText = dialogContent.StatusProgressTextBlockControl;

        var closeLabel = "Settings_UpdaterDialogClose".GetLocalized();
        var cancelLabel = "Settings_UpdaterDialogCancel".GetLocalized();
        var checkForUpdatesLabel = "Settings_AppUpdatesButton.Content".GetLocalized();

        if (string.IsNullOrWhiteSpace(checkForUpdatesLabel))
        {
            checkForUpdatesLabel = "Check for updates";
        }

        var dialog = new ContentDialog
        {
            Content = dialogContent,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        var statusAnimationService = new UIUpdateService(dialogContent.DispatcherQueue);
        void OnStatusAnimationPropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs e
        )
        {
            if (e.PropertyName == nameof(UIUpdateService.StatusText))
            {
                messageTextBlock.Text = statusAnimationService.StatusText;
            }
        }

        statusAnimationService.PropertyChanged += OnStatusAnimationPropertyChanged;

        static string NormalizeAnimatedBase(string text) => text.TrimEnd(' ', '.', '…');

        void StartStatusMessageAnimation(string baseText)
        {
            var normalized = NormalizeAnimatedBase(baseText);
            statusAnimationService.StartStatusAnimation(
                string.IsNullOrWhiteSpace(normalized) ? baseText : normalized
            );
        }

        void StopStatusMessageAnimation(string? message = null)
        {
            statusAnimationService.StopStatusAnimation();
            if (message is not null)
            {
                messageTextBlock.Text = message;
            }
        }

        var hasPersistedStartupPreference = false;
        var isChecking = false;
        var isDownloading = false;
        var isClosing = false;
        GitHubReleaseInfo? availableRelease = null;
        CancellationTokenSource? checkCts = null;
        CancellationTokenSource? downloadCts = null;

        async Task StartManualCheckAsync()
        {
            checkCts?.Cancel();
            checkCts?.Dispose();
            checkCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            availableRelease = null;
            isChecking = true;
            _logger.LogInformation("Update check started");

            dialog.Title = "Settings_UpdaterCheckingTitle".GetLocalized();
            StartStatusMessageAnimation("Settings_UpdaterCheckingContent".GetLocalized());

            checkOnStartup.Visibility = Visibility.Collapsed;
            checkOnStartup.IsEnabled = false;

            progressBar.IsIndeterminate = true;
            progressBar.Value = 0;
            progressBar.Visibility = Visibility.Visible;
            progressText.Visibility = Visibility.Collapsed;

            dialog.PrimaryButtonText = checkForUpdatesLabel;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = true;
            dialog.CloseButtonText = cancelLabel;

            try
            {
                var release = await _gitHubUpdaterService.GetLatestReleaseAsync(checkCts.Token);
                isChecking = false;

                if (release.IsUpToDate)
                {
                    _logger.LogInformation("Already on latest version: {CurrentVersion}", release.LatestVersion?.ToString() ?? release.TagName);
                    dialog.Title = "Settings_UpdaterAlreadyLatestTitle".GetLocalized();
                    SetCheckActionState("Settings_UpdaterAlreadyLatestContent".GetLocalized());
                    return;
                }

                availableRelease = release;
                _logger.LogInformation("Update available: version {LatestVersion}", release.LatestVersion?.ToString() ?? release.TagName);
                SetUpdateAvailableState(release);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                isChecking = false;
                _logger.LogInformation("Update check canceled by user");

                if (!isClosing)
                {
                    SetCheckActionState();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update check failed");
                isChecking = false;

                dialog.Title = "Settings_UpdaterErrorTitle".GetLocalized();
                StopStatusMessageAnimation(
                    string.Format("Settings_UpdaterErrorContent".GetLocalized(), ex.Message)
                );

                checkOnStartup.Visibility = Visibility.Visible;
                checkOnStartup.IsEnabled = true;

                progressBar.Visibility = Visibility.Collapsed;
                progressText.Visibility = Visibility.Collapsed;

                dialog.PrimaryButtonText = checkForUpdatesLabel;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
                dialog.CloseButtonText = closeLabel;
            }
        }

        void SetCheckActionState(string? messageOverride = null)
        {
            StopStatusMessageAnimation(
                messageOverride ?? "Settings_UpdaterCheckCanceledContent".GetLocalized()
            );

            checkOnStartup.Visibility = Visibility.Visible;
            checkOnStartup.IsEnabled = true;

            progressBar.Visibility = Visibility.Collapsed;
            progressText.Visibility = Visibility.Collapsed;
            progressBar.Value = 0;

            dialog.PrimaryButtonText = checkForUpdatesLabel;
            dialog.IsPrimaryButtonEnabled = true;
            dialog.IsSecondaryButtonEnabled = true;
            dialog.CloseButtonText = closeLabel;
        }

        void SetUpdateAvailableState(GitHubReleaseInfo release)
        {
            var latestLabel = release.LatestVersion?.ToString() ?? release.TagName;
            dialog.Title = "Settings_UpdaterConfirmTitle".GetLocalized();
            StopStatusMessageAnimation(
                string.Format("Settings_UpdaterConfirmContent".GetLocalized(), latestLabel)
            );

            checkOnStartup.Visibility = Visibility.Visible;
            checkOnStartup.IsEnabled = true;

            progressBar.Visibility = Visibility.Collapsed;
            progressText.Visibility = Visibility.Collapsed;

            dialog.PrimaryButtonText = "Settings_UpdaterConfirmPrimary".GetLocalized();
            dialog.IsPrimaryButtonEnabled = true;
            dialog.IsSecondaryButtonEnabled = true;
            dialog.CloseButtonText = closeLabel;
        }

        async Task StartManualUpdateAsync()
        {
            if (availableRelease is null)
                return;

            try
            {
                var enabled = checkOnStartup.IsChecked != false;
                await _localSettingsService.SaveSettingAsync(CheckUpdatesOnStartupKey, enabled);
                hasPersistedStartupPreference = true;

                isDownloading = true;
                downloadCts?.Cancel();
                downloadCts?.Dispose();
                downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _logger.LogInformation("Update installation started: version {Version}", availableRelease.LatestVersion?.ToString() ?? availableRelease.TagName);

                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressBar.Visibility = Visibility.Visible;
                progressText.Visibility = Visibility.Visible;

                checkOnStartup.Visibility = Visibility.Collapsed;
                checkOnStartup.IsEnabled = false;

                dialog.IsPrimaryButtonEnabled = false;
                dialog.IsSecondaryButtonEnabled = true;
                dialog.CloseButtonText = cancelLabel;

                dialog.Title = "Settings_UpdaterInstallingTitle".GetLocalized();
                StartStatusMessageAnimation("Status_Installing".GetLocalized());
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressText.Text = "0%";

                var progress = new Progress<double>(value =>
                {
                    var percent = (int)Math.Round(value * 100);
                    progressBar.Value = percent;
                    progressText.Text = string.Format(
                        "Settings_UpdaterProgressPercent".GetLocalized(),
                        percent
                    );
                });

                await _gitHubUpdaterService.StartUpdateAsync(availableRelease, progress, downloadCts.Token);
                Application.Current.Exit();
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                isDownloading = false;
                downloadCts?.Dispose();
                downloadCts = null;
                _logger.LogInformation("Update installation canceled by user");

                progressBar.Visibility = Visibility.Collapsed;
                progressText.Visibility = Visibility.Collapsed;
                progressBar.Value = 0;

                if (availableRelease is not null)
                {
                    SetUpdateAvailableState(availableRelease);
                }
                else
                {
                    SetCheckActionState();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update installation failed");

                isDownloading = false;
                downloadCts?.Dispose();
                downloadCts = null;

                dialog.Title = "Settings_UpdaterErrorTitle".GetLocalized();
                StopStatusMessageAnimation(
                    string.Format("Settings_UpdaterErrorContent".GetLocalized(), ex.Message)
                );

                checkOnStartup.Visibility = Visibility.Visible;
                checkOnStartup.IsEnabled = true;

                progressBar.Visibility = Visibility.Collapsed;
                progressText.Visibility = Visibility.Collapsed;

                dialog.PrimaryButtonText = checkForUpdatesLabel;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
                dialog.CloseButtonText = closeLabel;
                availableRelease = null;
            }
        }

        dialog.Closing += (_, args) =>
        {
            if (isDownloading)
            {
                args.Cancel = true;
                downloadCts?.Cancel();
                return;
            }

            isClosing = true;
            downloadCts?.Cancel();
            StopStatusMessageAnimation();
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;

            if (isChecking || isDownloading)
                return;

            if (availableRelease is null)
            {
                StartManualCheckAsync();
                return;
            }

            StartManualUpdateAsync();
        };

        var checkTask = StartManualCheckAsync();
        await dialog.ShowAsync();

        isClosing = true;
        checkCts?.Cancel();
        downloadCts?.Cancel();

        try
        {
            await checkTask;
        }
        catch
        {
            // Ignore exceptions from check task as dialog is closed
        }

        checkCts?.Dispose();
        downloadCts?.Dispose();
        statusAnimationService.StopStatusAnimation();
        statusAnimationService.PropertyChanged -= OnStatusAnimationPropertyChanged;

        if (!hasPersistedStartupPreference)
        {
            var isEnabled = checkOnStartup.IsChecked != false;
            await _localSettingsService.SaveSettingAsync(CheckUpdatesOnStartupKey, isEnabled);
        }
    }

    public async Task CheckForUpdatesOnStartupAsync(
        XamlRoot xamlRoot,
        CancellationToken cancellationToken = default
    )
    {
        var shouldCheck = await GetCheckOnStartupEnabledAsync();
        if (!shouldCheck)
            return;

        try
        {
            var release = await _gitHubUpdaterService.GetLatestReleaseAsync(cancellationToken);
            if (release.IsUpToDate)
                return;

            _logger.LogInformation("Update available on startup: version {Version}", release.LatestVersion?.ToString() ?? release.TagName);
            await ShowAvailableUpdateDialogAsync(release, xamlRoot, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup update check failed");
        }
    }

    private async Task ShowAvailableUpdateDialogAsync(
        GitHubReleaseInfo release,
        XamlRoot xamlRoot,
        CancellationToken cancellationToken
    )
    {
        var latestLabel = release.LatestVersion?.ToString() ?? release.TagName;
        var confirmMessage = string.Format(
            "Settings_UpdaterConfirmContent".GetLocalized(),
            latestLabel
        );

        var dialogContent = new UpdateDialogContent();
        dialogContent.StatusMessageTextBlockControl.Text = confirmMessage;
        dialogContent.StartupPreferenceCheckBoxControl.IsChecked = await GetCheckOnStartupEnabledAsync();

        var messageTextBlock = dialogContent.StatusMessageTextBlockControl;
        var checkOnStartup = dialogContent.StartupPreferenceCheckBoxControl;
        var progressBar = dialogContent.StatusProgressBarControl;
        var progressText = dialogContent.StatusProgressTextBlockControl;

        var closeLabel = "Settings_UpdaterDialogClose".GetLocalized();
        var cancelLabel = "Settings_UpdaterDialogCancel".GetLocalized();

        var dialog = new ContentDialog
        {
            Title = "Settings_UpdaterConfirmTitle".GetLocalized(),
            Content = dialogContent,
            PrimaryButtonText = "Settings_UpdaterConfirmPrimary".GetLocalized(),
            CloseButtonText = closeLabel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        var statusAnimationService = new UIUpdateService(dialogContent.DispatcherQueue);
        void OnStatusAnimationPropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs e
        )
        {
            if (e.PropertyName == nameof(UIUpdateService.StatusText))
            {
                messageTextBlock.Text = statusAnimationService.StatusText;
            }
        }

        statusAnimationService.PropertyChanged += OnStatusAnimationPropertyChanged;

        static string NormalizeAnimatedBase(string text) => text.TrimEnd(' ', '.', '…');

        void StartStatusMessageAnimation(string baseText)
        {
            var normalized = NormalizeAnimatedBase(baseText);
            statusAnimationService.StartStatusAnimation(
                string.IsNullOrWhiteSpace(normalized) ? baseText : normalized
            );
        }

        void StopStatusMessageAnimation(string? message = null)
        {
            statusAnimationService.StopStatusAnimation();
            if (message is not null)
            {
                messageTextBlock.Text = message;
            }
        }

        var hasPersistedStartupPreference = false;
        var isDownloading = false;
        var isClosing = false;
        CancellationTokenSource? downloadCts = null;

        async Task StartStartupUpdateAsync()
        {
            try
            {
                var enabled = checkOnStartup.IsChecked != false;
                await _localSettingsService.SaveSettingAsync(CheckUpdatesOnStartupKey, enabled);
                hasPersistedStartupPreference = true;

                isDownloading = true;
                downloadCts?.Cancel();
                downloadCts?.Dispose();
                downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                _logger.LogInformation("Update installation started (startup): version {Version}", release.LatestVersion?.ToString() ?? release.TagName);

                progressBar.Visibility = Visibility.Visible;
                progressText.Visibility = Visibility.Visible;
                checkOnStartup.Visibility = Visibility.Collapsed;
                checkOnStartup.IsEnabled = false;
                dialog.IsPrimaryButtonEnabled = false;
                dialog.IsSecondaryButtonEnabled = true;
                dialog.CloseButtonText = cancelLabel;

                dialog.Title = "Settings_UpdaterInstallingTitle".GetLocalized();
                StartStatusMessageAnimation("Status_Installing".GetLocalized());
                progressBar.IsIndeterminate = false;
                progressBar.Value = 0;
                progressText.Text = "0%";

                var progress = new Progress<double>(value =>
                {
                    var percent = (int)Math.Round(value * 100);
                    progressBar.Value = percent;
                    progressText.Text = string.Format(
                        "Settings_UpdaterProgressPercent".GetLocalized(),
                        percent
                    );
                });

                await _gitHubUpdaterService.StartUpdateAsync(release, progress, downloadCts.Token);
                Application.Current.Exit();
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                isDownloading = false;
                downloadCts?.Dispose();
                downloadCts = null;
                _logger.LogInformation("Update installation canceled by user (startup)");

                progressBar.Visibility = Visibility.Collapsed;
                progressText.Visibility = Visibility.Collapsed;
                progressBar.Value = 0;

                checkOnStartup.Visibility = Visibility.Visible;
                checkOnStartup.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
                dialog.CloseButtonText = closeLabel;
                StopStatusMessageAnimation(confirmMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update installation failed");

                isDownloading = false;
                downloadCts?.Dispose();
                downloadCts = null;

                dialog.Title = "Settings_UpdaterErrorTitle".GetLocalized();
                StopStatusMessageAnimation(
                    string.Format("Settings_UpdaterErrorContent".GetLocalized(), ex.Message)
                );

                checkOnStartup.Visibility = Visibility.Visible;
                checkOnStartup.IsEnabled = true;

                progressBar.Visibility = Visibility.Collapsed;
                progressText.Visibility = Visibility.Collapsed;

                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
                dialog.CloseButtonText = closeLabel;
            }
        }

        dialog.Closing += (_, args) =>
        {
            if (isDownloading)
            {
                args.Cancel = true;
                downloadCts?.Cancel();
                return;
            }

            isClosing = true;
            StopStatusMessageAnimation();
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;

            if (isDownloading)
                return;

            StartStartupUpdateAsync();
        };

        await dialog.ShowAsync();

        isClosing = true;
        downloadCts?.Cancel();
        downloadCts?.Dispose();
        statusAnimationService.StopStatusAnimation();
        statusAnimationService.PropertyChanged -= OnStatusAnimationPropertyChanged;

        if (!hasPersistedStartupPreference)
        {
            var isEnabled = checkOnStartup.IsChecked != false;
            await _localSettingsService.SaveSettingAsync(CheckUpdatesOnStartupKey, isEnabled);
        }
    }

    private async Task<bool> GetCheckOnStartupEnabledAsync()
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync<bool?>(CheckUpdatesOnStartupKey);
            return value ?? true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read startup check setting");
            return true;
        }
    }
}
