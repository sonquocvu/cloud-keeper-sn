# Architecture

## Scope

This document describes Checkpoint 1. Real Google Drive and Microsoft Graph adapters are intentionally absent.

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

`IStorageReadCapability` returns a stream and `IStorageWriteSession` accepts chunks. The fake implementation buffers only for deterministic tests. Real adapters must use bounded buffers and resumable OneDrive upload sessions. The transfer engine is a later checkpoint.

## Authentication boundary

`IProviderAuthenticationService` is provider-specific. Future implementations must open the system browser, request minimal scopes, and store cached tokens with Windows DPAPI. Account rows contain metadata only. Password forms are prohibited.

## Package decisions

Versions are centrally pinned in `Directory.Packages.props`:

- `Microsoft.Data.Sqlite` 10.0.10: current stable .NET 10 servicing release at implementation time; chosen for explicit, migration-safe persistence.
- `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12: explicitly pinned compatible servicing release so the vulnerable/deprecated 2.1.11 native transitive dependency cannot be selected.
- `Microsoft.Extensions.DependencyInjection` 10.0.10: composition root for WPF.
- xUnit 2.9.3, Visual Studio runner 3.1.5, and Microsoft.NET.Test.Sdk 18.8.1: offline unit-test stack with .NET 10 support.

No Google or Microsoft SDK dependency is added before its provider checkpoint.

## UI

The WPF application uses a small in-house `INotifyPropertyChanged`/`ICommand` foundation to avoid an unnecessary MVVM package. All visible strings are Vietnamese. The current dashboard reads account metadata from SQLite; actions requiring real provider connections are disabled.
