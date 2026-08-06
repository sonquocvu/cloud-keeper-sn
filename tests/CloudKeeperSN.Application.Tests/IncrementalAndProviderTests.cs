using CloudKeeperSN.Application.Planning;
using CloudKeeperSN.Domain.Backup;
using CloudKeeperSN.Providers.OneDrive.Fakes;

namespace CloudKeeperSN.Application.Tests;

public sealed class IncrementalAndProviderTests
{
    [Fact]
    public void IncrementalRerun_SkipsUnchangedMappedItem()
    {
        var mapping = new SourceDestinationMapping("google", "source-1", "microsoft", "dest", "file.txt", "dest-1", "fingerprint-a", DateTimeOffset.UtcNow);

        var decision = new IncrementalDecisionService().Decide(mapping, "fingerprint-a", true);

        Assert.Equal(IncrementalDecisionKind.SkipUnchanged, decision.Kind);
    }

    [Fact]
    public void IncrementalRerun_CopiesChangedSourceSafely()
    {
        var mapping = new SourceDestinationMapping("google", "source-1", "microsoft", "dest", "file.txt", "dest-1", "old", DateTimeOffset.UtcNow);

        var decision = new IncrementalDecisionService().Decide(mapping, "new", true);

        Assert.Equal(IncrementalDecisionKind.CopyUpdatedSafely, decision.Kind);
    }

    [Fact]
    public async Task FakeOneDrive_UploadsInChunksAndNeverOverwrites()
    {
        var provider = new FakeOneDriveProvider();
        provider.Connect();
        await using var session = await provider.CreateWriteSessionAsync("fake-microsoft-account", "root", "a?.txt", 4, CancellationToken.None);
        await session.WriteAsync(new byte[] { 1, 2 }, CancellationToken.None);
        await session.WriteAsync(new byte[] { 3, 4 }, CancellationToken.None);
        var item = await session.CompleteAsync(CancellationToken.None);

        Assert.Equal("a_.txt", item.Name);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, provider.GetContent(item.ItemId).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CreateWriteSessionAsync("fake-microsoft-account", "root", "a?.txt", 1, CancellationToken.None));
    }
}
