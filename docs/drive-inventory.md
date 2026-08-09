# Real Google Drive inventory

## Scope and data boundary

The production **Bắt đầu quét** action validates the restored Google session, reads storage quota metadata, enumerates non-trashed metadata visible through the Drive `user` corpus, reconstructs display paths, and writes a local SQLite snapshot. It includes My Drive and ordinary items shared with the connected account. Shared drives are excluded in this milestone. Shortcuts are recorded but never followed.

The request fields are ID, name, MIME type, parent IDs, size, created/modified time, `trashed`, MD5, extension, shared/owned flags and shortcut target metadata. Storage values are limit, total usage, Drive usage and trash usage when Google supplies them. A missing size/checksum remains `NULL`; the unknown-size count covers regular and Workspace files with no supplied size, and no value is invented.

No content bytes, thumbnails, permission lists, or web links are requested. The implementation does not call download, `files.export`, create, copy, update, delete, trash, move, rename, or permission APIs. The only OAuth scope remains `https://www.googleapis.com/auth/drive.readonly`.

## Pagination, hierarchy and snapshots

Inventory uses `files.list` with `trashed = false`, `spaces=drive`, `corpora=user` and page size 1.000. Each `nextPageToken` is consumed until absent. An incomplete search, repeated token, or the defensive 100.000-page guard fails the staging run. Duplicate IDs are stored once even when names match.

Each API page is committed to `drive_scan_items` in its own bounded transaction. Only compact ID/name/parent nodes stay in memory for hierarchy resolution. The iterative builder memoizes resolved parents, never follows shortcuts, avoids recursion on deep trees, and maps missing parents/cycles to `Không xác định được thư mục cha`. Display paths are presentation data; `(scan_id, file_id)` is the database identity.

`drive_scan_runs` starts as `Scanning` with `is_complete=0`. Only after all pages and hierarchy updates succeed does one guarded transaction set status `Completed` and `is_complete=1`. Latest-summary queries select completed rows only. Cancellation, failure, revoked authorization and startup recovery keep the new row incomplete, so the previous complete snapshot stays available.

## Lifecycle, retry and diagnostics

The visible stages are `Idle`, `ValidatingSession`, `LoadingStorageInformation`, `Scanning`, `BuildingHierarchy`, `SavingSnapshot`, `Completed`, `Cancelled`, `Failed`, and `RequiresReauthentication`. A zero-timeout semaphore permits only one scan; cancellation flows through API calls, persistence and retry delay; busy state resets in `finally`. A retry starts a new staging scan.

Transient network, timeout, HTTP 429 and 5xx failures use bounded exponential backoff with jitter. A server `Retry-After` delta/date overrides the calculated delay. Permanent auth/validation failures are not retried indefinitely. Diagnostics contain correlation ID, stage, page/batch/item counts, elapsed time and safe categories—never names, local paths, tokens, headers, secrets, raw API responses or content.

## Manual Windows verification

These checks require the developer’s connected Google account and are not performed by the automated suite:

1. Start CloudKeeperSN and confirm the real Google account is restored.
2. Confirm **Bắt đầu quét** is enabled, then start a scan.
3. Confirm stage text and item count update and the UI remains responsive.
4. Complete the scan and compare approximate item count and storage usage with Google Drive.
5. Confirm regular files, folders and Google Workspace items are represented.
6. Confirm Dashboard updates without restart or navigation.
7. Restart CloudKeeperSN and confirm the last successful snapshot is restored.
8. Start another scan, cancel it, and confirm the previous successful snapshot remains.
9. Disconnect the network during a scan; confirm a recoverable Vietnamese message and retry action.
10. Revoke Google access; confirm the app reports **Cần đăng nhập lại**.
11. Confirm no Google Drive item was created, modified, moved, renamed, downloaded or deleted.
