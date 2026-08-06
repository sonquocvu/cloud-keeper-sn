using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.UI.Theming;
using CloudKeeperSN.App.UI.Windowing;
using CloudKeeperSN.Domain.Transfers;

namespace CloudKeeperSN.App.Tests;

public sealed class PresentationAndSettingsTests
{
    [Fact]
    public void InternalStatesMapToNaturalVietnamese()
    {
        Assert.Equal("Đang chờ thử lại", VietnamesePresentationMapper.TransferState(TransferState.RetryPending).Text);
        Assert.Equal("Xác minh chưa thành công", VietnamesePresentationMapper.Verification(VerificationLevel.VerificationFailed).Text);
        Assert.Equal("Hoàn tất với cảnh báo", VietnamesePresentationMapper.RunStatus(DemoRunStatus.CompletedWithWarnings).Text);
    }

    [Fact]
    public async Task ThemeSelectionAppliesImmediatelyAndPersists()
    {
        var environment = await UiTestEnvironment.CreateAsync();
        var dark = environment.Settings.ThemeOptions.Single(option => option.Value == ThemeMode.Dark);

        await environment.Settings.ChangeThemeAsync(dark, CancellationToken.None);

        Assert.Equal(ThemeMode.Dark, environment.Theme.CurrentMode);
        Assert.Equal("Dark", environment.SettingsRepository.Values["ui.theme"]);
    }

    [Theory]
    [InlineData("0", "1024", "6")]
    [InlineData("2", "64", "6")]
    [InlineData("2", "1024", "11")]
    public async Task SettingsRejectUnsafeNumericValues(string concurrent, string cache, string retry)
    {
        var environment = await UiTestEnvironment.CreateAsync();
        environment.Settings.ConcurrentTransfers = concurrent;
        environment.Settings.CacheLimitMiB = cache;
        environment.Settings.RetryAttempts = retry;

        Assert.NotEmpty(environment.Settings.ValidationMessage);
        Assert.False(environment.Settings.SaveTransferSettingsCommand.CanExecute(null));
    }

    [Fact]
    public void WindowPlacementMovesOffscreenWindowBackToVisibleArea()
    {
        var restored = WindowPlacementValidator.EnsureVisible(new WindowPlacement(9000, 9000, 1280, 800, true), 0, 0, 1920, 1080);

        Assert.InRange(restored.Left, 0, 1920 - 80);
        Assert.InRange(restored.Top, 0, 1080 - 80);
        Assert.False(restored.IsMaximized);
    }

    [Fact]
    public async Task AsyncCommandDisposalCancelsInFlightWork()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new CloudKeeperSN.App.ViewModels.AsyncRelayCommand(async token =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled.SetResult(); }
        });
        command.Execute(null);
        await started.Task;

        command.Dispose();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DemoScenarioProducesStableHistory()
    {
        var first = await UiTestEnvironment.CreateAsync(DemoScenarioKind.Standard);
        var second = await UiTestEnvironment.CreateAsync(DemoScenarioKind.Standard);

        Assert.Equal(first.Workspace.Runs.Select(run => run.Id), second.Workspace.Runs.Select(run => run.Id));
        Assert.Equal(first.Workspace.Runs.Select(run => run.Status), second.Workspace.Runs.Select(run => run.Status));
    }
}
