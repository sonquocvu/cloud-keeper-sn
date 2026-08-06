using System.IO;

namespace CloudKeeperSN.App.Services;

public interface ILocalDataService
{
    string DatabasePath { get; }
    string LogPath { get; }
    string CachePath { get; }
    Task ClearCacheAsync(CancellationToken cancellationToken);
}

public sealed class LocalDataService : ILocalDataService
{
    private readonly string _applicationDataPath;

    public LocalDataService()
    {
        _applicationDataPath = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CloudKeeperSN"));
        DatabasePath = Path.Combine(_applicationDataPath, "cloudkeeper.db");
        LogPath = Path.Combine(_applicationDataPath, "Logs");
        CachePath = Path.Combine(_applicationDataPath, "Cache");
    }

    public string DatabasePath { get; }
    public string LogPath { get; }
    public string CachePath { get; }

    public Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = Path.GetFullPath(CachePath);
        if (!resolved.StartsWith(_applicationDataPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(resolved), "Cache", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn bộ nhớ tạm không hợp lệ.");
        }

        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        Directory.CreateDirectory(resolved);
        return Task.CompletedTask;
    }
}
