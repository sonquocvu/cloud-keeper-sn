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
- **Application/provider:** parse/validate Desktop OAuth JSON, giới hạn kích thước, loại credential sai, endpoint/redirect, protected configuration restore, precedence, callback/state stages, token-persistence ordering, account identity, Drive read-only verification, chống sign-in đồng thời, hủy/cleanup cục bộ, retry sau lỗi và chống late-restore overwrite; capability chỉ đọc, pagination nhiều trang/trang rỗng/token lặp, query escaping, duplicate ID, scan đệ quy, shortcut, native/unsupported/unknown-size, progress/cancellation và retry transient/non-transient.
- **Infrastructure:** migration SQLite, email metadata, recovery, redaction, protected credential round-trip, plaintext không xuất hiện trên disk và lỗi giải mã an toàn.
- **App:** navigation, accessibility-facing states, Settings OAuth picker/import/replace/remove/help với fake picker/dialog/protected manager, cập nhật Connect ngay trong phiên, dispatcher-bound account publication, name/email/success state và `CanExecuteChanged`, folder picker loading/error/cancel, demo workflow và production metadata preview; partial scan không được publish; transfer thật luôn bị vô hiệu hóa.

Các test import dùng file reader, clock, protected store, authentication và dialog giả; không gọi Windows DPAPI, browser, Google hoặc file picker tương tác. Infrastructure tests riêng xác nhận protected blob không chứa plaintext trên disk và lỗi giải mã được xử lý an toàn.

## Live integration tests

Chưa có live integration test tự động. Nếu bổ sung, phải đặt ở suite opt-in riêng, dùng tài khoản test chuyên dụng, không ghi dữ liệu, không dùng tài khoản cá nhân production, và không chạy trong unit-test mặc định. Không được thêm test gọi download/export chỉ để kiểm chứng metadata checkpoint.

## Manual checks deferred

Automated suite không thể xác nhận trực quan hoặc tương tác thật với browser OAuth. Checklist thủ công nằm tại [ui-readiness-checklist.md](ui-readiness-checklist.md). Trong lần triển khai này, các mục đó được ghi rõ là **chưa thực hiện**, không được báo cáo như đã pass.
