using System.Text.Json;
using System.IO;
using CloudKeeperSN.App.Development;
using CloudKeeperSN.Domain.Diagnostics;
using CloudKeeperSN.App.Presentation;
using Microsoft.Win32;

namespace CloudKeeperSN.App.Services;

public interface IDiagnosticExportService
{
    Task<string?> ExportAsync(IReadOnlyList<DemoBackupRun> runs, CancellationToken cancellationToken);
}

public sealed class DiagnosticExportService : IDiagnosticExportService
{
    public async Task<string?> ExportAsync(IReadOnlyList<DemoBackupRun> runs, CancellationToken cancellationToken)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất thông tin chẩn đoán",
            Filter = "Tệp JSON (*.json)|*.json",
            FileName = $"CloudKeeperSN-chan-doan-{DateTime.Now:yyyyMMdd-HHmm}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return null;

        var safeRuns = runs.Select(run => new
        {
            run.Id,
            run.Name,
            run.Source,
            run.Destination,
            run.StartedAt,
            DurationSeconds = run.Duration.TotalSeconds,
            Status = VietnamesePresentationMapper.RunStatus(run.Status).Text,
            run.CompletedFiles,
            run.SkippedFiles,
            run.WarningCount,
            run.FailedCount,
            run.TransferredBytes,
            Verification = VietnamesePresentationMapper.Verification(run.Verification).Text,
            Timeline = run.Timeline.Select(SensitiveDataRedactor.Redact).ToArray()
        });
        var document = new
        {
            Product = "CloudKeeperSN",
            ExportedAt = DateTimeOffset.Now,
            Notice = "Thông tin nhạy cảm đã được ẩn. Tệp này không chứa token hoặc mật khẩu.",
            Runs = safeRuns
        };
        await using var stream = File.Create(dialog.FileName);
        await JsonSerializer.SerializeAsync(stream, document, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
        return dialog.FileName;
    }
}
