# CloudKeeperSN

CloudKeeperSN là ứng dụng WPF dành cho Windows 10/11, hướng tới quản lý dữ liệu đám mây cá nhân lâu dài. Phiên bản hiện tại là **Checkpoint 1**: nền tảng kiến trúc an toàn cho “Sao lưu một chiều” từ Google Drive sang OneDrive.

## Trạng thái hiện tại

Đã có:

- cấu trúc Domain/Application/Infrastructure/provider độc lập;
- mô hình khả năng lưu trữ không phụ thuộc Google hoặc Microsoft;
- quy tắc đường dẫn, tên OneDrive, tên xung đột ổn định và ánh xạ tệp Google gốc;
- máy trạng thái truyền tệp, quyết định thử lại và mức xác minh;
- SQLite có migration, WAL, trạng thái phục hồi, lịch sử và ánh xạ danh tính;
- trình cung cấp Google Drive và OneDrive mô phỏng để kiểm thử ngoại tuyến;
- khung ứng dụng WPF MVVM hoàn toàn bằng tiếng Việt;
- kiểm thử tự động cho các quy tắc an toàn cốt lõi.

Chưa có trong Checkpoint 1:

- đăng nhập OAuth hoặc truy cập tài khoản đám mây thật;
- trình duyệt thư mục Google Drive/OneDrive thật;
- quét, xem trước và truyền tệp thật;
- điều khiển tạm dừng/tiếp tục/hủy/thử lại hoàn chỉnh trên giao diện;
- xuất nhật ký chẩn đoán;
- đồng bộ hai chiều, lên lịch, tìm trùng lặp, dọn dẹp hoặc xóa dữ liệu.

Các nút có thể tác động tới dịch vụ thật được vô hiệu hóa cho tới khi checkpoint tương ứng được triển khai.

## Yêu cầu phát triển

- Windows 10 phiên bản 2004 (build 19041) trở lên hoặc Windows 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Visual Studio 2026 với workload “.NET desktop development”, hoặc dòng lệnh `dotnet`.
- PowerShell 7 hoặc Windows PowerShell để chạy các lệnh mẫu.

## Xây dựng và kiểm thử

```powershell
dotnet restore CloudKeeperSN.sln
dotnet build CloudKeeperSN.sln --configuration Release --no-restore
dotnet test CloudKeeperSN.sln --configuration Release --no-build
```

Không cần tài khoản Google Drive hoặc OneDrive để chạy unit test. Không khởi chạy giao diện trong kiểm thử tự động.

## Cấu hình OAuth

Checkpoint 1 chỉ cung cấp mẫu cấu hình an toàn; không có client ID thật. Sao chép `.env.example` thành một tệp cục bộ không được Git theo dõi và làm theo [docs/oauth-setup.md](docs/oauth-setup.md). Không đặt client secret, token, mật khẩu hoặc authorization code trong mã nguồn hay nhật ký.

## Dữ liệu cục bộ

Ứng dụng dùng các vị trí sau:

- cơ sở dữ liệu: `%LOCALAPPDATA%\CloudKeeperSN\cloudkeeper.db`;
- nhật ký dự kiến: `%LOCALAPPDATA%\CloudKeeperSN\Logs\` (chưa triển khai xuất nhật ký);
- dữ liệu tạm dự kiến: `%LOCALAPPDATA%\CloudKeeperSN\Cache\` (chưa dùng trong Checkpoint 1).

Để ngắt kết nối tài khoản ở các checkpoint sau, dùng **Tài khoản → Ngắt kết nối tài khoản**; thao tác này phải xóa token đã mã hóa. Chức năng đó chưa hoạt động vì OAuth chưa được triển khai. Để xóa dữ liệu phát triển hiện tại, đóng ứng dụng rồi sao lưu và xóa riêng thư mục `%LOCALAPPDATA%\CloudKeeperSN`; không xóa thư mục rộng hơn.

Xem thêm: [kiến trúc](docs/architecture.md), [bất biến an toàn](docs/safety-invariants.md), [hành vi sao lưu](docs/backup-behavior.md), [kiểm thử](docs/testing.md).

