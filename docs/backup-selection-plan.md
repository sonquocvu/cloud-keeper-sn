# Local Google Drive backup selection plan

## Purpose and boundary

The **Kế hoạch** page opens only the latest successful Google Drive inventory already stored in SQLite. It provides a hierarchy tree plus a virtualized searchable/filterable list. Filters cover selected items, items requiring review, regular files, folders, Google Workspace files, shortcuts and shared items.

This page does not call Google Drive, read content, export Workspace documents, connect to OneDrive, choose a destination or transfer data. Saving means only that the local selection rules were persisted. It is never evidence that data has been backed up.

## Selection semantics

Each rule contains the Google file ID, `Include` or `Exclude`, last-known item kind and last-known display name. File ID is the identity; the name is retained only to explain a missing target.

- Including a file selects that eligible file.
- Including a folder applies to eligible descendants without expanding the rule into one row per file.
- Excluding a descendant file or folder overrides an included ancestor.
- The closest explicit rule in the parent chain wins.
- Selecting or deselecting a folder clears older descendant overrides beneath that folder, matching normal tree-selection behavior; new exclusions can then be added deliberately.
- The UI reports all selected non-folder items and separately reports how many are currently backup-eligible. Folders do not contribute bytes. Selected eligible regular/Workspace files contribute their supplied sizes; missing sizes are counted separately.
- Shortcuts, unsupported items and unresolved relationships remain visible for review and are not silently treated as transferable.

## Persistence and reconciliation

Migration 4 creates `backup_selection_plans` and `backup_selection_rules`. There is one editable plan per Google provider-account identity. The plan stores its source snapshot ID, timestamps and compact rules; no tokens, secrets, file content, OneDrive path or transfer status are stored.

On reopen, the plan is evaluated against the newest complete snapshot. If that differs from the saved baseline, CloudKeeperSN reports:

- new items now inherited by a selected folder;
- previously selected item IDs absent from the new snapshot;
- rules whose target ID is absent.

Renames and moves remain selected when the stable ID survives. Missing rules are retained for review rather than silently deleted. Save verifies that the snapshot has not changed since the editor loaded. A newly completed scan cannot discard unsaved edits.

The reconciliation panel offers an explicit local action to remove rules whose target no longer exists. Nothing is removed automatically, and this cleanup changes only the unsaved local draft until **Lưu kế hoạch** is pressed.

## Manual Windows verification

1. Open **Kế hoạch** after a successful real scan and confirm the snapshot time and approximate 4,109-item inventory.
2. Expand several folders and verify duplicate/Unicode names and paths.
3. Search by a filename/path and exercise every filter.
4. Select a folder and confirm eligible descendant count and known size update.
5. Exclude one child file and one child folder; confirm the totals decrease.
6. Open **Cần kiểm tra** and inspect shortcuts, unsupported or unresolved items and their explanations.
7. Rename the plan, save it, restart CloudKeeperSN and confirm rules/totals reopen.
8. Create or move a harmless test item in Google Drive outside CloudKeeperSN, run a new metadata scan, and reopen the plan.
9. Confirm newly inherited, missing and missing-rule counts are explicit before accepting the newer baseline.
10. Confirm the UI never says the selection was backed up and no OneDrive destination appears.
11. Confirm no Google file was downloaded, exported, created, modified, renamed, moved, trashed or deleted by planning.
