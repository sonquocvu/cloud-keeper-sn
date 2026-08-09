# CloudKeeperSN UI design system

## Visual direction

CloudKeeperSN should feel trustworthy, calm, modern, practical, and protective without being alarming. It uses restrained blue accents, quiet surfaces, rounded 7–14 px corners, minimal shadows, and clear text hierarchy. Avoid decorative gradients, glass, neon color, or dense raw-data tables.

## Semantic resources

Views must use `DynamicResource` semantic brushes from `UI/Themes/LightTheme.xaml`, `DarkTheme.xaml`, and system colors supplied by `HighContrastTheme.xaml`:

- `BackgroundPrimary`, `BackgroundSecondary`
- `SurfacePrimary`, `SurfaceElevated`
- `BorderSubtle`
- `TextPrimary`, `TextSecondary`, `TextDisabled`
- `AccentPrimary`, `AccentHover`, `AccentSoft`
- `Success`, `Warning`, `Error`, `Information` and their `*Soft` variants
- navigation-specific semantic brushes

Do not add raw colors to page views. The only intentional fixed color is white text on the accent-filled primary button/product mark.

`Spacing.xaml` follows an 8 px rhythm where practical (`4, 8, 12, 16, 24, 32`). Cards use 20 px padding. `Typography.xaml` defines application title, page title, section title, card title, body, supporting, and caption styles using Segoe UI for Vietnamese rendering.

## Themes

The default is **Theo cài đặt Windows**. `ThemeService` resolves the Windows app-theme preference on startup and on Windows preference changes, swaps one resource dictionary without recreating views, and persists `System`, `Light`, or `Dark` in SQLite. Windows High Contrast overrides the palette with `SystemColors`. Dark mode avoids pure black/white.

All future controls must consume semantic resources. Never create a separate dark copy of a page.

## Controls and states

Reusable controls include:

- `SummaryCard`
- `StatusBadge`
- `ProviderAccountCard`
- `InlineNotification`
- `EmptyState`
- `LoadingIndicator`
- themed confirmation and folder-picker dialogs

Standard styles cover enabled, disabled, hover, pressed, and keyboard focus. Use primary buttons for the single preferred next action; normal buttons for secondary actions; danger styling only for cancelling a run or disconnecting a local account. Keep destructive-looking actions visually separated.

Status color is always paired with Vietnamese text:

- green: completed or verified;
- amber: warning, conflict, limited verification, paused;
- red: failed or immediate attention;
- blue: informational or processing;
- gray: skipped, disconnected, cancelled, unavailable.

Never bind an enum directly into visible text. Use `VietnamesePresentationMapper`.

## Vietnamese terminology

Use consistently:

- “Sao lưu một chiều”
- “Google Drive là nguồn”
- “OneDrive là nơi lưu bản sao”
- “Không xóa dữ liệu nguồn”
- “Không ghi đè theo mặc định”
- “Quét và xem trước”
- “Bắt đầu sao lưu”

Do not imply bidirectional synchronization, moving, cleanup, duplicate deletion, or other unavailable features.

## Accessibility and responsiveness

- minimum window: 1100 × 700 logical pixels;
- navigation collapses to icons below 1180 px and retains tooltips/accessible names;
- visible focus visual on interactive controls;
- access keys on important actions;
- logical tab order and descriptive `AutomationProperties.Name`/`HelpText` on critical controls;
- status is communicated with text, not color alone;
- normal body text is 14 px; captions are 12 px;
- long preview/history lists use recycling virtualization;
- fake operations are asynchronous and cancellable; progress updates occur per meaningful item/operation, not every byte;
- animation is not required to understand state. The indeterminate loading bar is supplemental to visible loading text.

## Manual visual inspection

No screenshot or interactive GUI validation is performed by the automated suite. On a Windows desktop, inspect light/dark/system modes at 100%, 125%, and 150% scaling; verify the 1100 × 700 minimum, collapsed navigation, keyboard focus, dialogs, long lists, and high-contrast readability.
