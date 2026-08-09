# CloudKeeperSN

CloudKeeperSN là ứng dụng WPF cho Windows 10/11. Bản dựng hiện tại có hai chế độ tách biệt:

- **Chế độ thực:** đăng nhập Google Drive bằng trình duyệt hệ thống, duyệt thư mục, quét đệ quy siêu dữ liệu và lập bản xem trước. Google Drive là nguồn **chỉ đọc tuyệt đối**.
- **Chế độ trình diễn:** dùng provider giả để trình diễn toàn bộ luồng Google Drive → OneDrive mà không truy cập dịch vụ thật.

Trong chế độ thực chưa có đích lưu trữ và chưa có truyền dữ liệu. Nút **Bắt đầu sao lưu** bị vô hiệu hóa với giải thích rõ ràng. Ứng dụng không gọi API tải xuống/xuất nội dung và không cung cấp khả năng ghi, xóa, di chuyển hoặc đổi tên trên Google Drive.

## Chức năng hiện có

- OAuth 2.0 installed-app với PKCE, trình duyệt hệ thống và loopback receiver trên cổng trống;
- đúng một scope Google: `https://www.googleapis.com/auth/drive.readonly`;
- token được DPAPI bảo vệ theo Windows CurrentUser; SQLite chỉ lưu metadata tài khoản;
- trạng thái kết nối/hủy/lỗi/đăng nhập lại và ngắt kết nối có xác nhận;
- duyệt `Drive của tôi` theo trang, hủy/thử lại, chặn continuation token lặp;
- quét đệ quy metadata-only, nhận diện duplicate theo item ID, shortcut, kích thước chưa rõ và lỗi một phần;
- kế hoạch Google Docs → `.docx`, Sheets → `.xlsx`, Slides → `.pptx`, Drawings → `.png`; loại native khác được đánh dấu không hỗ trợ;
- retry có exponential backoff, jitter, giới hạn và tôn trọng hủy;
- theme sáng/tối/theo Windows/high contrast, nhãn trạng thái bằng chữ và hỗ trợ bàn phím;
- SQLite có migration, lịch sử, cài đặt và chẩn đoán được redaction;
- bộ provider giả và luồng sao lưu trình diễn ngoại tuyến.

## Yêu cầu

- Windows 10 build 19041+ hoặc Windows 11;
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Build và test

```powershell
dotnet restore CloudKeeperSN.sln
dotnet build CloudKeeperSN.sln --configuration Release --no-restore
dotnet test CloudKeeperSN.sln --configuration Release --no-build
```

Unit test không mở WPF và không cần tài khoản đám mây.

## Chạy với Google Drive thật

Tạo OAuth client loại **Desktop app**, bật Google Drive API, rồi đặt biến môi trường trong phiên PowerShell. Không commit giá trị thật:

```powershell
$env:CLOUDKEEPERSN_DEMO_MODE='false'
$env:CLOUDKEEPERSN_GOOGLE_CLIENT_ID='...apps.googleusercontent.com'
$env:CLOUDKEEPERSN_GOOGLE_CLIENT_SECRET='...'
dotnet run --project src/CloudKeeperSN.App/CloudKeeperSN.App.csproj --configuration Release
```

Xem quy trình đầy đủ và yêu cầu Google verification trong [OAuth setup](docs/oauth-setup.md). Ứng dụng không đọc file `.env` tự động; [.env.example](.env.example) chỉ liệt kê tên biến.

## Chế độ trình diễn

Debug build mặc định dùng demo; Release chỉ dùng demo khi được yêu cầu:

```powershell
$env:CLOUDKEEPERSN_DEMO_MODE='true'
$env:CLOUDKEEPERSN_DEMO_SCENARIO='Standard'
dotnet run --project src/CloudKeeperSN.App/CloudKeeperSN.App.csproj
```

Giao diện luôn hiện nhãn **Chế độ trình diễn** khi provider giả hoạt động. Xem [demo scenarios](docs/demo-scenarios.md).

## Dữ liệu cục bộ

- SQLite: `%LOCALAPPDATA%\CloudKeeperSN\cloudkeeper.db`
- token đã mã hóa: `%LOCALAPPDATA%\CloudKeeperSN\Credentials\google-drive\`
- log/cache: `%LOCALAPPDATA%\CloudKeeperSN\Logs\` và `Cache\`

Ngắt kết nối cố gắng revoke token và luôn xóa token cache cục bộ; không thay đổi tệp đám mây.

Tài liệu: [kiến trúc](docs/architecture.md), [provider Google chỉ đọc](docs/google-drive-readonly.md), [hành vi sao lưu](docs/backup-behavior.md), [bất biến an toàn](docs/safety-invariants.md), [kiểm thử](docs/testing.md), [UI/accessibility checklist](docs/ui-readiness-checklist.md).
