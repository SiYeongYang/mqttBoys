using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MqttPulse.App;
using MqttPulse.App.ViewModels;

namespace MqttPulse.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PeriodTopicAutocompleteTests
{
    [TestMethod]
    public void CommitSuggestionUpdatesTopicWithoutSelectingInputText()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                try
                {
                    window.Opacity = 0;
                    window.ShowInTaskbar = false;
                    window.Show();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

                    var viewModel = (MainViewModel)window.DataContext;
                    var input = (TextBox)window.FindName("PeriodTopicInput");
                    var popup = (Popup)window.FindName("PeriodTopicPopup");
                    var list = (ListBox)window.FindName("PeriodTopicSuggestionList");
                    const string topic = "VTS/EDGE_DATA/M_001";

                    viewModel.PeriodCheckTopicSuggestions.Add(topic);
                    popup.IsOpen = true;
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                    Assert.HasCount(1, list.Items);

                    var commit = typeof(MainWindow).GetMethod(
                        "CommitPeriodTopicSuggestion",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new MissingMethodException(nameof(MainWindow), "CommitPeriodTopicSuggestion");
                    commit.Invoke(window, new object[] { topic });
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                    Assert.AreEqual(topic, viewModel.PeriodCheckTopicText);
                    Assert.AreEqual(topic, input.Text);
                    Assert.AreEqual(topic.Length, input.CaretIndex);
                    Assert.AreEqual(0, input.SelectionLength);
                    Assert.IsFalse(popup.IsOpen);
                    Assert.AreEqual(-1, list.SelectedIndex);
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Assert.Fail(failure.ToString());
        }
    }
}
