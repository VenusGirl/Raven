using CommunityToolkit.Mvvm.ComponentModel;

namespace test.ViewModels;

public enum SearchType
{
    Url,
    ProductId,
    PackageFamilyName,
}

public partial class Advanced_SearchViewModel : ObservableRecipient
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceholderText))]
    [NotifyPropertyChangedFor(nameof(SelectedSearchType))]
    private int _selectedTypeIndex;

    public SearchType SelectedSearchType => SelectedTypeIndex switch
    {
        1 => SearchType.ProductId,
        2 => SearchType.PackageFamilyName,
        _ => SearchType.Url,
    };

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _showInfoCards = true;

    public string PlaceholderText => SelectedSearchType switch
    {
        SearchType.Url => "https://apps.microsoft.com/detail/...",
        SearchType.ProductId => "e.g. 9WZDNCRFHVJL",
        SearchType.PackageFamilyName => "e.g. Microsoft.WindowsTerminal_8wekyb3d8bbwe",
        _ => "Enter a value to search",
    };

    public Advanced_SearchViewModel()
    {
    }
}
