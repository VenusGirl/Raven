using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using test.Contracts.Services;
using test.Models;
using test.Services;

namespace test.ViewModels;

public partial class UpdatesViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public DispatcherQueue? DispatcherQueue { get; set; }

    [ObservableProperty]
    private ObservableCollection<UpdateItem> _availableUpdates = [];

    [ObservableProperty]
    private ObservableCollection<UpdateItem> _completedUpdates = [];

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isUpdating;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _checkingProgressText = string.Empty;

    [ObservableProperty]
    private double _checkingProgress;

    public bool HasUpdates => AvailableUpdates.Count > 0;
    public bool HasCompletedUpdates => CompletedUpdates.Count > 0;
    public bool ShowEmptyState => !HasUpdates && !IsChecking && !IsUpdating;
    public bool IsAllSelected =>
        AvailableUpdates.Count > 0 && AvailableUpdates.All(x => x.IsSelected);

    public string ButtonText =>
        IsChecking ? "Checking..."
        : IsUpdating ? "Updating..."
        : SelectedCount > 0 ? $"Update selected ({SelectedCount})"
        : "Check for updates";

    public bool ButtonEnabled => !IsChecking && !IsUpdating;

    private CancellationTokenSource? _checkCts;
    private CancellationTokenSource? _updateCts;

    private static readonly string CompletedUpdatesPath = Path.Combine(
        Path.GetTempPath(),
        "raven_completed_updates.json"
    );

    public UpdatesViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;

        AvailableUpdates.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasUpdates));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(IsAllSelected));
        };

        CompletedUpdates.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCompletedUpdates));
        };
    }

    partial void OnIsCheckingChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(ButtonEnabled));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnIsUpdatingChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(ButtonEnabled));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ButtonText));
    }

    [RelayCommand]
    private async Task CheckForUpdatesOrUpdate()
    {
        if (SelectedCount > 0 && !IsChecking)
            await UpdateSelectedAsync();
        else if (!IsUpdating)
            await CheckForUpdatesAsync();
    }

    public void CancelCheck() => _checkCts?.Cancel();

    [RelayCommand]
    private void ToggleSelectAll()
    {
        bool allSelected = IsAllSelected;
        foreach (var item in AvailableUpdates)
            item.IsSelected = !allSelected;
        RecalculateSelectedCount();
    }

    private async Task CheckForUpdatesAsync()
    {
        Debug.WriteLine("[Updates] CheckForUpdatesAsync START");
        IsChecking = true;

        foreach (var item in AvailableUpdates)
            item.PropertyChanged -= OnUpdateItemPropertyChanged;
        AvailableUpdates.Clear();

        _checkCts?.Cancel();
        _checkCts = new CancellationTokenSource();

        LoadCompletedUpdates();

        var progress = new Progress<(int completed, int total)>(p =>
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                CheckingProgressText = $"Checking ({p.completed}/{p.total})";
                CheckingProgress = p.total > 0 ? (double)p.completed / p.total * 100.0 : 0;
            });
        });

        List<UpdateItem> updates = [];

        try
        {
            updates = await UpdateCheckService.CheckForUpdatesAsync(progress, _checkCts.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Updates] Check cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Updates] Check EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            for (int i = 0; i < updates.Count; i++)
            {
                var item = updates[i];
                AvailableUpdates.Add(item);
                item.PropertyChanged += OnUpdateItemPropertyChanged;
            }

            IsChecking = false;
            CheckingProgressText = string.Empty;
            RecalculateSelectedCount();
            OnPropertyChanged(nameof(HasUpdates));
            OnPropertyChanged(nameof(ShowEmptyState));
        });
    }

    private async Task UpdateSelectedAsync()
    {
        IsUpdating = true;

        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();

        var selected = AvailableUpdates.Where(x => x.IsSelected).ToList();

        try
        {
            await UpdateCheckService.UpdateAppsAsync(
                selected,
                DispatcherQueue!,
                _updateCts.Token,
                OnUpdateItemCompleted
            );
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            IsUpdating = false;
            RecalculateSelectedCount();
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    }

    private void OnUpdateItemCompleted(UpdateItem item)
    {
        DownloadManagerService.Instance.RunOnUIThread(() =>
        {
            item.PropertyChanged -= OnUpdateItemPropertyChanged;
            AvailableUpdates.Remove(item);
            CompletedUpdates.Insert(0, item);
            PersistCompletedUpdates();
            OnPropertyChanged(nameof(HasUpdates));
            OnPropertyChanged(nameof(HasCompletedUpdates));
            OnPropertyChanged(nameof(ShowEmptyState));
        });
    }

    private void OnUpdateItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateItem.IsSelected))
            RecalculateSelectedCount();
    }

    private void RecalculateSelectedCount()
    {
        SelectedCount = AvailableUpdates.Count(x => x.IsSelected);
        OnPropertyChanged(nameof(IsAllSelected));
    }

    private void LoadCompletedUpdates()
    {
        CompletedUpdates.Clear();

        try
        {
            if (!File.Exists(CompletedUpdatesPath))
                return;

            var processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime;
            var fileTime = File.GetLastWriteTime(CompletedUpdatesPath);
            if (fileTime < processStart)
            {
                File.Delete(CompletedUpdatesPath);
                return;
            }

            var json = File.ReadAllText(CompletedUpdatesPath);
            var entries = JsonSerializer.Deserialize<List<CompletedUpdateEntry>>(json);
            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                CompletedUpdates.Add(
                    new UpdateItem
                    {
                        ProductId = entry.ProductId,
                        Title = entry.Title,
                        LogoUrl = entry.LogoUrl,
                        InstalledVersion = entry.InstalledVersion,
                        StoreVersion = entry.StoreVersion,
                        Status = DownloadStatus.Completed,
                    }
                );
            }
        }
        catch { }
    }

    private void PersistCompletedUpdates()
    {
        try
        {
            var entries = CompletedUpdates
                .Select(i => new CompletedUpdateEntry
                {
                    ProductId = i.ProductId,
                    Title = i.Title,
                    LogoUrl = i.LogoUrl,
                    InstalledVersion = i.InstalledVersion,
                    StoreVersion = i.StoreVersion,
                })
                .ToList();

            var json = JsonSerializer.Serialize(entries);
            File.WriteAllText(CompletedUpdatesPath, json);
        }
        catch { }
    }

    private sealed class CompletedUpdateEntry
    {
        public string ProductId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string InstalledVersion { get; set; } = string.Empty;
        public string StoreVersion { get; set; } = string.Empty;
    }
}
