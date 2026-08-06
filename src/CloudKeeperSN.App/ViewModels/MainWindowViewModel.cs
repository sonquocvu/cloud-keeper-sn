using System.Collections.ObjectModel;
using System.Windows.Input;

namespace CloudKeeperSN.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IReadOnlyDictionary<string, PageViewModel> _pages;
    private PageViewModel _currentPage;
    private string _globalStatus = "Sẵn sàng bảo vệ dữ liệu đám mây.";

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        AccountsViewModel accounts,
        BackupViewModel backup,
        HistoryViewModel history,
        SettingsViewModel settings)
    {
        _pages = new PageViewModel[] { dashboard, accounts, backup, history, settings }
            .ToDictionary(page => page.Key, StringComparer.Ordinal);
        _currentPage = dashboard;
        NavigationItems =
        [
            new("dashboard", "Tổng quan", "\uE80F", "Mở trang Tổng quan"),
            new("accounts", "Tài khoản", "\uE77B", "Mở trang Tài khoản"),
            new("backup", "Sao lưu", "\uE753", "Mở trang Sao lưu một chiều"),
            new("history", "Lịch sử", "\uE81C", "Mở trang Lịch sử"),
            new("settings", "Cài đặt", "\uE713", "Mở trang Cài đặt")
        ];
        NavigateCommand = new RelayCommand(Navigate);
        dashboard.CreateBackupRequested += (_, _) => NavigateTo("backup");
        backup.OpenHistoryRequested += (_, _) => NavigateTo("history");
        SelectNavigation("dashboard");
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public ICommand NavigateCommand { get; }
    public bool IsDemoMode { get; init; }
    public string EnvironmentLabel => IsDemoMode ? "Chế độ trình diễn" : "Dữ liệu cục bộ";
    public string VersionLabel => "Phiên bản 0.2.0";

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(CurrentPageTitle));
            OnPropertyChanged(nameof(CurrentPageSubtitle));
        }
    }

    public string CurrentPageTitle => CurrentPage.Title;
    public string CurrentPageSubtitle => CurrentPage.Subtitle;
    public string GlobalStatus { get => _globalStatus; set => SetProperty(ref _globalStatus, value); }

    public void NavigateTo(string key) => Navigate(key);

    public async Task NavigateToAsync(string key, CancellationToken cancellationToken)
    {
        Navigate(key);
        await CurrentPage.LoadAsync(cancellationToken);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var page in _pages.Values) await page.LoadAsync(cancellationToken);
    }

    private void Navigate(object? parameter)
    {
        var key = parameter switch
        {
            NavigationItemViewModel item => item.Key,
            string value => value,
            _ => "dashboard"
        };
        if (!_pages.TryGetValue(key, out var page)) return;
        CurrentPage = page;
        SelectNavigation(key);
        _ = RefreshPageAsync(page);
    }

    private void SelectNavigation(string key)
    {
        foreach (var item in NavigationItems) item.IsSelected = item.Key == key;
    }

    private async Task RefreshPageAsync(PageViewModel page)
    {
        try { await page.LoadAsync(CancellationToken.None); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GlobalStatus = "Không thể làm mới trang. Dữ liệu đám mây không bị thay đổi; vui lòng thử lại.";
        }
    }
}
