# Backup behavior

## Direction and confirmation

The intended direction is Google Drive → a future destination. Google Drive is source-only. The current production action builds a local metadata inventory summary containing regular-file/folder/Workspace/shortcut counts, known bytes, explicit unknown-size counts, unresolved items, quota metadata and future eligibility. It does not create a transfer preview or export decision that can execute.

No real destination or transfer exists in this build. The start action is disabled and explains that no file has been downloaded, exported or transmitted. OneDrive transfer remains available only in clearly labelled demo mode.

## Local selection plan

The **Kế hoạch** page reads the latest complete SQLite inventory. A direct include rule can target a folder or individual file. Folder selection is inherited by descendants; a closer exclude rule removes a child file or subtree. Only backup-eligible non-folder items contribute to the selected-item and known-size summary. Unsupported items, shortcuts and unresolved hierarchy remain visible under **Cần kiểm tra** and are never silently counted as transferable.

The saved plan contains no destination and cannot start a transfer. When a newer complete scan exists, the application evaluates the same stable-ID rules against both snapshots and reports newly inherited items, previously selected items that disappeared, and rules whose target is missing. Renames and moves retain selection when Google keeps the same file ID.

## Folder and filename handling

Relative path segments are preserved in order. Empty folders will be created where the destination supports folder creation. OneDrive-invalid/control characters are replaced deterministically, trailing spaces/dots are removed, reserved device names receive a safe prefix, and overlong names receive a stable hash suffix.

Duplicate Google Drive names remain distinct because provider item IDs are tracked. If a destination name is occupied by an unrelated item, the default policy creates `Tên (CloudKeeperSN 2).ext`, incrementing the occurrence deterministically during planning. The selected mapping must be saved and reused, preventing suffix accumulation on each rerun.

## Incremental reruns

- Existing CloudKeeperSN mapping + unchanged fingerprint + matching destination identity: skip.
- Existing mapping + changed source: plan a safe updated copy; never silently overwrite.
- Existing mapping + missing recorded destination: recreate safely after conflict checks.
- No mapping: plan a new copy.

A source fingerprint is evidence for change detection, not automatically proof of end-to-end equality.

## Google-native items

- Google Docs → `.docx`
- Google Sheets → `.xlsx`
- Google Slides → `.pptx`
- Google Drawings → `.png`
- shortcut or unsupported native type → skip with a Vietnamese warning

The exported extension participates in destination conflict detection. Since exported bytes differ from the provider-native representation, verification is labeled according to available export metadata rather than a nonexistent equal source byte count.

## Verification

The default chooses the strongest compatible evidence available: strong equal hash, same provider/hash algorithm where meaningful, size plus metadata, or uploaded but not fully verified. Different algorithms are never compared. Optional destination re-download verification is deferred because it consumes bandwidth.

## Recovery and retry

Persistent demo transfer states are validated. Interrupted downloading/uploading/verifying work becomes `RetryPending` on restart. Real Google metadata requests retry only transient network, timeout, throttle and service-unavailable categories with bounded exponential backoff and jitter. An HTTP `Retry-After` delta or date is captured before the Google SDK creates its exception and takes precedence over calculated delay. Cancellation interrupts both API calls and backoff.
