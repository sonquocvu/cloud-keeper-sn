using CloudKeeperSN.Domain.Backup;

namespace CloudKeeperSN.Application.Planning;

public enum IncrementalDecisionKind
{
    CopyNew,
    SkipUnchanged,
    CopyUpdatedSafely,
    RecreateMissingDestination
}

public sealed record IncrementalDecision(IncrementalDecisionKind Kind, string VietnameseExplanation);

public sealed class IncrementalDecisionService
{
    public IncrementalDecision Decide(
        SourceDestinationMapping? mapping,
        string currentSourceFingerprint,
        bool recordedDestinationStillExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentSourceFingerprint);
        if (mapping is null)
        {
            return new IncrementalDecision(IncrementalDecisionKind.CopyNew, "Mục này chưa từng được CloudKeeperSN sao lưu.");
        }

        if (!recordedDestinationStillExists)
        {
            return new IncrementalDecision(IncrementalDecisionKind.RecreateMissingDestination, "Bản sao đã ghi nhận không còn ở đích; sẽ tạo lại mà không ghi đè mục khác.");
        }

        if (string.Equals(mapping.SourceFingerprint, currentSourceFingerprint, StringComparison.Ordinal))
        {
            return new IncrementalDecision(IncrementalDecisionKind.SkipUnchanged, "Mục đã được sao lưu và nguồn không thay đổi.");
        }

        return new IncrementalDecision(IncrementalDecisionKind.CopyUpdatedSafely, "Nguồn đã thay đổi; sẽ tạo một bản cập nhật an toàn.");
    }
}

