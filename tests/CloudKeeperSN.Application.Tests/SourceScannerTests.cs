using CloudKeeperSN.Application.Scanning;
using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Providers.GoogleDrive.Fakes;

namespace CloudKeeperSN.Application.Tests;

public sealed class SourceScannerTests
{
    [Fact]
    public async Task Scan_PreservesDuplicateNamesByProviderIdentity()
    {
        var provider = new FakeGoogleDriveProvider();
        provider.Connect();
        provider.AddItem("root", File("id-a", "Báo cáo.pdf", 10));
        provider.AddItem("root", File("id-b", "Báo cáo.pdf", 20));
        var scanner = new SourceScanner(provider);

        var result = await scanner.ScanAsync("fake-google-account", "root", CancellationToken.None);

        Assert.Equal(2, result.FileCount);
        Assert.Equal(["id-a", "id-b"], result.Items.Select(item => item.Item.ItemId));
        Assert.All(result.Items, item => Assert.Equal("Báo cáo.pdf", item.RelativePath.ToString()));
    }

    [Fact]
    public async Task Scan_StopsFolderCyclesAndSkipsShortcuts()
    {
        var provider = new FakeGoogleDriveProvider();
        provider.Connect();
        provider.AddItem("root", Folder("loop", "Thư mục vòng"));
        provider.AddItem("loop", Folder("loop", "Quay lại"));
        provider.AddItem("root", new StorageItem
        {
            ProviderId = "google-drive",
            ProviderAccountId = "fake-google-account",
            ItemId = "shortcut",
            ParentItemId = "root",
            Name = "Lối tắt",
            Kind = StorageItemKind.Shortcut,
            MimeType = GoogleNativeExportPolicy.GoogleShortcut
        });
        var scanner = new SourceScanner(provider);

        var result = await scanner.ScanAsync("fake-google-account", "root", CancellationToken.None);

        Assert.Equal(2, result.FolderCount);
        Assert.Equal(2, result.VietnameseWarnings.Count);
    }

    [Fact]
    public async Task Scan_ObservesCancellation()
    {
        var provider = new FakeGoogleDriveProvider();
        provider.Connect();
        provider.AddItem("root", File("id", "tệp.txt", 1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new SourceScanner(provider).ScanAsync("fake-google-account", "root", cancellation.Token));
    }

    private static StorageItem File(string id, string name, long size) => new()
    {
        ProviderId = "google-drive",
        ProviderAccountId = "fake-google-account",
        ItemId = id,
        ParentItemId = "root",
        Name = name,
        Kind = StorageItemKind.File,
        Size = size
    };

    private static StorageItem Folder(string id, string name) => new()
    {
        ProviderId = "google-drive",
        ProviderAccountId = "fake-google-account",
        ItemId = id,
        Name = name,
        Kind = StorageItemKind.Folder
    };
}
