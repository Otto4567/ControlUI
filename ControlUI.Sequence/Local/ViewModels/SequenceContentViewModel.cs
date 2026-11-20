using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlUI.Support.Local.Models;
using ControlUI.Support.Services;
using Jamesnet.Wpf.Mvvm;


namespace ControlUI.Sequence.Local.ViewModels;

public partial class SequenceContentViewModel : ObservableBase
{
    private const string FileSuffix = ".sequence.json";
    private const string FileFilter = "Sequence files (*.sequence.json)|*.sequence.json";
    private static JsonSerializerOptions JsonOptions => ProjectWorkspace.JsonOptions;

    public ObservableCollection<SequenceRecipe> Sequences { get; }
    private readonly ProjectWorkspace _workspace;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly IMotionService _motion;
    private readonly IUserDialogs _dialogs;
    public string[] AxisOptions { get; private set; }

    [ObservableProperty] private SequenceRecipe? _selectedSequence;
    [ObservableProperty] private SequencePage? _selectedPage;
    [ObservableProperty] private SequenceStep? _selectedStep;
    [ObservableProperty] private SequenceAxisRow[] _selectedAxisTargets = [];
    [ObservableProperty] private string _status = "Create or load a project.";

    public SequenceContentViewModel(ProjectWorkspace workspace, IMotionService motion, IUserDialogs dialogs)
    {
        _workspace = workspace;
        Sequences = workspace.Projects;
        _motion = motion;
        _dialogs = dialogs;
        AxisOptions = motion.Configuration.Axes.Select(axis => axis.Name).ToArray();
        WeakEventManager<IMotionService, EventArgs>.AddHandler(
            _motion, nameof(IMotionService.ConfigurationChanged), OnMotionConfigurationChanged);
        RestoreSelection(workspace);
        TrackSelection();
    }

    private void OnMotionConfigurationChanged(object? sender, EventArgs args) =>
        _ = _dispatcher.InvokeAsync(() =>
        {
            AxisOptions = _motion.Configuration.Axes.Select(axis => axis.Name).ToArray();
            OnPropertyChanged(nameof(AxisOptions));
            OnSelectedStepChanged(SelectedStep);
        });

    partial void OnSelectedStepChanged(SequenceStep? value)
    {
        TrackSelection();
        if (value is null)
        {
            SelectedAxisTargets = [];
            return;
        }

        SelectedAxisTargets = AxisOptions.Select(name =>
        {
            var matching = value.AxisTargets.FirstOrDefault(entry =>
                StringComparer.OrdinalIgnoreCase.Equals(entry.Key, name)).Value;
            if (matching is null)
                value.AxisTargets[name] = matching = new SequenceAxisTarget();
            return new SequenceAxisRow(name, matching);
        }).ToArray();
    }

    private void RestoreSelection(ProjectWorkspace workspace)
    {
        if (workspace.RestoreError is { } error)
        {
            Status = $"Could not restore workspace: {error}";
            return;
        }

        if (workspace.Previous is not { } previous)
            return;

        SelectedSequence = Sequences.FirstOrDefault(project => project.Id == previous.SelectedProjectId)
            ?? Sequences.FirstOrDefault();
        if (SelectedSequence is { } selected &&
            previous.PageIndex >= 0 && previous.PageIndex < selected.Pages.Count)
            SelectedPage = selected.Pages[previous.PageIndex];

        if (SelectedPage is { } page && previous.StepIndex >= 0 && previous.StepIndex < page.Steps.Count)
            SelectedStep = page.Steps[previous.StepIndex];

        if (Sequences.Count > 0)
            Status = "Previous workspace restored.";
    }

    partial void OnSelectedSequenceChanged(SequenceRecipe? value)
    {
        SelectedPage = value?.Pages.FirstOrDefault();
        TrackSelection();
    }

    partial void OnSelectedPageChanged(SequencePage? value)
    {
        if (value is not null)
            NumberSteps(value);

        SelectedStep = value?.Steps.FirstOrDefault();
        TrackSelection();
    }

