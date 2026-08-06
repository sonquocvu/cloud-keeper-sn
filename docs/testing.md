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
- `CloudKeeperSN.App.Tests`: navigation, disconnected states, backup enablement/validation, preview counts and filtering, confirmation safety text, pause/resume/cancel/retry, Vietnamese state mapping, result severity, theme persistence, settings validation, window restoration, demo determinism, and async-command disposal.

SQLite tests use a unique directory under the OS temporary directory and remove it after each test instance.

## Optional integration tests

None exist in Checkpoint 1. Later integration tests must live separately, require explicit opt-in configuration, use dedicated test accounts, and document scopes and cleanup. Unit tests must remain offline and use fakes.

## Manual checks deferred

No interactive GUI smoke test or screenshot comparison is launched by the automated suite. Manually verify Vietnamese rendering, light/dark/system themes, keyboard navigation, dialogs, collapsed navigation, 100%/125%/150% scaling, and `%LOCALAPPDATA%\CloudKeeperSN` behavior on Windows.
