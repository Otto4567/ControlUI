using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Manual.Local.Models;

public partial class AxisSignalValue : ObservableObject
{
    [ObservableProperty]
    private bool? _isActive;

}