    private void TrackSelection() => _workspace.TrackSelection(SelectedSequence, SelectedPage, SelectedStep);

    private static void NumberSteps(SequencePage page)
    {
        for (int i = 0; i < page.Steps.Count; i++)
            page.Steps[i].StepNumber = i + 1;
    }

    [RelayCommand]
    private void NewSequence()
    {
        var recipe = new SequenceRecipe();
        recipe.Pages.Add(new SequencePage());
        Sequences.Add(recipe);
        SelectedSequence = recipe;
        Status = "Project created.";
    }

    [RelayCommand]
    private void RemoveSequence()
    {
        if (SelectedSequence is not { } recipe)
            return;

        int index = Sequences.IndexOf(recipe);
        Sequences.Remove(recipe);
        SelectedSequence = Sequences.Count == 0 ? null : Sequences[Math.Min(index, Sequences.Count - 1)];
        Status = "Removed from the list.";
    }

    [RelayCommand]
    private void AddPage()
    {
        if (SelectedSequence is not { } recipe)
            return;

        int number = recipe.Pages.Count + 1;
        while (recipe.Pages.Any(page => page.Name == $"Page {number}"))
            number++;

        var newPage = new SequencePage { Name = $"Page {number}" };
        recipe.Pages.Add(newPage);
        SelectedPage = newPage;
    }

    [RelayCommand]
    private void RemovePage()
    {
        if (SelectedSequence is not { } recipe || SelectedPage is not { } page)
            return;

        int index = recipe.Pages.IndexOf(page);
        if (index < 0)
            return;

        if (page.Steps.Count > 0 && !_dialogs.Confirm($"Remove '{page.Name}' and its steps?", "Remove Page"))
            return;

        recipe.Pages.RemoveAt(index);
        SelectedPage = recipe.Pages.Count == 0 ? null : recipe.Pages[Math.Min(index, recipe.Pages.Count - 1)];
    }

    [RelayCommand]
    private void MovePageUp() => MovePage(-1);

    [RelayCommand]
    private void MovePageDown() => MovePage(1);

    private void MovePage(int offset)
    {
        if (SelectedSequence is not { } recipe || SelectedPage is not { } page)
            return;

        int index = recipe.Pages.IndexOf(page);
        int destination = index + offset;
        if (index >= 0 && destination >= 0 && destination < recipe.Pages.Count)
        {
            recipe.Pages.Move(index, destination);
            SelectedPage = page;
        }
    }

    [RelayCommand]
    private void AddStep()
    {
        if (SelectedPage is not { } recipe)
            return;

        var step = new SequenceStep { Axis = AxisOptions.FirstOrDefault() ?? "" };
        recipe.Steps.Add(step);
        NumberSteps(recipe);
        SelectedStep = step;
    }

    [RelayCommand]
    private void RemoveStep()
    {
        if (SelectedPage is not { } recipe || SelectedStep is not { } step)
            return;

        int index = recipe.Steps.IndexOf(step);
        if (index < 0)
            return;

        recipe.Steps.RemoveAt(index);
        NumberSteps(recipe);
        SelectedStep = recipe.Steps.Count == 0 ? null : recipe.Steps[Math.Min(index, recipe.Steps.Count - 1)];
    }

    [RelayCommand]
    private void MoveUp() => MoveStep(-1);

    [RelayCommand]
    private void MoveDown() => MoveStep(1);

    private void MoveStep(int offset)
    {
        if (SelectedPage is not { } recipe || SelectedStep is not { } step)
            return;

        int index = recipe.Steps.IndexOf(step);
        int destination = index + offset;
        if (index < 0 || destination < 0 || destination >= recipe.Steps.Count)
            return;

        recipe.Steps.Move(index, destination);
        NumberSteps(recipe);
        SelectedStep = step;
    }

    [RelayCommand]
    private void SaveSequence()
    {
        if (SelectedSequence is not { } recipe)
            return;

        if (_dialogs.SaveFile(FileFilter, FileSuffix, SafeFileName(recipe.Name) + FileSuffix) is { } path)
            ReportFileAction(() =>
            {
                SaveFile(path, recipe);
                return "Sequence saved.";
            });
    }

