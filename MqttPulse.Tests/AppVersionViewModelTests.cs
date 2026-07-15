using MqttPulse.App.ViewModels;

namespace MqttPulse.Tests;

[TestClass]
public sealed class AppVersionViewModelTests
{
    [TestMethod]
    public void AppVersionTextUsesTheApplicationAssemblyVersion()
    {
        using var viewModel = new MainViewModel();
        var version = typeof(MainViewModel).Assembly.GetName().Version;

        Assert.IsNotNull(version);
        Assert.AreEqual(
            $"v{version.Major}.{version.Minor}.{version.Build}",
            viewModel.AppVersionText);
    }
}
