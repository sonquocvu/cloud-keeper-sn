using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.ViewModels;

namespace CloudKeeperSN.App.Tests;

public sealed class BackupWorkflowTests
{
    [Fact]
    public async Task ScanRequiresBothFoldersAndConnectedAccounts()
    {
        var environment = await UiTestEnvironment.CreateAsync();

        Assert.False(environment.Backup.CanScan);
        Assert.Contains("thư mục nguồn", environment.Backup.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        environment.QueueDefaultFolders();
        environment.Backup.SelectSourceCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.SourceFolder is not null);
        Assert.False(environment.Backup.CanScan);
        environment.Backup.SelectDestinationCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.DestinationFolder is not null);

        Assert.True(environment.Backup.CanScan);
        Assert.True(environment.Backup.ScanCommand.CanExecute(null));
    }

    [Fact]
    public async Task PreviewCountsAndFilteringAreDeterministic()
    {
        var environment = await CreatePreviewAsync();

        Assert.NotNull(environment.Backup.Preview);
        Assert.Equal(1, environment.Backup.Preview.ConflictCount);
        Assert.Equal(1, environment.Backup.Preview.SkipCount);
        Assert.Equal(3, environment.Backup.Preview.ExportCount);
        Assert.Equal(1, environment.Backup.Preview.UnsupportedCount);
        Assert.True(environment.Backup.Preview.WarningCount >= 2);

        environment.Backup.SelectedFilter = environment.Backup.PreviewFilters.Single(filter => filter.Category == PreviewItemCategory.Conflict);
        Assert.Single(environment.Backup.VisiblePreviewItems);
        Assert.Contains("CloudKeeperSN 2", environment.Backup.VisiblePreviewItems[0].DestinationName);

        environment.Backup.SelectedFilter = environment.Backup.PreviewFilters.Single(filter => filter.Category == PreviewItemCategory.Warning);
        Assert.Single(environment.Backup.VisiblePreviewItems);
    }

    [Fact]
    public async Task ConfirmationContainsScopeAndMandatorySafetyLanguage()
    {
        var environment = await CreatePreviewAsync();
        environment.Dialogs.ConfirmationResult = false;

        environment.Backup.StartBackupCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Dialogs.Requests.Count > 0);

        var request = environment.Dialogs.Requests[^1];
        Assert.Contains("Google Drive", request.Message);
        Assert.Contains("OneDrive", request.Message);
        Assert.Contains("không bị thay đổi", request.SupportingText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("không bị xóa hoặc ghi đè", request.SupportingText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BackupWorkflowStage.Preview, environment.Backup.Stage);
    }

    [Fact]
    public async Task RunningBackupSupportsPauseResumeAndCancel()
    {
        var environment = await UiTestEnvironment.CreateAsync(DemoScenarioKind.LongRunning, new DemoDelay());
        await PreparePreviewAsync(environment);
        environment.Dialogs.ConfirmationResult = true;

        environment.Backup.StartBackupCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.Stage == BackupWorkflowStage.Running);
        Assert.True(environment.Backup.PauseCommand.CanExecute(null));
        environment.Backup.PauseCommand.Execute(null);
        Assert.True(environment.Backup.IsPaused);
        Assert.True(environment.Backup.ResumeCommand.CanExecute(null));
        environment.Backup.ResumeCommand.Execute(null);
        Assert.False(environment.Backup.IsPaused);
        environment.Backup.CancelCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.Stage == BackupWorkflowStage.Result);

        Assert.Equal("Đã hủy sao lưu", environment.Backup.Result!.Headline);
    }

    [Fact]
    public async Task FailedResultEnablesRetryAndNeverShowsGreenSuccess()
    {
        var environment = await UiTestEnvironment.CreateAsync(DemoScenarioKind.RetryAndFailure);
        await PreparePreviewAsync(environment);
        environment.Backup.StartBackupCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.Stage == BackupWorkflowStage.Result);

        Assert.True(environment.Backup.Result!.FailedCount > 0);
        Assert.NotEqual(CloudKeeperSN.App.Presentation.StatusTone.Success, environment.Backup.Result.Tone);
        Assert.True(environment.Backup.RetryFailedCommand.CanExecute(null));
        environment.Backup.RetryFailedCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.Result!.FailedCount == 0);
    }

    private static async Task<UiTestEnvironment> CreatePreviewAsync()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        await PreparePreviewAsync(environment);
        return environment;
    }

    private static async Task PreparePreviewAsync(UiTestEnvironment environment)
    {
        environment.QueueDefaultFolders();
        environment.Backup.SelectSourceCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.SourceFolder is not null);
        environment.Backup.SelectDestinationCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.DestinationFolder is not null);
        environment.Backup.ScanCommand.Execute(null);
        await AsyncTest.UntilAsync(() => environment.Backup.Stage == BackupWorkflowStage.Preview);
    }
}
