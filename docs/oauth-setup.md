# Google OAuth setup

## Google Cloud Console

1. Tạo/chọn Google Cloud project và bật **Google Drive API**.
2. Cấu hình OAuth consent screen, tên ứng dụng, email hỗ trợ và đối tượng người dùng phù hợp.
3. Thêm đúng scope `https://www.googleapis.com/auth/drive.readonly`.
4. Tạo OAuth client loại **Desktop app**. Không dùng client loại Web.
5. Sao chép client ID và client secret vào biến môi trường cục bộ; không đưa JSON credentials vào repository.

```powershell
$env:CLOUDKEEPERSN_DEMO_MODE='false'
$env:CLOUDKEEPERSN_GOOGLE_CLIENT_ID='...apps.googleusercontent.com'
$env:CLOUDKEEPERSN_GOOGLE_CLIENT_SECRET='...'
```

Google installed-app flow mở trình duyệt hệ thống và dùng loopback receiver `127.0.0.1` trên một cổng trống do thư viện chọn. Không cấu hình fixed redirect URI trong ứng dụng. Flow dùng PKCE và không bao giờ yêu cầu mật khẩu Google trong CloudKeeperSN.

Nếu thiếu client ID/secret, nút kết nối bị vô hiệu hóa và UI giải thích cấu hình còn thiếu. Chỉ một lần đăng nhập tương tác được phép chạy tại một thời điểm; người dùng có thể hủy.

## Scope và verification

`drive.readonly` là restricted scope. Nó cho phép xem/tải toàn bộ tệp Drive mà người dùng có thể truy cập; CloudKeeperSN hiện chỉ dùng phần metadata/list và chưa gọi download/export. Trước khi phát hành rộng rãi, chủ ứng dụng phải hoàn tất OAuth app verification của Google và có thể phải thực hiện security assessment theo chính sách restricted-scope hiện hành. Trong testing mode, giới hạn test users và thời hạn token của Google vẫn áp dụng.

Không thay scope bằng `drive.file`: scope đó chỉ phù hợp với tệp do ứng dụng tạo/mở qua picker và không đáp ứng việc người dùng chọn/quét cây thư mục hiện có cho backup.

## Token và ngắt kết nối

- Google token JSON được serialize rồi mã hóa bằng Windows DPAPI `CurrentUser`.
- Mỗi blob nằm ngoài repo tại `%LOCALAPPDATA%\CloudKeeperSN\Credentials\google-drive\`; tên file là hash, không chứa token key rõ.
- SQLite chỉ lưu provider/account ID, tên hiển thị, email và thời điểm kết nối.
- Ngắt kết nối yêu cầu xác nhận chính xác tài khoản, cố gắng revoke token, rồi xóa cache cục bộ ngay cả khi revoke từ xa lỗi.
- Token hết hạn/revoked hoặc DPAPI không giải mã được sẽ chuyển UI sang trạng thái cần đăng nhập lại; không ghi token hay authorization code vào log.

## Không có Microsoft/OneDrive thật

Bản dựng này không đăng ký Microsoft identity, không gọi Microsoft Graph và không thay provider thật bằng fake provider một cách âm thầm. OneDrive chỉ xuất hiện trong chế độ demo. Production dừng ở bản xem trước Google Drive chỉ đọc.
