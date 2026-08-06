using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Diagnostics;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteActivityEventRepository(
    IApplicationDatabase database,
    SqliteConnectionFactory connectionFactory) : IActivityEventRepository
{
    public async Task AddAsync(ActivityEvent activityEvent, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO activity_events(id, run_id, occurred_at_utc, event_type, message_vi, technical_details_redacted)
            VALUES ($id, $runId, $occurredAt, $eventType, $message, $details)
            """;
        command.Parameters.AddWithValue("$id", activityEvent.Id.ToString());
        command.Parameters.AddWithValue("$runId", activityEvent.RunId?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$occurredAt", activityEvent.OccurredAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$eventType", activityEvent.EventType);
        command.Parameters.AddWithValue("$message", activityEvent.VietnameseMessage);
        command.Parameters.AddWithValue("$details", activityEvent.TechnicalDetails is null ? DBNull.Value : SensitiveDataRedactor.Redact(activityEvent.TechnicalDetails));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int maximumCount, CancellationToken cancellationToken)
    {
        if (maximumCount < 1) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, run_id, occurred_at_utc, event_type, message_vi, technical_details_redacted
            FROM activity_events ORDER BY occurred_at_utc DESC LIMIT $maximumCount
            """;
        command.Parameters.AddWithValue("$maximumCount", maximumCount);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<ActivityEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new ActivityEvent(
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return events;
    }
}

