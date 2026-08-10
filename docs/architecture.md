# Architecture

## Scope

This document describes the domain foundation, the real read-only Google Drive inventory provider, and the isolated fake-provider UI showcase. Microsoft Graph remains intentionally absent.

## Dependency direction

```text
CloudKeeperSN.App
  -> Application
  -> Infrastructure
  -> Providers.GoogleDrive / Providers.OneDrive

Infrastructure -> Application -> Domain
Providers.*    -> Application -> Domain
Domain         -> no project dependency
```

Provider SDK models must remain inside their provider assembly. Application code depends on capability interfaces (`IStorageBrowserCapability`, `IStorageReadCapability`, `IStorageWriteCapability`, and `IStorageFolderWriteCapability`) instead of assuming that every provider can do everything. A provider advertises capabilities through `StorageProviderDescriptor`.

## Major concepts

- `StorageItem` uses provider account ID plus provider item ID; names are display data, not identity.
- `StoragePath` preserves ordered relative segments.
- `SourceDestinationMapping` persists a stable destination name and destination item ID for reruns.
- `TransferItem` carries source/destination identity, retry state, verification state, timestamps, size, checksum information, and error category.
- `TransferStateMachine` is the only domain rule for legal transfer state changes.
- `IConflictNamePolicy` isolates deterministic destination naming.
- `VerificationLevel` states evidence strength without pretending incompatible hashes match.

## Persistence

The infrastructure uses `Microsoft.Data.Sqlite` directly rather than EF Core. The schema is small, explicit SQL is clearer for atomic state updates, and this avoids an ORM dependency while retaining numbered migrations. Initialization enables foreign keys, WAL journaling, and a busy timeout. Each queue-item update is atomic. On startup, in-flight `Downloading`, `Uploading`, or `Verifying` items are changed to `RetryPending`; they are never marked complete during recovery.

Migration 1 creates tables for accounts (metadata only), backup definitions, runs, transfer items, mappings, export decisions, verification results, retry state, Vietnamese activity events, and application settings. Migration 2 adds account email metadata. Migration 3 adds `drive_scan_runs` and `drive_scan_items`. Migration 4 adds one local backup-selection plan per provider account plus compact include/exclude rules. A scan run begins incomplete, API pages are appended in bounded transactions, hierarchy paths are updated in bounded chunks, and a guarded final update publishes the run as complete. Startup changes abandoned `Scanning` rows to `Interrupted`; it never changes an earlier complete row. File contents, thumbnails, OAuth tokens, and client secrets are never stored in SQLite.

## Drive inventory pipeline

`IDriveInventorySource` is the provider boundary for `about.get` quota metadata and paged `files.list` metadata. `DriveInventoryScanner` owns the explicit lifecycle, single-scan gate, cancellation, safe diagnostics, counters, and staging snapshot. `DriveHierarchyBuilder` resolves parent IDs iteratively with memoized paths, so duplicate names remain legal and deep or cyclic graphs cannot recurse the process stack. `IDriveInventoryRepository` is the only persistence boundary used by the scanner.

The scanner keeps only stable IDs and hierarchy nodes in memory; full item metadata is written one API page at a time. `DashboardViewModel`, `BackupViewModel`, and `HistoryViewModel` subscribe to the singleton scanner and marshal state changes through `IUiDispatcher`, allowing the active page and dashboard/history to refresh without navigation or restart.

## Local selection planning

`BackupSelectionPlanner` evaluates explicit include/exclude rules by walking stable parent IDs. The closest rule wins: selecting a folder includes eligible descendants, while an exclusion on a child file or folder overrides the ancestor. Rules are not expanded into thousands of rows, so a newly discovered descendant can be identified as newly selected during reconciliation. Evaluation is iterative, cycle-safe and memoized.

