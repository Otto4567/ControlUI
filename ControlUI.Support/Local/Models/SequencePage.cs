using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Support.Local.Models;

public partial class SequencePage : ObservableObject
{
    [ObservableProperty]
    private string _name = "Page 1";

    public ObservableCollection<SequenceStep> Steps { get; init; } = [];
}
