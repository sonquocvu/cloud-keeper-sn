namespace CloudKeeperSN.Domain.Storage;

public enum ProviderFailureCategory
{
    AuthenticationRequired,
    AuthorizationCancelled,
    AuthorizationRevoked,
    PermissionDenied,
    NetworkUnavailable,
    RequestTimedOut,
    ProviderThrottled,
    ServiceUnavailable,
    SourceFolderMissing,
    SourceItemInaccessible,
    InvalidOrUnsupportedItem,
    CredentialProtectionFailed,
    InvalidProviderResponse,
    UnknownProviderError
}

public sealed class ProviderOperationException : Exception
{
    public ProviderOperationException(ProviderFailureCategory category, string message, Exception? innerException = null, TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        Category = category;
        RetryAfter = retryAfter;
    }

    public ProviderFailureCategory Category { get; }
    public TimeSpan? RetryAfter { get; }
}

public static class ProviderFailureMessages
{
    public static string ToVietnamese(ProviderFailureCategory category) => category switch
    {
        ProviderFailureCategory.AuthenticationRequired or ProviderFailureCategory.AuthorizationRevoked =>
            "Phiên Google Drive không còn hợp lệ. Dữ liệu nguồn vẫn an toàn và chưa bị thay đổi. Vui lòng đăng nhập lại.",
        ProviderFailureCategory.AuthorizationCancelled =>
            "Bạn đã hủy đăng nhập. Không có dữ liệu Google Drive nào bị thay đổi; bạn có thể thử kết nối lại khi sẵn sàng.",
        ProviderFailureCategory.PermissionDenied =>
            "CloudKeeperSN không có quyền đọc mục này. Dữ liệu Google Drive vẫn an toàn. Vui lòng chọn mục khác hoặc kiểm tra quyền truy cập.",
        ProviderFailureCategory.NetworkUnavailable =>
            "Không thể tiếp tục vì kết nối mạng bị gián đoạn. Dữ liệu trên Google Drive vẫn an toàn và chưa bị thay đổi. Vui lòng kiểm tra kết nối rồi thử lại.",
        ProviderFailureCategory.RequestTimedOut =>
            "Google Drive phản hồi quá chậm. Dữ liệu nguồn chưa bị thay đổi. Vui lòng thử lại.",
        ProviderFailureCategory.ProviderThrottled =>
            "Google Drive đang tạm giới hạn yêu cầu. CloudKeeperSN sẽ chờ an toàn trước khi thử lại.",
        ProviderFailureCategory.ServiceUnavailable =>
            "Google Drive đang tạm thời không khả dụng. Dữ liệu nguồn vẫn an toàn. Vui lòng thử lại sau.",
        ProviderFailureCategory.SourceFolderMissing =>
            "Không còn tìm thấy thư mục nguồn đã chọn. Dữ liệu khác trên Google Drive không bị thay đổi. Vui lòng chọn lại thư mục.",
        ProviderFailureCategory.SourceItemInaccessible =>
            "Một mục nguồn không còn truy cập được. Bản quét chưa hoàn tất và không có dữ liệu Google Drive nào bị thay đổi.",
        ProviderFailureCategory.InvalidOrUnsupportedItem =>
            "Google Drive trả về một mục chưa được hỗ trợ. Mục đó sẽ không được sao lưu và dữ liệu nguồn không bị thay đổi.",
        ProviderFailureCategory.CredentialProtectionFailed =>
            "Không thể khôi phục thông tin đăng nhập được bảo vệ. Vui lòng đăng nhập lại; dữ liệu Google Drive vẫn an toàn.",
        _ => "Không thể hoàn tất yêu cầu Google Drive. Dữ liệu nguồn vẫn an toàn và chưa bị thay đổi. Vui lòng thử lại."
    };
}
