using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Manual.Local.Models;

public partial class TriggerChannelRow(string channel) : ObservableObject
{
    public string Channel { get; } = channel;

    [ObservableProperty]
    private bool? _isEnabled;

    [ObservableProperty]
    private long? _intervalCounts;

    [ObservableProperty]
    private long? _delayTicks;

    [ObservableProperty]
    private long? _pulseWidthTicks;

    [ObservableProperty]
    private string? _polarity;

    [ObservableProperty]
    private long? _outputCount;
}
