using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.Application.Recovery;

public sealed class TransferRecoveryService(ITransferItemRepository transferItems)
{
    public Task<int> RecoverAsync(CancellationToken cancellationToken) =>
        transferItems.RecoverInterruptedAsync(DateTimeOffset.UtcNow, cancellationToken);
}

