using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.Infrastructure;

public sealed class ActivityProviderDiagnostics(IActivityEventRepository events) : IProviderDiagnostics
{
    public Task WriteAsync(string eventType, string vietnameseMessage, string? technicalDetails, CancellationToken cancellationToken) =>
        events.AddAsync(new ActivityEvent(Guid.NewGuid(), null, DateTimeOffset.UtcNow, eventType, vietnameseMessage, technicalDetails), cancellationToken);
}
