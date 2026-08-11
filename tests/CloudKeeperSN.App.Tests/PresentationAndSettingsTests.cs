using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.Presentation;
using CloudKeeperSN.App.UI.Theming;
using CloudKeeperSN.App.UI.Windowing;
using CloudKeeperSN.Domain.Transfers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace CloudKeeperSN.App.Tests;

public sealed class PresentationAndSettingsTests
{
    [Fact]
    public void ScanSummarySemanticResourcesExistInLightAndDarkThemes()
    {
        string[] requiredKeys =
        [
            "AccentSoft", "BackgroundSecondary", "BorderSubtle", "Information", "InformationSoft", "SurfacePrimary",
            "Success", "SuccessSoft", "TextPrimary", "TextSecondary", "Warning", "WarningSoft"
        ];

        foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
        {
            var document = XDocument.Load(RepositoryFile("src", "CloudKeeperSN.App", "UI", "Themes", theme));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var keys = document.Root!.Elements()
                .Select(element => (string?)element.Attribute(x + "Key"))
                .Where(key => key is not null)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in requiredKeys)
                Assert.Contains(key, keys);
        }
    }

    [Fact]
    public void ScanSummaryProgressBindsReadOnlyPercentageOneWay()
    {
        var document = XDocument.Load(RepositoryFile("src", "CloudKeeperSN.App", "Views", "BackupView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var progress = document.Descendants(presentation + "ProgressBar")
            .Single(element => ((string?)element.Attribute("Value"))?.Contains("StorageUsagePercent", StringComparison.Ordinal) == true);

        var binding = (string)progress.Attribute("Value")!;
        Assert.Contains("Mode=OneWay", binding, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanSummaryUsesResponsiveEqualWidthTileLayouts()
    {
        var document = XDocument.Load(RepositoryFile("src", "CloudKeeperSN.App", "Views", "BackupView.xaml"));
        var panels = document.Descendants()
            .Where(element => element.Name.LocalName == "ResponsiveUniformGrid")
            .ToArray();

        Assert.Contains(panels, panel => (string?)panel.Attribute("MaximumColumns") == "5" &&
                                          (string?)panel.Attribute("MinimumItemWidth") == "150");
        Assert.Contains(panels, panel => (string?)panel.Attribute("MaximumColumns") == "4" &&
                                          (string?)panel.Attribute("MinimumItemWidth") == "170");
    }

    [Fact]
    public void BackupPlanPageKeepsSemanticMetricsResponsivePanesAndVirtualization()
    {
        var document = XDocument.Load(RepositoryFile("src", "CloudKeeperSN.App", "Views", "InventoryPlanView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var controls = document.Descendants().ToArray();

        Assert.Contains(controls, element => element.Name.LocalName == "AdaptiveTwoPanePanel" &&
                                             (string?)element.Attribute("Breakpoint") == "900");
        Assert.DoesNotContain(controls, element => element.Name == presentation + "ScrollViewer");

        var itemList = controls.Single(element => element.Name == presentation + "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding SearchResults}");
        Assert.Equal("True", (string?)itemList.Attribute("VirtualizingStackPanel.IsVirtualizing"));
        Assert.Equal("Recycling", (string?)itemList.Attribute("VirtualizingStackPanel.VirtualizationMode"));
        Assert.Equal("True", (string?)itemList.Attribute("ScrollViewer.CanContentScroll"));

        var reviewMetric = controls.Single(element => element.Name == presentation + "Border" &&
            (string?)element.Attribute("Style") == "{StaticResource PlanMetricTile}" &&
            element.Descendants(presentation + "TextBlock").Any(text => (string?)text.Attribute("Text") == "Cần kiểm tra") &&
            element.Descendants(presentation + "TextBlock").Any(text => ((string?)text.Attribute("Text"))?.Contains("SelectedReviewItemCountLabel", StringComparison.Ordinal) == true));
        Assert.DoesNotContain(reviewMetric.Descendants().Attributes("Text"), attribute => attribute.Value.Contains("BackupEligibleItemCount", StringComparison.Ordinal));
    }

    [Fact]
    public void BackupPlanPageUsesThemedControlsWithoutRawWhiteSurfaces()
    {
        var path = RepositoryFile("src", "CloudKeeperSN.App", "Views", "InventoryPlanView.xaml");
        var source = File.ReadAllText(path);
        var document = XDocument.Parse(source);

        Assert.DoesNotContain("Background=\"White\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#FFFFFF", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DynamicResource SurfacePrimary", source, StringComparison.Ordinal);
        Assert.Contains("DynamicResource BackgroundSecondary", source, StringComparison.Ordinal);
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "ComboBox");
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "TreeView" &&
                                                        (string?)element.Attribute("Background") == "Transparent");
    }

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

    private static string RepositoryFile(params string[] segments)
    {
        var testDirectory = Path.GetDirectoryName(CurrentSourceFile())!;
        return Path.GetFullPath(Path.Combine([testDirectory, "..", "..", .. segments]));
    }

    private static string CurrentSourceFile([CallerFilePath] string path = "") => path;
}
