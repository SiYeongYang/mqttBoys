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
        StringAssert.Contains(viewModel.JsonFormatterStatus, "유효하지 않은 JSON");
    }
}
