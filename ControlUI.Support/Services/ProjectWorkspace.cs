using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlUI.Support.Local.Models;

namespace ControlUI.Support.Services;

public sealed class ProjectWorkspace
{
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<Action> _detach = [];
    private readonly object _writeLock = new();
    private readonly string _path;
    private long _revision;
    private long _writtenRevision;
    private bool _editorOpened;
    private SequenceRecipe? _selectedProject;
    private SequencePage? _selectedPage;
    private SequenceStep? _selectedStep;
    public static string FilePath => MachinePaths.Workspace;

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

    public ProjectWorkspace() : this(FilePath) { }

    internal ProjectWorkspace(string path)
    {
        _path = path;
        _saveTimer.Tick += (_, _) => Flush(background: true);
        try
        {
            if (!File.Exists(_path))
                return;

            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path), JsonOptions);
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
        finally { ObserveProjects(); }
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
        if (Projects.Count > 0) ScheduleSave();
    }

    private void ObserveProjects()
    {
        foreach (var unsubscribe in _detach)
            unsubscribe();
        _detach.Clear();

        ObserveCollection(Projects);
        foreach (var project in Projects)
        {
            ObserveItem(project);
            ObserveCollection(project.Pages);
            foreach (var page in project.Pages)
            {
                ObserveItem(page);
                ObserveCollection(page.Steps);
                foreach (var step in page.Steps)
                {
                    ObserveItem(step);
                    foreach (var target in step.AxisTargets.Values)
                        ObserveItem(target);
                }
            }
        }
    }

    private void ObserveCollection(INotifyCollectionChanged collection)
    {
        collection.CollectionChanged += OnCollectionChanged;
        _detach.Add(() => collection.CollectionChanged -= OnCollectionChanged);
    }

    private void ObserveItem(INotifyPropertyChanged item)
    {
        item.PropertyChanged += OnEdited;
        _detach.Add(() => item.PropertyChanged -= OnEdited);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ObserveProjects();
        ScheduleSave();
    }

    private void OnEdited(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SequenceStep.AxisTargets))
            ObserveProjects();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        ++_revision;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Save() => Flush(background: false);

    private void Flush(bool background)
    {
        _saveTimer.Stop();
        if (_revision == 0 && !_editorOpened)
            return;
        int pageIndex = _selectedPage is null ? -1 : _selectedProject?.Pages.IndexOf(_selectedPage) ?? -1;
        int stepIndex = _selectedStep is null ? -1 : _selectedPage?.Steps.IndexOf(_selectedStep) ?? -1;
        var snapshot = new Snapshot(Projects.ToArray(), _selectedProject?.Id, pageIndex, stepIndex);
        long revision = _revision;
        // Snapshot bound objects on the dispatcher before writing on a worker.
        string json;
        try { json = JsonSerializer.Serialize(snapshot, JsonOptions); }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not serialize workspace: {0}", ex.Message);
            return;
        }
        if (background)
            _ = Task.Run(Write);
        else
            Write();

        void Write()
        {
            lock (_writeLock)
            {
                if (_writtenRevision >= revision)
                    return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    WriteJsonText(_path, json, overwrite: true);
                    _writtenRevision = revision;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    System.Diagnostics.Trace.TraceWarning("Could not save workspace: {0}", ex.Message);
                }
            }
        }
    }

    public static void WriteJsonFile<T>(string path, T value, bool overwrite) =>
        WriteJsonText(path, JsonSerializer.Serialize(value, JsonOptions), overwrite);

    private static void WriteJsonText(string path, string json, bool overwrite)
    {
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
