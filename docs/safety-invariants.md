# Safety invariants

These rules are mandatory and take priority over convenience:

1. Google Drive is read-only in the MVP. No source delete, move, rename, or update capability is exposed.
2. OneDrive deletion is not exposed in the MVP.
3. Existing destination files are not overwritten by default.
4. This release cannot start a real transfer; future transfer work requires a completed preview and explicit confirmation.
5. “Sao lưu một chiều” is used in the UI; this is not bidirectional synchronization.
6. Source identity is provider account ID plus provider item ID, never filename alone.
7. Destination conflict names are deterministic and the chosen mapping is persisted.
8. A restarted application cannot infer completion from a temporary file or interrupted state.
9. A filename match is not verification. Only compatible checksum algorithms may be compared.
10. Google shortcuts are skipped until guarded shortcut traversal is deliberately implemented.
11. Unsupported Google-native types are skipped with a Vietnamese explanation.
12. Every future copy, skip, rename, export, warning, and failure decision must produce an activity event.
13. Tokens, secrets, authorization headers/codes, and sensitive query values must be redacted before persistence or export.
14. SQLite stores metadata and state, never full document content or image thumbnails.
15. Imported OAuth client configuration is accepted only from a validated Google Desktop `installed` JSON object and is DPAPI CurrentUser protected outside SQLite.
16. Replacing or removing OAuth configuration clears only local authorization for the previous client; it does not revoke access remotely or change Drive data automatically.
17. OAuth client secrets, original JSON, source paths and unmasked client IDs are never exposed through Settings view-model display state, logs or diagnostic exports.

The production Google provider exposes authentication/browse/metadata/export-planning capabilities only. It has no content-read, export execution, folder-create or write capability. DPAPI credential storage, migration constraints, redaction, pagination guards, complete-scan-only persistence and tests enforce these rules. A later destination/transfer pipeline must preserve them.
