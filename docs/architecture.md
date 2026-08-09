# Architecture

## Scope

This document describes the domain foundation, the real read-only Google Drive provider, and the isolated fake-provider UI showcase. Microsoft Graph remains intentionally absent.

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

Migration 1 creates tables for accounts (metadata only), backup definitions, runs, transfer items, mappings, export decisions, verification results, retry state, Vietnamese activity events, and application settings. File contents and thumbnails are never stored in SQLite.

## Streaming boundary

`IStorageReadCapability` returns a stream and `IStorageWriteSession` accepts chunks, but the real Google provider implements neither. Only fake providers exercise content transfer for deterministic demo tests. A future destination/transfer checkpoint must use bounded buffers and resumable upload sessions.

## Authentication boundary

`GoogleAuthenticationService` uses system-browser installed-app OAuth with PKCE and a random-port `127.0.0.1` loopback receiver. A wrapper adds and validates an unpredictable OAuth state value, and interactive authorization has a bounded timeout. `GoogleOAuthConfigurationManager` accepts only Google's dedicated Desktop `installed` JSON shape, validates Google HTTPS endpoints/loopback redirects, and persists only client ID/secret through the DPAPI CurrentUser protected credential store. Imported Settings configuration has precedence over a complete environment-variable development pair; values are never combined across sources. Change notifications refresh the OAuth client, Accounts command state and Settings metadata in the same process. `ProtectedGoogleDataStore` separately protects user authorization tokens outside SQLite. Account rows contain metadata only. Password forms are prohibited.

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

`DemoDataService`, `DemoBackupPlanner`, and `DemoTransferEngine` are UI-development adapters around the existing fake providers. They are explicitly gated by `DemoConfiguration`, use deterministic scenarios, and never masquerade as live provider data. The guided workflow requires a preview and confirmation before the fake engine starts.

UI-facing statuses are mapped to natural Vietnamese by `VietnamesePresentationMapper`; internal enum names never need to appear in views. Diagnostic export redacts sensitive values before serialization.
