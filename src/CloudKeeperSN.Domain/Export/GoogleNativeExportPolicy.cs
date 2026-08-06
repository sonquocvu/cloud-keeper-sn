namespace CloudKeeperSN.Domain.Export;

public sealed record ExportDecision(bool IsSupported, string? ExportMimeType, string? Extension, string VietnameseExplanation);

public static class GoogleNativeExportPolicy
{
    public const string GoogleDocument = "application/vnd.google-apps.document";
    public const string GoogleSpreadsheet = "application/vnd.google-apps.spreadsheet";
    public const string GooglePresentation = "application/vnd.google-apps.presentation";
    public const string GoogleDrawing = "application/vnd.google-apps.drawing";
    public const string GoogleShortcut = "application/vnd.google-apps.shortcut";

    public static ExportDecision Decide(string mimeType) => mimeType switch
    {
        GoogleDocument => Supported("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx", "Sẽ xuất Google Tài liệu thành tệp Word (.docx)."),
        GoogleSpreadsheet => Supported("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx", "Sẽ xuất Google Trang tính thành tệp Excel (.xlsx)."),
        GooglePresentation => Supported("application/vnd.openxmlformats-officedocument.presentationml.presentation", ".pptx", "Sẽ xuất Google Trang trình bày thành tệp PowerPoint (.pptx)."),
        GoogleDrawing => Supported("image/png", ".png", "Sẽ xuất Google Bản vẽ thành ảnh PNG; nội dung được xác minh theo siêu dữ liệu xuất."),
        GoogleShortcut => Unsupported("Lối tắt Google Drive được bỏ qua trong phiên bản này để tránh vòng lặp thư mục."),
        _ when mimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal) => Unsupported("Loại tệp gốc của Google này chưa được hỗ trợ và sẽ được bỏ qua."),
        _ => new ExportDecision(false, null, null, "Đây không phải tệp gốc của Google.")
    };

    private static ExportDecision Supported(string mimeType, string extension, string message) => new(true, mimeType, extension, message);
    private static ExportDecision Unsupported(string message) => new(false, null, null, message);
}

