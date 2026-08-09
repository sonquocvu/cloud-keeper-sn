# UI readiness and accessibility checklist

## Implemented by inspection and automated tests

- minimum main window 1100 × 700 and collapsed navigation behavior retained;
- semantic light/dark theme plus Windows system-theme change listener;
- high-contrast theme uses `SystemColors`;
- visible loading/error/empty states and retry actions in folder picker;
- folder loading can be cancelled; dialog close cancels outstanding work;
- access keys on primary account, folder and preview actions;
- critical controls have Automation name/help text and live status regions;
- status never relies on color alone;
- long preview/history lists retain recycling virtualization;
- confirmation text wraps; disabled controls expose explanatory tooltip/text;
- production page removes the OneDrive destination step and states that no content is downloaded/exported/transferred.

## Manual Windows validation — not performed in this task

- [ ] OAuth system-browser connect, cancel, consent denial, reconnect and exact-account disconnect confirmation.
- [ ] Keyboard-only traversal, Enter/Space activation, Escape in dialogs and visible focus order.
- [ ] Narrator announcements for auth, folder loading/error, scan progress and disabled transfer explanation.
- [ ] Light, dark, system and Windows High Contrast themes.
- [ ] 100%, 125%, 150% and 200% scaling; long Vietnamese text; 1100 × 700 minimum; narrow collapsed navigation.
- [ ] Folder trees with thousands of entries, empty folders, duplicate names and slow/cancelled requests.
- [ ] Production preview with normal, native, unsupported, shortcut and unknown-size items.
- [ ] Verify no token/secret/code appears in `%LOCALAPPDATA%\CloudKeeperSN`, logs or diagnostic export except encrypted credential blobs.
- [ ] Inspect release executable icon in Explorer, taskbar, Alt+Tab and window chrome.

These unchecked items are release gates for a signed public build; automated success is not evidence that they passed.