`BackupSelectionPlanService` always loads the newest complete snapshot and compares it with the plan's prior source snapshot. It reports newly inherited selections, previously selected IDs no longer present, and rule targets missing from the latest inventory. Save rechecks the latest snapshot ID to prevent a scan/save race. `InventoryPlanViewModel` preserves unsaved edits across page refresh requests and dispatches completed-scan notifications safely.

## Streaming boundary

`IStorageReadCapability` returns a stream and `IStorageWriteSession` accepts chunks, but the real Google provider implements neither. Only fake providers exercise content transfer for deterministic demo tests. A future destination/transfer checkpoint must use bounded buffers and resumable upload sessions.

## Authentication boundary

`GoogleAuthenticationService` uses system-browser installed-app OAuth with PKCE and a random-port `127.0.0.1` loopback receiver. Its explicit lifecycle is opening browser → waiting for callback → code exchange → account lookup → read-only Drive verification → connected. A wrapper adds and validates an unpredictable OAuth state value; callback waiting and code exchange have separate bounded timeouts. Connected state is a single authoritative record containing account metadata, published only after protected token storage, `about.user`, a minimal read-only `files.list`, and local account persistence succeed. Operation versions prevent a late startup restore from overwriting a newer interactive connection, and the Accounts view dispatches provider events through the WPF dispatcher before notifying properties and commands.

`GoogleOAuthConfigurationManager` accepts only Google's dedicated Desktop `installed` JSON shape, validates Google HTTPS endpoints/loopback redirects, and persists only client ID/secret through the DPAPI CurrentUser protected credential store. Imported Settings configuration has precedence over a complete environment-variable development pair; values are never combined across sources. Change notifications refresh the OAuth client, Accounts command state and Settings metadata in the same process. `ProtectedGoogleDataStore` separately protects user authorization tokens outside SQLite. Account rows contain metadata only. Disconnect and incomplete-authentication cleanup remove local authorization only; remote revocation remains an explicit Google Account action. Password forms are prohibited.

## Package decisions

Versions are centrally pinned in `Directory.Packages.props`:

- `Microsoft.Data.Sqlite` 10.0.10: current stable .NET 10 servicing release at implementation time; chosen for explicit, migration-safe persistence.
- `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12: explicitly pinned compatible servicing release so the vulnerable/deprecated 2.1.11 native transitive dependency cannot be selected.
- `Microsoft.Extensions.DependencyInjection` 10.0.10: composition root for WPF.
- xUnit 2.9.3, Visual Studio runner 3.1.5, and Microsoft.NET.Test.Sdk 18.8.1: offline unit-test stack with .NET 10 support.

- `Google.Apis.Auth` / `Google.Apis.Drive.v3` 1.75.0: official .NET OAuth/Drive SDKs, isolated inside the Google provider assembly.
- `System.Security.Cryptography.ProtectedData` 10.0.10: Windows DPAPI token protection.

No Microsoft Graph dependency is present.

## UI

The WPF application uses a lightweight internal `INotifyPropertyChanged`/`ICommand` foundation. `MainWindowViewModel` owns navigation and page view models own workflow state. Views contain presentation only; code-behind is limited to dialog results and window placement.

Semantic resource dictionaries provide light/dark/high-contrast themes, typography, spacing, and standard control states. `ThemeService` persists the preference, follows Windows theme changes in System mode and uses system colors in high contrast. `WindowPlacementService` persists bounds; `WindowPlacementValidator` restores off-screen windows to the visible desktop.

`DemoDataService`, `DemoBackupPlanner`, and `DemoTransferEngine` are UI-development adapters around the existing fake providers. They are explicitly gated by `DemoConfiguration`, use deterministic scenarios, and never masquerade as live provider data. Production composition registers the real inventory scanner and does not register fake providers as `IStorageProvider`. The guided demo workflow requires a preview and confirmation before the fake engine starts.

UI-facing statuses are mapped to natural Vietnamese by `VietnamesePresentationMapper`; internal enum names never need to appear in views. Diagnostic export redacts sensitive values before serialization.
