using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed class SqliteApplicationDatabase(SqliteConnectionFactory connectionFactory) : IApplicationDatabase
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;

    private static readonly IReadOnlyList<(int Version, string Sql)> Migrations =
    [
        (1, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE connected_accounts (
                id TEXT NOT NULL PRIMARY KEY,
                provider_id TEXT NOT NULL,
                provider_account_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                is_connected INTEGER NOT NULL,
                last_connected_at_utc TEXT NULL,
                UNIQUE(provider_id, provider_account_id)
            );

            CREATE TABLE backup_definitions (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                source_provider_id TEXT NOT NULL,
                source_account_id TEXT NOT NULL,
                source_folder_id TEXT NOT NULL,
                destination_provider_id TEXT NOT NULL,
                destination_account_id TEXT NOT NULL,
                destination_folder_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE backup_runs (
                id TEXT NOT NULL PRIMARY KEY,
                backup_definition_id TEXT NOT NULL,
                status TEXT NOT NULL,
                preview_was_shown INTEGER NOT NULL DEFAULT 0,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                FOREIGN KEY(backup_definition_id) REFERENCES backup_definitions(id)
            );

            CREATE TABLE transfer_items (
                id TEXT NOT NULL PRIMARY KEY,
                run_id TEXT NOT NULL,
                source_provider_account_id TEXT NOT NULL,
                source_item_id TEXT NOT NULL,
                source_parent_item_id TEXT NULL,
                original_name TEXT NOT NULL,
                normalized_destination_name TEXT NOT NULL,
                relative_source_path TEXT NOT NULL,
                destination_item_id TEXT NULL,
                mime_type TEXT NULL,
                file_size INTEGER NULL,
                source_created_at_utc TEXT NULL,
                source_modified_at_utc TEXT NULL,
                source_checksum_algorithm TEXT NULL,
                source_checksum TEXT NULL,
                state TEXT NOT NULL,
                verification_level TEXT NULL,
                last_error_category TEXT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                next_retry_at_utc TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY(run_id) REFERENCES backup_runs(id),
                UNIQUE(run_id, source_provider_account_id, source_item_id)
            );

            CREATE TABLE source_destination_mappings (
                source_provider_account_id TEXT NOT NULL,
                source_item_id TEXT NOT NULL,
                destination_provider_account_id TEXT NOT NULL,
                destination_parent_item_id TEXT NOT NULL,
                destination_name TEXT NOT NULL,
                destination_item_id TEXT NULL,
                source_fingerprint TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                PRIMARY KEY(source_provider_account_id, source_item_id, destination_provider_account_id)
            );

            CREATE TABLE export_decisions (
                run_id TEXT NOT NULL,
                source_item_id TEXT NOT NULL,
                source_mime_type TEXT NOT NULL,
                export_mime_type TEXT NULL,
                destination_extension TEXT NULL,
                is_supported INTEGER NOT NULL,
                explanation_vi TEXT NOT NULL,
                PRIMARY KEY(run_id, source_item_id),
                FOREIGN KEY(run_id) REFERENCES backup_runs(id)
            );

            CREATE TABLE verification_results (
                transfer_item_id TEXT NOT NULL PRIMARY KEY,
                level TEXT NOT NULL,
                explanation_vi TEXT NOT NULL,
                source_evidence TEXT NULL,
                destination_evidence TEXT NULL,
                verified_at_utc TEXT NOT NULL,
                FOREIGN KEY(transfer_item_id) REFERENCES transfer_items(id)
            );

            CREATE TABLE retry_state (
                transfer_item_id TEXT NOT NULL PRIMARY KEY,
                attempt_count INTEGER NOT NULL,
                next_retry_at_utc TEXT NULL,
                retry_after_seconds REAL NULL,
                last_category TEXT NULL,
                FOREIGN KEY(transfer_item_id) REFERENCES transfer_items(id)
            );

            CREATE TABLE activity_events (
                id TEXT NOT NULL PRIMARY KEY,
                run_id TEXT NULL,
                occurred_at_utc TEXT NOT NULL,
                event_type TEXT NOT NULL,
                message_vi TEXT NOT NULL,
                technical_details_redacted TEXT NULL
            );

            CREATE TABLE application_settings (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE INDEX ix_transfer_items_recovery ON transfer_items(state, next_retry_at_utc);
            CREATE INDEX ix_activity_events_time ON activity_events(occurred_at_utc DESC);
            """),
        (2, """
            ALTER TABLE connected_accounts ADD COLUMN email TEXT NULL;
            """),
        (3, """
            CREATE TABLE drive_scan_runs (
                scan_id TEXT NOT NULL PRIMARY KEY,
                provider_id TEXT NOT NULL,
                provider_account_id TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                status TEXT NOT NULL,
                total_items INTEGER NOT NULL DEFAULT 0,
                folder_count INTEGER NOT NULL DEFAULT 0,
                file_count INTEGER NOT NULL DEFAULT 0,
                known_bytes INTEGER NOT NULL DEFAULT 0,
                unknown_size_count INTEGER NOT NULL DEFAULT 0,
                google_workspace_count INTEGER NOT NULL DEFAULT 0,
                shortcut_count INTEGER NOT NULL DEFAULT 0,
                unresolved_count INTEGER NOT NULL DEFAULT 0,
                backup_eligible_count INTEGER NOT NULL DEFAULT 0,
                failure_category TEXT NULL,
                is_complete INTEGER NOT NULL DEFAULT 0,
                storage_limit_bytes INTEGER NULL,
                total_usage_bytes INTEGER NULL,
                drive_usage_bytes INTEGER NULL,
                trash_usage_bytes INTEGER NULL
            );

            CREATE TABLE drive_scan_items (
                scan_id TEXT NOT NULL,
                file_id TEXT NOT NULL,
                name TEXT NOT NULL,
                parent_id TEXT NULL,
                display_path TEXT NOT NULL,
                mime_type TEXT NOT NULL,
                item_kind TEXT NOT NULL,
                location TEXT NOT NULL,
                file_size INTEGER NULL,
                created_at_utc TEXT NULL,
                modified_at_utc TEXT NULL,
                md5_checksum TEXT NULL,
                file_extension TEXT NULL,
                shortcut_target_id TEXT NULL,
                shortcut_target_mime_type TEXT NULL,
                is_shared INTEGER NOT NULL,
                is_owned_by_user INTEGER NULL,
                backup_eligible INTEGER NOT NULL,
                skip_reason TEXT NULL,
                PRIMARY KEY(scan_id, file_id),
                FOREIGN KEY(scan_id) REFERENCES drive_scan_runs(scan_id) ON DELETE CASCADE
            );

            CREATE INDEX ix_drive_scan_runs_latest ON drive_scan_runs(provider_account_id, is_complete, completed_at_utc DESC);
            CREATE INDEX ix_drive_scan_items_path ON drive_scan_items(scan_id, display_path);
            """)
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var connection = await connectionFactory.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=15000;", cancellationToken);
            await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER NOT NULL PRIMARY KEY, applied_at_utc TEXT NOT NULL);", cancellationToken);

            foreach (var migration in Migrations)
            {
                await using var existsCommand = connection.CreateCommand();
                existsCommand.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = $version";
                existsCommand.Parameters.AddWithValue("$version", migration.Version);
                var exists = Convert.ToInt64(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
                if (exists) continue;

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using var migrationCommand = connection.CreateCommand();
                migrationCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
                migrationCommand.CommandText = migration.Sql;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

                await using var recordCommand = connection.CreateCommand();
                recordCommand.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
                recordCommand.CommandText = "INSERT INTO schema_migrations(version, applied_at_utc) VALUES ($version, $appliedAt)";
                recordCommand.Parameters.AddWithValue("$version", migration.Version);
                recordCommand.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
                await recordCommand.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private static async Task ExecuteAsync(Microsoft.Data.Sqlite.SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
