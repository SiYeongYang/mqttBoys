using System.Collections.ObjectModel;
using System.Windows.Media;
using MqttPulse.App.Infrastructure;
using MqttPulse.Core;

namespace MqttPulse.App.ViewModels;

public sealed class JsonTreeNodeViewModel : ObservableObject
{
    private static readonly Brush StringBrush = Frozen("#167245");
    private static readonly Brush NumberBrush = Frozen("#B45309");
    private static readonly Brush BooleanBrush = Frozen("#8B2F75");
    private static readonly Brush NullBrush = Frozen("#65716E");
    private bool _isExpanded;

    public JsonTreeNodeViewModel(JsonStructureNode node, bool isRoot = false)
    {
        Name = node.Name;
        Kind = node.Kind;
        Value = node.Value;
        IsContainer = node.Kind is JsonStructureKind.Object or JsonStructureKind.Array;
        Summary = node.Kind switch
        {
            JsonStructureKind.Object => $"{{{node.ChildCount:N0}}}",
            JsonStructureKind.Array => $"[{node.ChildCount:N0}]",
            _ => string.Empty
        };
        Separator = IsContainer ? " " : " : ";
        ValueBrush = node.Kind switch
        {
            JsonStructureKind.String => StringBrush,
            JsonStructureKind.Number => NumberBrush,
            JsonStructureKind.Boolean => BooleanBrush,
            _ => NullBrush
        };
        Children = new ObservableCollection<JsonTreeNodeViewModel>(
            node.Children.Select(child => new JsonTreeNodeViewModel(child)));
        _isExpanded = isRoot;
    }

    public string Name { get; }

    public JsonStructureKind Kind { get; }

    public string Value { get; }

    public string Summary { get; }

    public string Separator { get; }

    public bool IsContainer { get; }

    public Brush ValueBrush { get; }

    public ObservableCollection<JsonTreeNodeViewModel> Children { get; }

    public bool IsSearchVisible => true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private static SolidColorBrush Frozen(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