    [RelayCommand]
    private void SaveAll()
    {
        if (Sequences.Count == 0)
            return;

        if (_dialogs.OpenFolder("Choose a folder for all sequences") is { } folder)
            ReportFileAction(() =>
            {
                var files = Sequences.Select(recipe => (
                    Recipe: recipe,
                    Path: Path.Combine(folder,
                        $"{SafeFileName(recipe.Name)}-{recipe.Id:N}{FileSuffix}"))).ToArray();
                if (files.Any(file => string.IsNullOrWhiteSpace(file.Recipe.Name)))
                    return "Enter a name for every sequence before saving.";

                bool overwrite = files.Any(file => File.Exists(file.Path));
                if (overwrite && !_dialogs.Confirm(
                    "Replace existing sequence files in this folder?", "Save All Sequences"))
                    return "Save cancelled.";

                int saved = 0;
                foreach (var file in files)
                {
                    try
                    {
                        SaveFile(file.Path, file.Recipe, overwrite);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        return $"Saved {saved} of {Sequences.Count}. {ex.Message}";
                    }
                    saved++;
                }

                return $"Saved {saved} sequence files.";
            });
    }

    [RelayCommand]
    private void LoadSequence()
    {
        if (_dialogs.OpenFile(FileFilter) is { } path)
            LoadFiles([path]);
    }

    [RelayCommand]
    private void LoadFolder()
    {
        if (_dialogs.OpenFolder("Choose a sequence folder") is { } folder)
            ReportFileAction(() => ImportFiles(Directory.EnumerateFiles(
                folder, "*" + FileSuffix).OrderBy(path => path)));
    }

    private void LoadFiles(IEnumerable<string> paths) =>
        ReportFileAction(() => ImportFiles(paths));

    private string ImportFiles(IEnumerable<string> paths)
    {
        int loaded = 0;
        int skipped = 0;
        string? firstError = null;
        SequenceRecipe? firstLoaded = null;
        foreach (string path in paths)
        {
            try
            {
                var recipe = JsonSerializer.Deserialize<SequenceRecipe>(File.ReadAllText(path), JsonOptions);
                if (recipe is null || recipe.Id == Guid.Empty || recipe.Pages is null ||
                    string.IsNullOrWhiteSpace(recipe.Name))
                    throw new JsonException("Invalid sequence file.");

                if (recipe.Pages.Count == 0 && recipe.LegacySteps is { } legacySteps)
                    recipe.Pages.Add(new SequencePage { Steps = legacySteps });

                if (recipe.Pages.Any(page => page is null || page.Steps is null ||
                    page.Steps.Any(step => step is null)))
                    throw new JsonException("Invalid project page or step.");

                recipe.LegacySteps = null;

                if (Sequences.Any(existing => existing.Id == recipe.Id))
                {
                    skipped++;
                    continue;
                }

                Sequences.Add(recipe);
                firstLoaded ??= recipe;
                loaded++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                firstError ??= $"{Path.GetFileName(path)}: {ex.Message}";
                skipped++;
            }
        }

        if (firstLoaded is not null)
            SelectedSequence = firstLoaded;

        return $"Loaded {loaded}, skipped {skipped}." +
            (firstError is null ? "" : $" {firstError}");
    }

    private static void SaveFile(string path, SequenceRecipe recipe, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(recipe.Name))
            throw new InvalidOperationException("Enter a sequence name before saving.");

        ProjectWorkspace.WriteJsonFile(path, recipe, overwrite);
    }

    private static string SafeFileName(string name)
    {
        string result = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c))
            .Trim().TrimEnd('.');
        return result.Length == 0 ? "sequence" : result;
    }

    private void ReportFileAction(Func<string> action)
    {
        try
        {
            Status = action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException)
        {
            Status = ex.Message;
        }
    }
}

public sealed record SequenceAxisRow(string Name, SequenceAxisTarget Target);
