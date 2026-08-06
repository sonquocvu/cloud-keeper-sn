using CloudKeeperSN.Domain.Export;
using CloudKeeperSN.Domain.Storage;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.Domain.Tests;

public sealed class ExportAndVerificationTests
{
    [Theory]
    [InlineData(GoogleNativeExportPolicy.GoogleDocument, ".docx")]
    [InlineData(GoogleNativeExportPolicy.GoogleSpreadsheet, ".xlsx")]
    [InlineData(GoogleNativeExportPolicy.GooglePresentation, ".pptx")]
    [InlineData(GoogleNativeExportPolicy.GoogleDrawing, ".png")]
    public void NativeExport_MapsSupportedExtensions(string mimeType, string extension)
    {
        var decision = GoogleNativeExportPolicy.Decide(mimeType);

        Assert.True(decision.IsSupported);
        Assert.Equal(extension, decision.Extension);
    }

    [Fact]
    public void NativeExport_SkipsShortcutsClearly()
    {
        var decision = GoogleNativeExportPolicy.Decide(GoogleNativeExportPolicy.GoogleShortcut);

        Assert.False(decision.IsSupported);
        Assert.Contains("bỏ qua", decision.VietnameseExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Checksums_WithDifferentAlgorithms_AreNeverComparedAsEqual()
    {
        var source = new ProviderChecksum("MD5", "same-text");
        var destination = new ProviderChecksum("SHA-1", "same-text");

        Assert.False(ChecksumCompatibility.AreCompatible(source, destination));
        Assert.False(ChecksumCompatibility.ValuesMatch(source, destination));
    }
}

