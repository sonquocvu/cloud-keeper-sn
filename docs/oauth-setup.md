# Google OAuth setup

CloudKeeperSN dùng OAuth 2.0 Authorization Code cho ứng dụng máy tính, PKCE, trình duyệt hệ thống và callback loopback `127.0.0.1` trên cổng trống. Scope duy nhất là:

```text
https://www.googleapis.com/auth/drive.readonly
```

Không dùng OAuth client loại Web, service account, trình duyệt nhúng, biểu mẫu mật khẩu hay scope có quyền ghi.

## 1. Tải đúng file từ Google Cloud

1. Mở [Google Cloud Console](https://console.cloud.google.com/) và tạo hoặc chọn project.
2. Vào **APIs & Services > Library**, tìm **Google Drive API** và chọn **Enable**.
3. Cấu hình **Google Auth Platform / OAuth consent screen**: tên ứng dụng, email hỗ trợ, thông tin liên hệ và audience.
4. Nếu publishing status là **Testing**, thêm tài khoản Google sẽ dùng đăng nhập vào **Test users**.
5. Vào **Clients / Credentials**, chọn **Create OAuth client**.
6. Chọn application type **Desktop app**. Không chọn Web application.
7. Tạo client và chọn tải file JSON của client xuống máy.

File đúng có đối tượng top-level `installed`, Client ID kết thúc bằng `.apps.googleusercontent.com`, endpoint HTTPS của Google và redirect localhost. Không chỉnh sửa hoặc tự trích Client Secret cho workflow thông thường.

## 2. Nhập bằng Settings — phương thức khuyến nghị

1. Chạy CloudKeeperSN ở chế độ thực.
2. Mở **Cài đặt**.
3. Tại **Kết nối dịch vụ > Kết nối Google Drive**, chọn **Chọn file OAuth JSON**.
4. Trong hộp thoại **Chọn file OAuth của Google**, chọn file Desktop OAuth JSON vừa tải.
5. Xác nhận trạng thái chuyển thành **Đã cấu hình**. UI chỉ hiển thị loại ứng dụng, nguồn, Client ID đã che và thời điểm nhập; không hiển thị Client Secret hoặc JSON gốc.
6. Mở **Tài khoản** và chọn **Kết nối Google Drive**. Nút này được bật ngay, không cần khởi động lại.
7. Hoàn tất authorization trong trình duyệt hệ thống và kiểm tra quyền được yêu cầu là Drive read-only.

Nhập JSON chỉ cấu hình OAuth client của CloudKeeperSN; nó không đăng nhập tài khoản Google. Việc đăng nhập vẫn phải diễn ra trong trình duyệt để người dùng chọn tài khoản và đồng ý quyền.

### Validation

CloudKeeperSN đọc tối đa 1 MiB và chỉ ánh xạ các trường bắt buộc trong `installed`. Ứng dụng từ chối JSON trống/lỗi, top-level `web`, service account, thiếu client ID/secret, Client ID sai định dạng, endpoint không an toàn và redirect không phải HTTP loopback. Unknown fields được bỏ qua. Import lỗi không ghi đè cấu hình đang hoạt động và không đưa raw JSON/secret vào thông báo hoặc log.

File đã tải chỉ được đọc. CloudKeeperSN không sửa, xóa hoặc phụ thuộc vào vị trí gốc sau khi import.

## 3. Bảo vệ và vị trí lưu

Chỉ Client ID, Client Secret và thời điểm import được serialize vào protected credential store. Blob được Windows DPAPI bảo vệ với `CurrentUser`, ghi bằng tệp tạm rồi thay thế atomically tại:

```text
%LOCALAPPDATA%\CloudKeeperSN\Credentials\google-oauth-config\7ef2d595eb197422db1a41727b1729c201f78473abe14f3e1a901677c6abaf20.bin
```

Tên blob là SHA-256 của logical key, không chứa credential. Cấu hình không nằm trong SQLite, repository, cạnh executable, log hoặc diagnostic export. Chỉ cùng Windows user mới giải mã được. Token tài khoản Google được bảo vệ riêng tại `%LOCALAPPDATA%\CloudKeeperSN\Credentials\google-drive\`.

Nếu blob mất/hỏng/không giải mã được, UI báo **Cấu hình OAuth không hợp lệ** và cho phép nhập lại. Cấu hình hợp lệ cuối cùng chỉ thay đổi sau khi protected write thành công.

## 4. Thay đổi và xóa cấu hình

- **Thay đổi file OAuth**: CloudKeeperSN validate file mới trước, sau đó yêu cầu xác nhận. Khi được chấp thuận, tài khoản/token của OAuth client cũ bị xóa cục bộ trước khi lưu client mới; token giữa hai OAuth client không được tái sử dụng.
- **Xóa cấu hình**: yêu cầu xác nhận, ngắt tài khoản cục bộ, xóa authorization cache và protected client configuration. Lịch sử quét/sao lưu vẫn giữ nguyên.
- Hai thao tác không sửa dữ liệu Drive, không xóa file JSON gốc và không tự động revoke quyền trong Google Account. Muốn revoke từ xa, dùng [Google Account > Third-party connections](https://myaccount.google.com/connections).

## 5. Precedence

1. Cấu hình hợp lệ được nhập từ **Cài đặt**.
2. Cặp biến môi trường hoàn chỉnh dành cho phát triển/chẩn đoán.
3. Chưa cấu hình.

CloudKeeperSN không trộn Client ID và Client Secret từ nhiều nguồn. UI luôn hiển thị một trong các nguồn an toàn: **Đã nhập từ Cài đặt**, **Cấu hình môi trường phát triển**, hoặc **Chưa cấu hình**.

Developer fallback tùy chọn:

```powershell
$env:CLOUDKEEPERSN_GOOGLE_CLIENT_ID='YOUR_CLIENT_ID.apps.googleusercontent.com'
$env:CLOUDKEEPERSN_GOOGLE_CLIENT_SECRET='YOUR_CLIENT_SECRET'
```

Cả hai biến phải tồn tại và hợp lệ. Imported Settings configuration luôn có ưu tiên cao hơn.

## 6. Browser callback và session

Listener dùng random-port loopback `127.0.0.1`, state ngẫu nhiên được kiểm tra constant-time và timeout năm phút. Chỉ một sign-in chạy cùng lúc; người dùng có thể chọn **Hủy đăng nhập**. Sau token exchange, CloudKeeperSN gọi Drive `about.user`; chỉ khi truy cập thành công ứng dụng mới hiển thị **Đã kết nối**, tên và email.

Lần chạy sau, protected token được khôi phục/refresh và account identity được xác nhận lại. Token revoked hoặc không giải mã được chuyển UI sang **Cần đăng nhập lại** mà không thay đổi dữ liệu Drive.

## 7. Testing mode và phát hành

`drive.readonly` là restricted scope. Trong testing mode, chỉ Test users đã khai báo có thể đăng nhập và refresh token có thể bị giới hạn thời gian. Trước khi phát hành rộng rãi, chủ ứng dụng phải hoàn tất OAuth verification và các yêu cầu restricted-scope hiện hành của Google.

## 8. Xử lý lỗi nhanh

- **File không phải OAuth dành cho ứng dụng máy tính**: tạo client mới với loại Desktop app rồi tải JSON mới.
- **access_denied**: kiểm tra người dùng có từ chối consent hay chưa; nếu app đang Testing, kiểm tra Test users.
- **invalid_client**: tải lại JSON của đúng Desktop client và import bằng Settings.
- **redirect_uri_mismatch**: client thường sai loại; không dùng Web client.
- **Không mở được callback**: kiểm tra firewall/endpoint security có chặn loopback.
- **Đóng browser**: chọn **Hủy đăng nhập** hoặc đợi timeout năm phút.

## Không có Microsoft/OneDrive thật

Bản dựng này không đăng ký Microsoft identity, không gọi Microsoft Graph và không thay provider thật bằng fake data. Production dừng ở Google Drive chỉ đọc; chưa có truyền dữ liệu thật.
