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
    private long _loadVersion;
    private bool _isDisposed;

    public FolderPickerViewModel(FolderPickerRequest request, IStorageBrowserCapability browser, IStorageFolderWriteCapability? folderWriter)
    {
        _request = request;
        _browser = browser;
        _folderWriter = folderWriter;
        _currentFolderId = request.RootFolderId;
        var rootName = request.ProviderId == "google-drive" ? "Drive của tôi" : "OneDrive";
        _breadcrumbs.Add(new BreadcrumbItemViewModel(request.RootFolderId, rootName, rootName));
        OpenFolderCommand = new AsyncParameterRelayCommand<FolderEntryViewModel>(OpenFolderAsync, _ => !IsLoading);
        GoUpCommand = new AsyncRelayCommand(GoUpAsync, () => _breadcrumbs.Count > 1 && !IsLoading);
        RetryCommand = new AsyncRelayCommand(LoadCurrentAsync, () => !IsLoading);
        CreateFolderCommand = new AsyncRelayCommand(CreateFolderAsync, () => CanCreateFolder && !string.IsNullOrWhiteSpace(NewFolderName) && !IsLoading);
        CancelLoadingCommand = new RelayCommand(_ => CancelActiveOperations(), _ => IsLoading);
    }

    public string Title => _request.Title;
    public bool CanCreateFolder => _request.CanCreateFolder && _folderWriter is not null;
    public ObservableCollection<FolderEntryViewModel> Folders { get; } = [];
    public ICommand OpenFolderCommand { get; }
    public ICommand GoUpCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand CreateFolderCommand { get; }
    public ICommand CancelLoadingCommand { get; }
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
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var version = Interlocked.Increment(ref _loadVersion);
        var folderId = _currentFolderId;
        IsLoading = true;
        ErrorMessage = null;
        Folders.Clear();
        SelectedEntry = null;
        try
        {
            var folders = new List<FolderEntryViewModel>();
            await foreach (var item in _browser.GetChildrenAsync(_request.AccountId, folderId, cancellationToken))
            {
                if (item.Kind == StorageItemKind.Folder) folders.Add(new FolderEntryViewModel(item.ItemId, item.Name));
            }
            if (version != Volatile.Read(ref _loadVersion) || _isDisposed) return;
            foreach (var folder in folders) Folders.Add(folder);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (version == Volatile.Read(ref _loadVersion) && !_isDisposed)
                ErrorMessage = exception is ProviderOperationException failure
                    ? ProviderFailureMessages.ToVietnamese(failure.Category)
                    : "Không thể tải thư mục. Dữ liệu đám mây vẫn an toàn và chưa bị thay đổi. Vui lòng thử lại.";
        }
        finally
        {
            if (version == Volatile.Read(ref _loadVersion) && !_isDisposed)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
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
        ((AsyncParameterRelayCommand<FolderEntryViewModel>)OpenFolderCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)GoUpCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RetryCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CreateFolderCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelLoadingCommand).RaiseCanExecuteChanged();
    }

    private void CancelActiveOperations()
    {
        ((AsyncParameterRelayCommand<FolderEntryViewModel>)OpenFolderCommand).Cancel();
        ((AsyncRelayCommand)GoUpCommand).Cancel();
        ((AsyncRelayCommand)RetryCommand).Cancel();
        ((AsyncRelayCommand)CreateFolderCommand).Cancel();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Interlocked.Increment(ref _loadVersion);
        CancelActiveOperations();
        ((AsyncParameterRelayCommand<FolderEntryViewModel>)OpenFolderCommand).Dispose();
        ((AsyncRelayCommand)GoUpCommand).Dispose();
        ((AsyncRelayCommand)RetryCommand).Dispose();
        ((AsyncRelayCommand)CreateFolderCommand).Dispose();
    }
}
