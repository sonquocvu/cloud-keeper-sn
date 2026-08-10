using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Domain.Planning;
using CloudKeeperSN.Domain.Scanning;
using Microsoft.Data.Sqlite;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteBackupSelectionPlanRepository(
    IApplicationDatabase database,
    SqliteConnectionFactory connectionFactory) : IBackupSelectionPlanRepository
{
    public async Task<BackupSelectionPlan?> GetByAccountAsync(string providerAccountId, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM backup_selection_plans WHERE provider_account_id=$account LIMIT 1";
        command.Parameters.AddWithValue("$account", providerAccountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var id = Guid.Parse(reader.GetString(reader.GetOrdinal("plan_id")));
        var name = reader.GetString(reader.GetOrdinal("name"));
        var scanId = Guid.Parse(reader.GetString(reader.GetOrdinal("source_scan_id")));
        var created = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at_utc")));
        var updated = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at_utc")));
        await reader.DisposeAsync();

        await using var ruleCommand = connection.CreateCommand();
        ruleCommand.CommandText = "SELECT * FROM backup_selection_rules WHERE plan_id=$plan ORDER BY item_id";
        ruleCommand.Parameters.AddWithValue("$plan", id.ToString());
        var rules = new List<BackupSelectionRule>();
        await using var ruleReader = await ruleCommand.ExecuteReaderAsync(cancellationToken);
        while (await ruleReader.ReadAsync(cancellationToken))
        {
            rules.Add(new BackupSelectionRule(
                ruleReader.GetString(ruleReader.GetOrdinal("item_id")),
                Enum.Parse<BackupSelectionRuleMode>(ruleReader.GetString(ruleReader.GetOrdinal("rule_mode"))),
                Enum.Parse<DriveInventoryItemKind>(ruleReader.GetString(ruleReader.GetOrdinal("item_kind"))),
                ruleReader.GetString(ruleReader.GetOrdinal("last_known_name"))));
        }
        return new(id, providerAccountId, name, scanId, created, updated, rules);
    }

    public async Task SaveAsync(BackupSelectionPlan plan, CancellationToken cancellationToken)
    {
        await database.InitializeAsync(cancellationToken);
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO backup_selection_plans(plan_id, provider_account_id, name, source_scan_id, created_at_utc, updated_at_utc)
                VALUES($id, $account, $name, $scan, $created, $updated)
                ON CONFLICT(provider_account_id) DO UPDATE SET
                    name=excluded.name, source_scan_id=excluded.source_scan_id, updated_at_utc=excluded.updated_at_utc;
                """;
            command.Parameters.AddWithValue("$id", plan.PlanId.ToString());
            command.Parameters.AddWithValue("$account", plan.ProviderAccountId);
            command.Parameters.AddWithValue("$name", plan.Name);
            command.Parameters.AddWithValue("$scan", plan.SourceScanId.ToString());
            command.Parameters.AddWithValue("$created", plan.CreatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$updated", plan.UpdatedAtUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM backup_selection_rules WHERE plan_id=$plan";
            delete.Parameters.AddWithValue("$plan", plan.PlanId.ToString());
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var rule in plan.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO backup_selection_rules(plan_id, item_id, rule_mode, item_kind, last_known_name)
                VALUES($plan, $item, $mode, $kind, $name);
                """;
            command.Parameters.AddWithValue("$plan", plan.PlanId.ToString());
            command.Parameters.AddWithValue("$item", rule.ItemId);
            command.Parameters.AddWithValue("$mode", rule.Mode.ToString());
            command.Parameters.AddWithValue("$kind", rule.ItemKind.ToString());
            command.Parameters.AddWithValue("$name", rule.LastKnownName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
