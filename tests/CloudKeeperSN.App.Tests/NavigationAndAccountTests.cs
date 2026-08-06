using CloudKeeperSN.App.Development;
using CloudKeeperSN.App.ViewModels;

namespace CloudKeeperSN.App.Tests;

public sealed class NavigationAndAccountTests
{
    [Theory]
    [InlineData("dashboard")]
    [InlineData("accounts")]
    [InlineData("backup")]
    [InlineData("history")]
    [InlineData("settings")]
    public async Task Navigation_ReachesEveryPrimaryPage(string key)
    {
        var environment = await UiTestEnvironment.CreateAsync();

        await environment.Main.NavigateToAsync(key, CancellationToken.None);

        Assert.Equal(key, environment.Main.CurrentPage.Key);
        Assert.Single(environment.Main.NavigationItems, item => item.IsSelected && item.Key == key);
    }

    [Fact]
    public async Task Accounts_DisconnectedStateShowsSafeConnectActions()
    {
        var environment = await UiTestEnvironment.CreateAsync(DemoScenarioKind.Disconnected);

        Assert.Equal(AccountConnectionState.Disconnected, environment.Accounts.GoogleDrive.State);
        Assert.Equal(AccountConnectionState.Disconnected, environment.Accounts.OneDrive.State);
        Assert.True(environment.Accounts.GoogleDrive.ConnectCommand.CanExecute(null));
        Assert.False(environment.Accounts.GoogleDrive.DisconnectCommand.CanExecute(null));
    }
}

