# Development and demo scenarios

Demo data is never silently enabled in a Release build. Set:

```powershell
$env:CLOUDKEEPERSN_DEMO_MODE='true'
$env:CLOUDKEEPERSN_DEMO_SCENARIO='Standard'
```

Debug builds default to demo mode when `CLOUDKEEPERSN_DEMO_MODE` is absent. The shell displays **Chế độ trình diễn** whenever it is active.

Available deterministic scenarios:

| Value | Demonstrates |
|---|---|
| `Disconnected` | Both accounts disconnected |
| `GoogleOnly` | Google Drive connected, OneDrive disconnected |
| `ConnectedEmpty` | Both connected, empty source/destination roots |
| `Standard` | Normal folders, duplicate names, Google Docs/Sheets/Slides, unsupported native item, destination conflict, previous backup, retryable interruption, recent history |
| `LongRunning` | Slower fake transfer for pause/resume/cancel demonstration |
| `CompletedSuccessfully` | Successful recent run and fully positive result presentation |
| `CompletedWithWarnings` | Recent run with warnings and limited verification |
| `RetryAndFailure` | Retryable interruption followed by a deterministic permanent file failure |

The source fake contains duplicate `Ngân sách.xlsx` identities, supported Google-native items, an unsupported Form, and normal files. Preview planning is deterministic and never uses filename as source identity.

Fake account connect/disconnect, folder browsing, folder creation, scanning, and transfer delays are asynchronous and cancellable. No HTTP request, OAuth browser, Google API, Microsoft Graph API, cloud upload, or cloud deletion occurs.

