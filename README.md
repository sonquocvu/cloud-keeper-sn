# CloudKeeperSN

CloudKeeperSN là ứng dụng WPF cho Windows 10/11. Bản dựng hiện tại có hai chế độ tách biệt:

- **Chế độ thực:** đăng nhập Google Drive bằng trình duyệt hệ thống và tạo danh mục metadata toàn Drive trong SQLite. Google Drive là nguồn **chỉ đọc tuyệt đối**.
- **Chế độ trình diễn:** dùng provider giả để trình diễn toàn bộ luồng Google Drive → OneDrive mà không truy cập dịch vụ thật.

Trong chế độ thực chưa có đích lưu trữ và chưa có truyền dữ liệu. Nút **Bắt đầu sao lưu** bị vô hiệu hóa với giải thích rõ ràng. Ứng dụng không gọi API tải xuống/xuất nội dung và không cung cấp khả năng ghi, xóa, di chuyển hoặc đổi tên trên Google Drive.

## Chức năng hiện có

- OAuth 2.0 installed-app với PKCE, trình duyệt hệ thống và loopback receiver trên cổng trống;
- nhập trực tiếp file OAuth JSON loại Desktop app tại **Cài đặt > Kết nối dịch vụ**, không cần tự chép Client ID/secret;
- đúng một scope Google: `https://www.googleapis.com/auth/drive.readonly`;
- token được DPAPI bảo vệ theo Windows CurrentUser; SQLite chỉ lưu metadata tài khoản;
- trạng thái kết nối/hủy/lỗi/đăng nhập lại và ngắt kết nối có xác nhận;
- quét toàn bộ metadata không nằm trong thùng rác bằng `files.list` 1.000 mục/trang và đọc quota bằng `about.get`;
- snapshot SQLite dạng staging, chỉ công bố sau khi mọi trang và cấu trúc thư mục hoàn tất; lần quét thành công trước sống sót khi hủy/lỗi/tắt ứng dụng;
- nhận diện bằng file ID, giữ tên trùng/Unicode, phân loại tệp thường, thư mục, Google Workspace, shortcut, shared, thiếu kích thước và parent không hợp lệ;
- trang **Kế hoạch** duyệt cây thư mục của snapshot mới nhất, tìm kiếm/lọc metadata, chọn tệp hoặc thư mục, loại trừ hậu duệ và lưu kế hoạch cục bộ;
- quy tắc lựa chọn dùng file ID, tự đối chiếu snapshot mới và cảnh báo mục mới được kế thừa, mục biến mất hoặc quy tắc mất đích;
- kế hoạch Google Docs → `.docx`, Sheets → `.xlsx`, Slides → `.pptx`, Drawings → `.png`; loại native khác được đánh dấu không hỗ trợ;
- retry có exponential backoff, jitter, giới hạn, tôn trọng `Retry-After` và hủy;
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

Unit test không mở WPF và không cần tài khoản đám mây. Xem [Drive inventory](docs/drive-inventory.md) để biết phạm vi và checklist kiểm tra thủ công.

## Chạy với Google Drive thật

1. Trong Google Cloud Console, bật Google Drive API, cấu hình consent screen và tạo OAuth Client ID loại **Desktop app**.
2. Tải file JSON của OAuth client; không mở hoặc sao chép Client Secret vào CloudKeeperSN bằng tay.
3. Chạy ứng dụng ở chế độ thực:

```powershell
$env:CLOUDKEEPERSN_DEMO_MODE='false'
dotnet run --project src/CloudKeeperSN.App/CloudKeeperSN.App.csproj --configuration Release
```

4. Mở **Cài đặt > Kết nối dịch vụ > Kết nối Google Drive**, chọn **Chọn file OAuth JSON** và chọn file vừa tải.
5. Khi trạng thái thành **Đã cấu hình**, mở **Tài khoản** và chọn **Kết nối Google Drive**.

File nguồn không bị sửa/xóa và không cần tồn tại sau khi nhập. Cấu hình được DPAPI CurrentUser bảo vệ; việc nhập cấu hình chưa đăng nhập tài khoản, nên authorization vẫn diễn ra trong trình duyệt hệ thống. Khi consent screen ở trạng thái Testing, phải thêm tài khoản đăng nhập vào Test users. Xem chi tiết trong [OAuth setup](docs/oauth-setup.md).

Hai biến `CLOUDKEEPERSN_GOOGLE_CLIENT_ID` và `CLOUDKEEPERSN_GOOGLE_CLIENT_SECRET` vẫn được hỗ trợ như một cặp fallback dành cho phát triển/chẩn đoán. Cấu hình được nhập từ Settings luôn có ưu tiên cao hơn; các trường từ nhiều nguồn không được trộn.

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
- cấu hình OAuth đã nhập và mã hóa: `%LOCALAPPDATA%\CloudKeeperSN\Credentials\google-oauth-config\`
- token đã mã hóa: `%LOCALAPPDATA%\CloudKeeperSN\Credentials\google-drive\`
- log/cache: `%LOCALAPPDATA%\CloudKeeperSN\Logs\` và `Cache\`

Ngắt kết nối cố gắng revoke token và luôn xóa token cache cục bộ; không thay đổi tệp đám mây.

Tài liệu: [kiến trúc](docs/architecture.md), [Drive inventory](docs/drive-inventory.md), [kế hoạch lựa chọn](docs/backup-selection-plan.md), [provider Google chỉ đọc](docs/google-drive-readonly.md), [hành vi sao lưu](docs/backup-behavior.md), [bất biến an toàn](docs/safety-invariants.md), [kiểm thử](docs/testing.md), [UI/accessibility checklist](docs/ui-readiness-checklist.md).
