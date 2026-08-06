using System.Collections.ObjectModel;
using System.Windows.Input;
using CloudKeeperSN.App.Services;
using CloudKeeperSN.Application.Storage;
using CloudKeeperSN.Domain.Storage;

namespace CloudKeeperSN.App.ViewModels;

public sealed record FolderEntryViewModel(string ItemId, string Name, string IconGlyph = "\uE8B7");
public sealed record BreadcrumbItemViewModel(string ItemId, string Name, string DisplayPath);

public sealed class FolderPickerViewModel : ObservableObject, IDisposable
{
    private readonly FolderPickerRequest _request;
    private readonly IStorageBrowserCapability _browser;
    private readonly IStorageFolderWriteCapability? _folderWriter;
    private readonly List<BreadcrumbItemViewModel> _breadcrumbs = [];
    private bool _isLoading;
    private string? _errorMessage;
    private string _newFolderName = string.Empty;
    private string _currentFolderId;

    public FolderPickerViewModel(FolderPickerRequest request, IStorageBrowserCapability browser, IStorageFolderWriteCapability? folderWriter)
    {
        _request = request;
        _browser = browser;
        _folderWriter = folderWriter;
        _currentFolderId = request.RootFolderId;
        _breadcrumbs.Add(new BreadcrumbItemViewModel(request.RootFolderId, request.ProviderId == "google-drive" ? "Google Drive" : "OneDrive", request.ProviderId == "google-drive" ? "Google Drive" : "OneDrive"));
        OpenFolderCommand = new AsyncParameterRelayCommand<FolderEntryViewModel>(OpenFolderAsync);
        GoUpCommand = new AsyncRelayCommand(GoUpAsync, () => _breadcrumbs.Count > 1 && !IsLoading);
        RetryCommand = new AsyncRelayCommand(LoadCurrentAsync, () => !IsLoading);
        CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync, () => CanCreateFolder && !string.IsNullOrWhiteSpace(NewFolderName) && !IsLoading);
    }

    public string Title => _request.Title;
    public bool CanCreateFolder => _request.CanCreateFolder && _folderWriter is not null;
    public ObservableCollection<FolderEntryViewModel> Folders { get; } = [];
    public ICommand OpenFolderCommand { get; }
    public ICommand GoUpCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand CreateFolderCommand { get; }
    public string CurrentPath => _breadcrumbs[^1].DisplayPath;
    public bool IsEmpty => !IsLoading && ErrorMessage is null && Folders.Count == 0;
    public FolderSelection SelectedFolder => new(_request.ProviderId, _request.AccountId, _currentFolderId, CurrentPath);

    private FolderEntryViewModel? _selectedEntry;
    public FolderEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!SetProperty(ref _selectedEntry, value)) return;
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
            NotifyCommands();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string NewFolderName
    {
        get => _newFolderName;
        set
        {
            if (!SetProperty(ref _newFolderName, value)) return;
            ((AsyncRelayCommand)CreateFolderCommand).NotifyCanExecuteChanged();
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken) => LoadCurrentAsync(cancellationToken);

    private async Task OpenFolderAsync(FolderEntryViewModel entry, CancellationToken cancellationToken)
    {
        var path = $"{CurrentPath} / {entry.Name}";
        _breadcrumbs.Add(new BreadcrumbItemViewModel(entry.ItemId, entry.Name, path));
        _currentFolderId = entry.ItemId;
        OnPropertyChanged(nameof(CurrentPath));
        ((AsyncRelayCommand)GoUpCommand).NotifyCanExecuteChanged();
        await LoadCurrentAsync(cancellationToken);
    }

    private async Task GoUpAsync(CancellationToken cancellationToken)
    {
        if (_breadcrumbs.Count <= 1) return;
        _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);
        _currentFolderId = _breadcrumbs[^1].ItemId;
        OnPropertyChanged(nameof(CurrentPath));
        ((AsyncRelayCommand)GoUpCommand).NotifyCanExecuteChanged();
        await LoadCurrentAsync(cancellationToken);
    }

    private async Task LoadCurrentAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        Folders.Clear();
        SelectedEntry = null;
        try
        {
            await foreach (var item in _browser.GetChildrenAsync(_request.AccountId, _currentFolderId, cancellationToken))
            {
                if (item.Kind == StorageItemKind.Folder) Folders.Add(new FolderEntryViewModel(item.ItemId, item.Name));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = "Không thể tải thư mục. Dữ liệu đám mây vẫn an toàn. Vui lòng thử lại.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task CreateFolderAsync(CancellationToken cancellationToken)
    {
        if (_folderWriter is null || string.IsNullOrWhiteSpace(NewFolderName)) return;
        IsLoading = true;
        try
        {
            await _folderWriter.CreateFolderAsync(_request.AccountId, _currentFolderId, NewFolderName.Trim(), cancellationToken);
            NewFolderName = string.Empty;
            await LoadCurrentAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = "Không thể tạo thư mục với tên này. Không có tệp hiện có nào bị thay đổi.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)GoUpCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RetryCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CreateFolderCommand).NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        ((AsyncParameterRelayCommand<FolderEntryViewModel>)OpenFolderCommand).Dispose();
        ((AsyncRelayCommand)GoUpCommand).Dispose();
        ((AsyncRelayCommand)RetryCommand).Dispose();
        ((AsyncRelayCommand)CreateFolderCommand).Dispose();
    }
}
