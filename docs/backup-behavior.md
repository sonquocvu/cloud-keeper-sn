# Backup behavior

## Direction and confirmation

The intended direction is Google Drive → a future destination. Google Drive is source-only. The current production scan builds a metadata-only preview containing file/folder counts, known estimated bytes, explicit unknown-size counts, warnings and export decisions.

No real destination or transfer exists in this build. The start action is disabled and explains that no file has been downloaded, exported or transmitted. OneDrive transfer remains available only in clearly labelled demo mode.

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

Persistent demo transfer states are validated. Interrupted downloading/uploading/verifying work becomes `RetryPending` on restart. Real Google metadata requests retry only transient categories with bounded exponential backoff and jitter; cancellation interrupts backoff. The current SDK mapping does not extract `Retry-After`, so future transfer work must add and test that header mapping before relying on it.
