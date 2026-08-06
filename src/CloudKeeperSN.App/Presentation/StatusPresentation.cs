using CloudKeeperSN.Domain.Transfers;
using CloudKeeperSN.App.Development;

namespace CloudKeeperSN.App.Presentation;

public enum StatusTone
{
    Neutral,
    Information,
    Success,
    Warning,
    Error
}

public sealed record StatusPresentation(string Text, StatusTone Tone, string IconGlyph);

public static class VietnamesePresentationMapper
{
    public static StatusPresentation TransferState(TransferState state) => state switch
    {
        CloudKeeperSN.Domain.Transfers.TransferState.Discovered => Info("Đã phát hiện"),
        CloudKeeperSN.Domain.Transfers.TransferState.Planned => Info("Đã lên kế hoạch"),
        CloudKeeperSN.Domain.Transfers.TransferState.Waiting => Info("Đang chờ"),
        CloudKeeperSN.Domain.Transfers.TransferState.Downloading => Info("Đang đọc từ Google Drive"),
        CloudKeeperSN.Domain.Transfers.TransferState.Uploading => Info("Đang lưu bản sao lên OneDrive"),
        CloudKeeperSN.Domain.Transfers.TransferState.Verifying => Info("Đang xác minh"),
        CloudKeeperSN.Domain.Transfers.TransferState.Completed => Success("Đã hoàn tất"),
        CloudKeeperSN.Domain.Transfers.TransferState.Skipped => Neutral("Đã bỏ qua"),
        CloudKeeperSN.Domain.Transfers.TransferState.Paused => Warning("Đã tạm dừng"),
        CloudKeeperSN.Domain.Transfers.TransferState.RetryPending => Warning("Đang chờ thử lại"),
        CloudKeeperSN.Domain.Transfers.TransferState.Failed => Error("Chưa hoàn tất"),
        CloudKeeperSN.Domain.Transfers.TransferState.Cancelled => Neutral("Đã hủy"),
        _ => Neutral("Chưa xác định")
    };

    public static StatusPresentation Verification(VerificationLevel level) => level switch
    {
        VerificationLevel.VerifiedByStrongHash => Success("Đã xác minh bằng mã kiểm tra mạnh"),
        VerificationLevel.VerifiedByProviderHash => Success("Đã xác minh bằng bằng chứng của dịch vụ"),
        VerificationLevel.VerifiedBySizeAndMetadata => Warning("Đã xác minh bằng dung lượng và thông tin tệp"),
        VerificationLevel.UploadedButNotFullyVerified => Warning("Đã tải lên nhưng chưa xác minh đầy đủ"),
        VerificationLevel.VerificationFailed => Error("Xác minh chưa thành công"),
        _ => Neutral("Chưa xác minh")
    };

    public static StatusPresentation RunStatus(DemoRunStatus status) => status switch
    {
        DemoRunStatus.Running => Info("Đang sao lưu"),
        DemoRunStatus.Completed => Success("Đã hoàn tất"),
        DemoRunStatus.CompletedWithWarnings => Warning("Hoàn tất với cảnh báo"),
        DemoRunStatus.Failed => Error("Chưa hoàn tất"),
        DemoRunStatus.Cancelled => Neutral("Đã hủy"),
        _ => Neutral("Chưa xác định")
    };

    private static StatusPresentation Info(string text) => new(text, StatusTone.Information, "\uE946");
    private static StatusPresentation Success(string text) => new(text, StatusTone.Success, "\uE73E");
    private static StatusPresentation Warning(string text) => new(text, StatusTone.Warning, "\uE7BA");
    private static StatusPresentation Error(string text) => new(text, StatusTone.Error, "\uEA39");
    private static StatusPresentation Neutral(string text) => new(text, StatusTone.Neutral, "\uE946");
}
