using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlUI.Support.Local.Models;

namespace ControlUI.Support.Services;

public sealed class ProjectWorkspace
{
    private bool _editorOpened;
    private SequenceRecipe? _selectedProject;
    private SequencePage? _selectedPage;
    private SequenceStep? _selectedStep;
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ControlUI", "workspace.json");

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public sealed record Snapshot(
        SequenceRecipe[] Projects, Guid? SelectedProjectId, int PageIndex, int StepIndex);

    public ObservableCollection<SequenceRecipe> Projects { get; } = [];
    public Snapshot? Previous { get; }
    public string? RestoreError { get; }

    public ProjectWorkspace()
    {
        try
        {
            if (!File.Exists(FilePath))
                return;

            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(FilePath), JsonOptions);
            if (snapshot?.Projects is not { } projects)
                throw new JsonException("Invalid workspace.");
            foreach (var project in projects)
                ValidateRecipe(project);

            Previous = snapshot;
            foreach (var project in projects)
                Projects.Add(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            RestoreError = ex.Message;
        }
    }

    public static void ValidateRecipe(SequenceRecipe? recipe)
    {
        if (recipe is null || recipe.Id == Guid.Empty || recipe.Pages is null ||
            string.IsNullOrWhiteSpace(recipe.Name))
            throw new JsonException("Invalid sequence project.");

        if (recipe.Pages.Count == 0 && recipe.LegacySteps is { } legacySteps)
            recipe.Pages.Add(new SequencePage { Steps = legacySteps });

        if (recipe.Pages.Any(page => page is null || page.Steps is null ||
            page.Steps.Any(step => step is null || step.AxisTargets is null ||
                step.AxisTargets.Values.Any(target => target is null))))
            throw new JsonException("Invalid project page, step or axis targets.");

        recipe.LegacySteps = null;
    }

    public void TrackSelection(SequenceRecipe? project, SequencePage? page, SequenceStep? step)
    {
        _editorOpened = true;
        _selectedProject = project;
        _selectedPage = page;
        _selectedStep = step;
    }

    public void Save()
    {
        if (!_editorOpened)
            return;
        try
        {
            int pageIndex = _selectedPage is null ? -1 : _selectedProject?.Pages.IndexOf(_selectedPage) ?? -1;
            int stepIndex = _selectedStep is null ? -1 : _selectedPage?.Steps.IndexOf(_selectedStep) ?? -1;
            var snapshot = new Snapshot(Projects.ToArray(), _selectedProject?.Id, pageIndex, stepIndex);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            WriteJsonFile(FilePath, snapshot, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not save workspace: {0}", ex.Message);
        }
    }

    public static void WriteJsonFile<T>(string path, T value, bool overwrite)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
