# Google Drive read-only provider

## Boundary

Production exposes only `Authenticate`, `Browse`, `ReadMetadata`, `PlanNativeExport` and provider checksum metadata. It does **not** implement `IStorageReadCapability`, `IStorageNativeExportCapability`, `IStorageWriteCapability` or `IStorageFolderWriteCapability`. Therefore the composition root cannot resolve a Google content/download/export/write path accidentally.

The provider requests only `drive.readonly`. Calls currently used:

- Drive `about.get` fields: `user(displayName,emailAddress,permissionId)`;
- Drive `about.get` fields: `storageQuota(limit,usage,usageInDrive,usageInDriveTrash)`;
- folder browsing through `files.list` with a parent-ID query and page size 200;
- inventory through `files.list` with `corpora=user`, `spaces=drive`, `trashed = false`, page size 1.000 and a deliberate metadata field list.

No `files.get` with `alt=media`, `files.export`, create, update, copy, delete or permissions mutation is called.

## Browse and inventory rules

- root ID is `root`, displayed as **Drive của tôi**;
- folder queries escape backslash and apostrophe literals;
- every continuation page is consumed, including empty pages with a next token;
- repeated tokens, incomplete search and excessive page counts fail safely;
- duplicate display names remain separate by account ID + item ID;
- picker results are published only after the current request completes;
- the inventory includes non-trashed My Drive items and ordinary items shared with the connected user that the `user` corpus exposes;
- shared drives are deliberately excluded (`includeItemsFromAllDrives=false`, `supportsAllDrives=false`) until separately implemented and tested;
- shortcuts are recorded with target ID/MIME metadata and never automatically followed;
- all continuation pages are consumed at up to 1.000 items per request; repeated tokens, incomplete search and excessive page counts fail safely;
- metadata is persisted per page, while folder hierarchy is reconstructed iteratively by stable IDs;
- missing parents and cycles receive `Không xác định được thư mục cha`; partial/cancelled scans never become a complete snapshot.

## Native export plan (planning only)

| Google type | Planned format | Current action |
|---|---|---|
| Docs | `.docx` | preview only |
| Sheets | `.xlsx` | preview only |
| Slides | `.pptx` | preview only |
| Drawings | `.png` | preview only |
| Forms, Sites, other native types | unsupported | skip with reason |
| Shortcuts | unsupported traversal | skip with reason |

Native sizes are usually unknown and the UI says so. No estimate is invented. The preview explicitly says export has not occurred.

## Failure and retry

User messages are stable Vietnamese categories: authentication/re-authentication, permission, network, timeout, throttling, service unavailable, missing folder, inaccessible item and invalid response. Technical diagnostics include categories/types but pass through redaction.

Network, timeout, throttle and service-unavailable failures use bounded exponential backoff with jitter. `Retry-After` from an unsuccessful Google HTTP response is captured and honored when present. Permission/authentication/invalid-response failures are not retried. Cancellation interrupts both API requests and backoff delay.

## Known limitations

- Shared drives and an explicit “Shared with me” navigation UX are not implemented. Ordinary shared items visible through the user corpus are classified and may have unresolved hierarchy.
- No content download/export, destination provider, transfer or verification.
- No guarded shortcut traversal.
- No live-account automated integration suite.
- Google OAuth verification/security assessment remains an owner/release prerequisite.
