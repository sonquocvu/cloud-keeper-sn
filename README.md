# CloudKeeperSN

CloudKeeperSN là ứng dụng WPF dành cho Windows 10/11, hướng tới quản lý dữ liệu đám mây cá nhân lâu dài. Phiên bản hiện tại cung cấp giao diện hoàn chỉnh để **trình diễn an toàn bằng dữ liệu giả lập** quy trình sao lưu một chiều từ Google Drive sang OneDrive.

## Hiện đã có

- kiến trúc Domain/Application/Infrastructure/provider độc lập;
- mô hình khả năng lưu trữ không phụ thuộc Google hoặc Microsoft;
- SQLite có migration, trạng thái phục hồi, lịch sử và ánh xạ danh tính;
- trình cung cấp Google Drive và OneDrive giả lập để kiểm thử ngoại tuyến;
- hệ thống thiết kế WPF dùng màu ngữ nghĩa, giao diện sáng/tối/theo Windows;
- khung ứng dụng responsive với năm trang: Tổng quan, Tài khoản, Sao lưu, Lịch sử, Cài đặt;
- kết nối/ngắt kết nối tài khoản giả lập;
- trình chọn thư mục, tạo thư mục OneDrive giả lập và bản xem trước bắt buộc;
- mô phỏng sao lưu có tiến độ, tạm dừng, tiếp tục, hủy, thử lại và kết quả xác minh;
- lịch sử có tìm kiếm/bộ lọc và xuất thông tin chẩn đoán đã ẩn dữ liệu nhạy cảm;
- lưu lựa chọn giao diện, cài đặt truyền dữ liệu và vị trí cửa sổ;
- kiểm thử tự động cho quy tắc an toàn, persistence, provider giả và view model UI.

## Chưa có

- đăng nhập Google/Microsoft thật;
- Google Drive API hoặc Microsoft Graph API;
- quét, tải xuống hoặc tải lên dữ liệu đám mây thật;
- xóa, di chuyển, ghi đè, cách ly hoặc đồng bộ hai chiều;
- phát hiện tệp trùng, ảnh tương tự, lịch chạy hoặc dọn dẹp.

Giao diện luôn dùng ngôn ngữ an toàn: **Google Drive là nguồn**, **OneDrive là nơi lưu bản sao**, **không xóa dữ liệu nguồn**, và **không ghi đè theo mặc định**.

## Yêu cầu phát triển

- Windows 10 build 19041 trở lên hoặc Windows 11;
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0);
- Visual Studio với workload “.NET desktop development”, hoặc dòng lệnh `dotnet`.

## Xây dựng và kiểm thử

```powershell
dotnet restore CloudKeeperSN.sln
dotnet build CloudKeeperSN.sln --configuration Release --no-restore
dotnet test CloudKeeperSN.sln --configuration Release --no-build
```

Unit test không cần tài khoản đám mây và không mở WPF.

## Chạy chế độ trình diễn

Debug build tự bật dữ liệu trình diễn. Release build chỉ bật khi được yêu cầu rõ ràng:

```powershell
$env:CLOUDKEEPERSN_DEMO_MODE='true'
$env:CLOUDKEEPERSN_DEMO_SCENARIO='Standard'
dotnet run --project src/CloudKeeperSN.App/CloudKeeperSN.App.csproj
```

Các kịch bản hợp lệ được mô tả trong [docs/demo-scenarios.md](docs/demo-scenarios.md). Giao diện luôn hiển thị nhãn **Chế độ trình diễn** khi dữ liệu giả đang hoạt động.

Không cần cấu hình OAuth cho giao diện giả lập. `.env.example` chỉ là mẫu an toàn; không commit client secret, token, mật khẩu hoặc authorization code.

## Dữ liệu cục bộ

- cơ sở dữ liệu: `%LOCALAPPDATA%\CloudKeeperSN\cloudkeeper.db`;
- nhật ký: `%LOCALAPPDATA%\CloudKeeperSN\Logs\`;
- bộ nhớ tạm: `%LOCALAPPDATA%\CloudKeeperSN\Cache\`.

**Xóa bộ nhớ tạm** chỉ xóa thư mục Cache riêng của CloudKeeperSN. Ngắt kết nối giả lập giữ nguyên lịch sử. Không có thao tác UI nào xóa dữ liệu Google Drive hoặc OneDrive.

Xem thêm: [kiến trúc](docs/architecture.md), [hệ thống thiết kế UI](docs/ui-design-system.md), [bất biến an toàn](docs/safety-invariants.md), [hành vi sao lưu](docs/backup-behavior.md), [kiểm thử](docs/testing.md).

