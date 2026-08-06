# Testing

## Commands

With .NET 10 SDK installed:

```powershell
dotnet restore CloudKeeperSN.sln
dotnet build CloudKeeperSN.sln --configuration Release --no-restore
dotnet test CloudKeeperSN.sln --configuration Release --no-build
```

Tests do not launch WPF or require live accounts.

## Test layout

- `CloudKeeperSN.Domain.Tests`: path preservation, OneDrive normalization/reserved names, deterministic conflict naming, Google-native exports, checksums, retry/`Retry-After`, state transitions, pause/resume, redaction, and cycle identity.
- `CloudKeeperSN.Application.Tests`: fake-source scanning, duplicate Google names, shortcut/folder cycle behavior, cancellation, incremental decisions, and chunked fake OneDrive uploads without overwrite.
- `CloudKeeperSN.Infrastructure.Tests`: SQLite migrations, stable identity mapping, crash recovery, and redaction before activity persistence.

SQLite tests use a unique directory under the OS temporary directory and remove it after each test instance.

## Optional integration tests

None exist in Checkpoint 1. Later integration tests must live separately, require explicit opt-in configuration, use dedicated test accounts, and document scopes and cleanup. Unit tests must remain offline and use fakes.

## Manual checks deferred

No interactive GUI smoke test was launched, per the delivery rules. Once a .NET 10-capable developer machine is available, a developer may manually verify navigation, Vietnamese text rendering, DPI scaling, and `%LOCALAPPDATA%\CloudKeeperSN` creation.

