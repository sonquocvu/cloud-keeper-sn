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
- **Application/provider:** OAuth states, thiếu cấu hình, chống sign-in đồng thời, hủy/revoke, capability chỉ đọc, pagination nhiều trang/trang rỗng/token lặp, query escaping, duplicate ID, scan đệ quy, shortcut, native/unsupported/unknown-size, progress/cancellation và retry transient/non-transient.
- **Infrastructure:** migration SQLite, email metadata, recovery, redaction, protected credential round-trip, plaintext không xuất hiện trên disk và lỗi giải mã an toàn.
- **App:** navigation, accessibility-facing states, folder picker loading/error/cancel, demo workflow và production metadata preview; partial scan không được publish; transfer thật luôn bị vô hiệu hóa.

## Live integration tests

Chưa có live integration test tự động. Nếu bổ sung, phải đặt ở suite opt-in riêng, dùng tài khoản test chuyên dụng, không ghi dữ liệu, không dùng tài khoản cá nhân production, và không chạy trong unit-test mặc định. Không được thêm test gọi download/export chỉ để kiểm chứng metadata checkpoint.

## Manual checks deferred

Automated suite không thể xác nhận trực quan hoặc tương tác thật với browser OAuth. Checklist thủ công nằm tại [ui-readiness-checklist.md](ui-readiness-checklist.md). Trong lần triển khai này, các mục đó được ghi rõ là **chưa thực hiện**, không được báo cáo như đã pass.
