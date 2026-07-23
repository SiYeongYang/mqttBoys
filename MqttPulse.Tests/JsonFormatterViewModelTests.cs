using System.Windows;
using MqttPulse.App.ViewModels;

namespace MqttPulse.Tests;

[TestClass]
public sealed class JsonFormatterViewModelTests
{
    [TestMethod]
    public void FormatterToolOpensFormatsAndClosesWithoutChangingMqttState()
    {
        using var viewModel = new MainViewModel();
        viewModel.JsonFormatterInput = "{\"상태\":\"정상\",\"value\":42}";

        viewModel.OpenJsonFormatterCommand.Execute(null);
        viewModel.FormatJsonFormatterCommand.Execute(null);

        Assert.AreEqual(Visibility.Visible, viewModel.JsonFormatterVisibility);
        StringAssert.Contains(viewModel.JsonFormatterOutput, Environment.NewLine);
        StringAssert.Contains(viewModel.JsonFormatterOutput, "\"상태\": \"정상\"");
        StringAssert.Contains(viewModel.JsonFormatterStatus, "정리");
        Assert.HasCount(1, viewModel.JsonFormatterTreeRoots);
        Assert.HasCount(2, viewModel.JsonFormatterTreeRoots[0].Children);
        Assert.AreEqual("상태", viewModel.JsonFormatterTreeRoots[0].Children[0].Name);
        Assert.AreEqual("정상", viewModel.JsonFormatterTreeRoots[0].Children[0].Value);

        viewModel.CloseJsonFormatterCommand.Execute(null);

        Assert.AreEqual(Visibility.Collapsed, viewModel.JsonFormatterVisibility);
        Assert.IsFalse(viewModel.IsConnected);
    }

    [TestMethod]
    public void FormatterToolShowsValidationErrorAndClearsOutput()
    {
        using var viewModel = new MainViewModel
        {
            JsonFormatterInput = "{\"value\":}"
        };

        viewModel.FormatJsonFormatterCommand.Execute(null);

        Assert.AreEqual(string.Empty, viewModel.JsonFormatterOutput);
        Assert.IsEmpty(viewModel.JsonFormatterTreeRoots);
        StringAssert.Contains(viewModel.JsonFormatterStatus, "유효하지 않은 JSON");
    }
}
