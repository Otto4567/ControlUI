using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ControlUI.Support.Local.Models;

public enum ProjectRunMode { Once, Repeat, Continuous }

public partial class SequenceRecipe : ObservableObject
{
    [ObservableProperty] private string _name = "New Project";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepeatMode))]
    private ProjectRunMode _runMode;

    [ObservableProperty] private int _repeatCount = 1;

    [JsonIgnore]
    public bool IsRepeatMode => RunMode == ProjectRunMode.Repeat;

    public Guid Id { get; init; } = Guid.NewGuid();
    public ObservableCollection<SequencePage> Pages { get; init; } = [];

    [JsonPropertyName("Steps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ObservableCollection<SequenceStep>? LegacySteps { get; set; }
}
