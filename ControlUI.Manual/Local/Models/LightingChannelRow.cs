using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Manual.Local.Models;

public partial class LightingChannelRow(string channel) : ObservableObject
{
    public string Channel { get; } = channel;

    [ObservableProperty]
    private string? _mode;

    [ObservableProperty]
    private string? _source;

    [ObservableProperty]
    private double? _dcBrightnessPercent;

    [ObservableProperty]
    private double? _pulseBrightnessPercent;

    [ObservableProperty]
    private double? _currentMilliamps;

    [ObservableProperty]
    private double? _delayMicroseconds;

    [ObservableProperty]
    private double? _durationMicroseconds;
}
