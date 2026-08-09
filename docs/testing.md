# Testing

## Automated commands

```powershell
dotnet restore CloudKeeperSN.sln
dotnet build CloudKeeperSN.sln --configuration Release --no-restore
dotnet test CloudKeeperSN.sln --configuration Release --no-build
```

Các test mặc định hoàn toàn ngoại tuyến, không mở GUI và không cần credentials.

## Coverage trọng yếu

- **Domain:** đường dẫn/identity, tên conflict xác định, native export policy, checksum, retry, state machine, redaction và cycle guard.
- **Application/provider:** OAuth lifecycle; capability chỉ đọc; inventory rỗng/một trang/nhiều trang/token lặp; tên trùng/Unicode; thiếu size/checksum; regular/folder/Workspace/shortcut/shared/trash; missing parent/cycle/depth 5.000; cancellation giữa trang; concurrent scan; auth revoked; database failure/retry; transient/non-transient retry, `Retry-After`, và hủy trong backoff.
- **Infrastructure:** migration SQLite và dữ liệu account cũ; staged/complete inventory, item/path/quota round-trip, batching, failed snapshot preservation, startup interruption recovery, schema không có token/secret; recovery/redaction/protected credential.
- **App:** production scan enable/disable, progress item count, busy/cancel/retry command state, property/command notifications, dispatcher use, previous-summary preservation, immediate dashboard refresh, navigation/accessibility, Settings OAuth, folder picker and isolated demo workflow. Transfer thật luôn bị vô hiệu hóa.

Các test import dùng file reader, clock, protected store, authentication và dialog giả; không gọi Windows DPAPI, browser, Google hoặc file picker tương tác. Infrastructure tests riêng xác nhận protected blob không chứa plaintext trên disk và lỗi giải mã được xử lý an toàn.

## Live integration tests

Chưa có live integration test tự động. Nếu bổ sung, phải đặt ở suite opt-in riêng, dùng tài khoản test chuyên dụng, không ghi dữ liệu, không dùng tài khoản cá nhân production, và không chạy trong unit-test mặc định. Không được thêm test gọi download/export chỉ để kiểm chứng metadata checkpoint.

## Manual checks deferred

Automated suite không thể xác nhận trực quan, tương tác thật với browser OAuth, số mục thật hoặc quota thật. Checklist scan nằm tại [drive-inventory.md](drive-inventory.md#manual-windows-verification) và checklist UI chung tại [ui-readiness-checklist.md](ui-readiness-checklist.md). Các mục đó phải được ghi rõ là **chưa thực hiện** cho đến khi developer chạy bằng tài khoản đã kết nối.
