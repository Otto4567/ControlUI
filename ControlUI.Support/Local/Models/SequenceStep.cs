using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Support.Local.Models;

public enum StepAction { Move, Wait, MultiAxisMove }

public partial class SequenceStep : ObservableObject
{
    [ObservableProperty]
    [property: System.Text.Json.Serialization.JsonIgnore]
    private int _stepNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMove))]
    [NotifyPropertyChangedFor(nameof(IsWait))]
    [NotifyPropertyChangedFor(nameof(IsMultiAxisMove))]
    private StepAction _action;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMove => Action == StepAction.Move;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWait => Action == StepAction.Wait;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMultiAxisMove => Action == StepAction.MultiAxisMove;

    public Dictionary<string, SequenceAxisTarget> AxisTargets { get; init; } = new();
    public SequenceAxisTarget GetOrAddTarget(string name)
    {
        var match = AxisTargets.FirstOrDefault(pair => StringComparer.OrdinalIgnoreCase.Equals(pair.Key, name)).Value;
        if (match is not null)
            return match;
        var target = new SequenceAxisTarget();
        AxisTargets.Add(name, target);
        OnPropertyChanged(nameof(AxisTargets));
        return target;
    }

    [ObservableProperty] private string _axis = "Y";
    [ObservableProperty] private double _positionMm;
    [ObservableProperty] private double _velocityMmPerSecond = 10;
    [ObservableProperty] private double _accelMmPerSecondSquared = 100;
    [ObservableProperty] private double _decelMmPerSecondSquared = 100;
    [ObservableProperty] private int _waitMilliseconds = 500;
}

public partial class SequenceAxisTarget : ObservableObject
{
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private double _positionMm;
    [ObservableProperty] private double _velocityMmPerSecond = 10;
    [ObservableProperty] private double _accelMmPerSecondSquared = 100;
    [ObservableProperty] private double _decelMmPerSecondSquared = 100;
}
