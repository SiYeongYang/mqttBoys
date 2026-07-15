using System.Collections.ObjectModel;
using MqttPulse.App.Infrastructure;
using MqttPulse.App.Models;

namespace MqttPulse.App.ViewModels;

public sealed class ProfileTreeNodeViewModel : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isSelected;

    public ProfileTreeNodeViewModel(string name, string fullPath, BrokerProfile? profile)
    {
        Name = name;
        FullPath = fullPath;
        Profile = profile;
    }

    public string Name { get; }

    public string FullPath { get; }

    public BrokerProfile? Profile { get; }

    public bool IsFolder => Profile is null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ObservableCollection<ProfileTreeNodeViewModel> Children { get; } = new();

    public string DetailText => Profile is null
        ? FullPath
        : $"{Profile.Host}:{Profile.Port}";
}
