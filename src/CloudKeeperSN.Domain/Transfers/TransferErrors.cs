namespace CloudKeeperSN.Domain.Transfers;

public enum TransferErrorCategory
{
    AuthenticationRequired,
    PermissionDenied,
    NetworkUnavailable,
    ProviderThrottled,
    SourceItemMissing,
    DestinationConflict,
    UnsupportedFileType,
    TemporaryStorageUnavailable,
    UploadSessionExpired,
    VerificationFailed,
    UnknownProviderError
}

public static class TransferErrorMessages
{
    public static string ToVietnamese(TransferErrorCategory category) => category switch
    {
        TransferErrorCategory.AuthenticationRequired => "Cần kết nối lại tài khoản.",
        TransferErrorCategory.PermissionDenied => "Ứng dụng không có quyền truy cập mục này.",
        TransferErrorCategory.NetworkUnavailable => "Không thể kết nối mạng. Vui lòng thử lại sau.",
        TransferErrorCategory.ProviderThrottled => "Dịch vụ lưu trữ đang giới hạn yêu cầu. Ứng dụng sẽ thử lại.",
        TransferErrorCategory.SourceItemMissing => "Không còn tìm thấy mục nguồn.",
        TransferErrorCategory.DestinationConflict => "Tên tại thư mục đích đang được một mục khác sử dụng.",
        TransferErrorCategory.UnsupportedFileType => "Loại tệp này chưa được hỗ trợ.",
        TransferErrorCategory.TemporaryStorageUnavailable => "Không thể sử dụng vùng lưu tạm của ứng dụng.",
        TransferErrorCategory.UploadSessionExpired => "Phiên tải lên đã hết hạn. Ứng dụng sẽ tạo phiên mới.",
        TransferErrorCategory.VerificationFailed => "Không thể xác minh tệp sau khi tải lên.",
        _ => "Đã xảy ra lỗi từ dịch vụ lưu trữ. Xem nhật ký chẩn đoán để biết chi tiết."
    };
}

