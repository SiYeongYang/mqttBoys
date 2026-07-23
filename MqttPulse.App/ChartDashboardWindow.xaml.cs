using System.Windows;
using MqttPulse.App.ViewModels;
using MqttPulse.Core;

namespace MqttPulse.App;

public partial class ChartDashboardWindow : Window
{
    private readonly ChartDashboardViewModel _viewModel;

    public ChartDashboardWindow(MainViewModel mainViewModel)
    {
        InitializeComponent();
        _viewModel = new ChartDashboardViewModel(mainViewModel);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    public ChartDashboardViewModel ViewModel => _viewModel;

    public void AddChart(string topic, JsonScalarMetric metric)
    {
        _viewModel.AddChart(topic, metric);
    }
}
