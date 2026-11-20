using ControlUI.Support.Services;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jamesnet.Wpf.Mvvm;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace ControlUI.Settings.Local.ViewModels;

public partial class SettingsContentViewModel : ObservableBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _ready;
    private readonly IMotionService _motion;
    private readonly IUserDialogs _dialogs;

    public string SettingsPath { get; }

    public string[] Themes { get; } = ["Dark", "Light"];
    public string[] Languages { get; } = ["English", "한국어"];

    [ObservableProperty] private string _selectedTheme = "Dark";
    [ObservableProperty] private string _selectedLanguage = "English";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _saveFeedback = "";
    [ObservableProperty] private bool _isSaveFeedbackVisible;
    [ObservableProperty] private bool _isSaveFeedbackError;
    private int _saveFeedbackVersion;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedMotPath))]
    private string _motPath = "";
    public string DisplayedMotPath => string.IsNullOrWhiteSpace(MotPath)
        ? new MotionPreferences().GetMotPath()
        : MotPath;
    [ObservableProperty] private string _homeOrder = new MotionPreferences().HomeOrder;
    [ObservableProperty] private int _homeTimeoutSeconds = new MotionPreferences().HomeTimeoutSeconds;
    private readonly Dictionary<int, MotionIoEditor> _ioDrafts = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyMotionBoardCommand))]
    private MotionIoEditor? _motionIo;
    [ObservableProperty] private string _motionIoStatus = "";
    [ObservableProperty] private string? _motionBoardSummary;
    [ObservableProperty] private AxisSettingsRow? _selectedAxis;
    private ProgramAxis? _selectedBoardAxis;
    public ObservableCollection<AxisSettingsRow> ProgramAxes { get; } = [];
    public SystemPropertyEditor SystemProperties { get; }
    public MotionPropertyEditor MotionProperties { get; }

    public SettingsContentViewModel(IMotionService motion, IUserDialogs dialogs)
        : this(motion, dialogs, AppPreferences.PathName)
    {
    }

    internal SettingsContentViewModel(IMotionService motion, IUserDialogs dialogs, string settingsPath)
    {
        _motion = motion;
        _dialogs = dialogs;
        SettingsPath = settingsPath;
        SystemProperties = new SystemPropertyEditor(this);
        MotionProperties = new MotionPropertyEditor(this);
        var settings = motion.Configuration;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var saved = AppPreferences.Load(SettingsPath);
                SelectedTheme = saved.Theme;
                SelectedLanguage = saved.Language;
                settings = saved.Motion ?? settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Status = $"Using defaults. Could not load settings: {ex.Message}";
        }
        finally
        {
            MotPath = settings.MotPath;
            HomeOrder = settings.HomeOrder;
            HomeTimeoutSeconds = settings.HomeTimeoutSeconds;
            foreach (var axis in settings.Axes)
                TrackAxis(new AxisSettingsRow(axis.Name, axis.Number, axis.MicrometersPerPulse, axis.MaxSpeed)
                {
                    JogDirection = axis.GetJogDirection()
                });
            SelectedAxis = ProgramAxes.FirstOrDefault();
            _ready = true;
        }
    }

    [RelayCommand]
    private void AddAxis()
    {
        int number = 0;
        while (ProgramAxes.Any(axis => axis.Number == number ||
            StringComparer.OrdinalIgnoreCase.Equals(axis.Name, $"Axis {number}")))
            number++;
        var axis = new AxisSettingsRow($"Axis {number}", number);
        TrackAxis(axis);
        HomeOrder = string.IsNullOrWhiteSpace(HomeOrder) ? axis.Name : $"{HomeOrder},{axis.Name}";
        SelectedAxis = axis;
        Status = "Enter the highlighted values for the new axis, then save.";
    }

    [RelayCommand]
    private void RemoveAxis()
    {
        if (SelectedAxis is not { } axis)
            return;
        int index = ProgramAxes.IndexOf(axis);
        if (index < 0)
            return;
        axis.NameChanged -= OnAxisNameChanged;
        ProgramAxes.Remove(axis);
        HomeOrder = string.Join(",", HomeOrder.Split(',', StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0 && !StringComparer.OrdinalIgnoreCase.Equals(name, axis.HomingName)));
        SelectedAxis = ProgramAxes.Count == 0 ? null : ProgramAxes[Math.Min(index, ProgramAxes.Count - 1)];
    }

    private void TrackAxis(AxisSettingsRow axis)
    {
        axis.NameChanged += OnAxisNameChanged;
        ProgramAxes.Add(axis);
    }

    private void OnAxisNameChanged(AxisSettingsRow axis)
    {
        string current = axis.Name.Trim();
        if (ProgramAxes.Any(other => other != axis &&
            StringComparer.OrdinalIgnoreCase.Equals(other.HomingName, current)))
            return;
        HomeOrder = string.Join(",", HomeOrder.Split(',', StringSplitOptions.TrimEntries)
            .Select(name => StringComparer.OrdinalIgnoreCase.Equals(name, axis.HomingName) ? current : name));
        axis.HomingName = current;
    }

    partial void OnSelectedAxisChanged(AxisSettingsRow? oldValue, AxisSettingsRow? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnSelectedAxisMappingChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnSelectedAxisMappingChanged;
        RefreshBoardEditor();
    }

    private void OnSelectedAxisMappingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AxisSettingsRow.Number) or nameof(AxisSettingsRow.Name))
            RefreshBoardEditor();
    }

    private void RefreshBoardEditor()
    {
        var value = SelectedAxis is { } axis
            ? _motion.Configuration.Axes.FirstOrDefault(saved => saved.Number == axis.Number &&
                StringComparer.OrdinalIgnoreCase.Equals(saved.Name, axis.Name.Trim()))
            : null;
        _selectedBoardAxis = value;
        MotionBoardSummary = null;
        MotionIo = value is null ? null :
            _ioDrafts.TryGetValue(value.Number, out var draft) ? draft :
            _ioDrafts[value.Number] = new MotionIoEditor();
        MotionIoStatus = MotionIo is null
            ? "Save the axis settings to enable board I/O for this axis."
            : MotionIo.Original is null
            ? "Keep or a blank field leaves that controller setting unchanged."
            : "Board values loaded.";
    }

    [RelayCommand]
    private void BrowseMot()
    {
        string? directory = Path.GetDirectoryName(DisplayedMotPath);
        if (_dialogs.OpenFile("Motion files (*.mot)|*.mot", "Select Default MOT File",
                Directory.Exists(directory) ? directory : null, Path.GetFileName(DisplayedMotPath)) is { } path)
            MotPath = path;
    }

    [RelayCommand]
    private void UseDefaultMot() => MotPath = "";

    private bool _boardBusy;

    [RelayCommand]
    private async Task ReadMotionBoardAsync()
    {
        if (_boardBusy)
            return;
        if (!_motion.IsReady || _selectedBoardAxis is not { } selectedAxis || MotionIo is not { } draft)
        {
            MotionIoStatus = "Initialize motion in Manual before reading axis settings.";
            return;
        }

        _boardBusy = true;
        try
        {
            var axis = await Task.Run(() => _motion.ReadAxisIoSettings(selectedAxis.Number));
            if (_selectedBoardAxis != selectedAxis)
                return;
            MotionIo = _ioDrafts[selectedAxis.Number] = draft.WithReadback(axis);
            MotionIoStatus = _motion.IsSimulation ? "Simulated axis settings loaded." : "Axis settings loaded.";
            try
            {
                var board = await Task.Run(() => _motion.ReadMotionBoard(selectedAxis.Number));
                if (_selectedBoardAxis == selectedAxis)
                    MotionBoardSummary = FormatBoardSummary(selectedAxis, board);
            }
            catch (Exception ex)
            {
                if (_selectedBoardAxis == selectedAxis)
                    MotionBoardSummary = $"Motion parameter readback unavailable: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            MotionIoStatus = ex.Message;
        }
        finally
        {
            _boardBusy = false;
            ApplyMotionBoardCommand.NotifyCanExecuteChanged();
        }
    }

    private static string FormatBoardSummary(ProgramAxis axis, MotionBoardReadback board)
    {
        var units = board.Units;
        double scale = axis.MillimetersPerPulse * units.Pulses / units.MoveUnit;
        string profile = board.ProfileMode switch
        {
            0 => "Symmetric trapezoid",
            1 => "Asymmetric trapezoid",
            3 => "Symmetric S-curve",
            4 => "Asymmetric S-curve",
            _ => $"Mode {board.ProfileMode}"
        };
        string limits = board.Limits.Enabled
            ? $"{board.Limits.Negative * scale:0.####} to {board.Limits.Positive * scale:0.####} mm"
            : "Disabled";
        return $"Profile: {profile}   ·   {units.MoveUnit:G} controller unit / {units.Pulses} pulse(s)\n" +
               $"Board speed: {board.MinVelocity * scale:0.####} to {units.MaxVelocity * scale:0.####} mm/s\n" +
               $"Soft limits: {limits}";
    }

    private bool CanApplyMotionBoard() => !_boardBusy && MotionIo is not null;

    [RelayCommand(CanExecute = nameof(CanApplyMotionBoard))]
    private async Task ApplyMotionBoardAsync()
    {
        if (!CanApplyMotionBoard() || _selectedBoardAxis is not { } axis || MotionIo is not { } editor)
            return;
        if (!_motion.IsReady)
        {
            MotionIoStatus = "Initialize motion in Manual before applying axis settings.";
            return;
        }

        _boardBusy = true;
        var draft = editor.WithReadback(editor.Original);
        try
        {
            var confirmed = await Task.Run(() =>
            {
                var original = draft.Original ?? _motion.ReadAxisIoSettings(axis.Number);
                _motion.ApplyAxisIoSettings(axis.Number, original, draft.Merge(original));
                return _motion.ReadAxisIoSettings(axis.Number);
            });
            if (_selectedBoardAxis != axis)
                return;
            MotionIo = _ioDrafts[axis.Number] = new MotionIoEditor(confirmed);
            MotionIoStatus = _motion.IsSimulation
                ? "Applied to the simulated axis."
                : "Applied. Use Save As in Manual to save the updated MOT file.";
        }
        catch (Exception ex)
        {
            MotionIoStatus = ex.Message;
        }
        finally
        {
            _boardBusy = false;
            ApplyMotionBoardCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedThemeChanged(string value) => PreviewAppearance();

    partial void OnSelectedLanguageChanged(string value) => PreviewAppearance();

    partial void OnHomeOrderChanged(string value) => MotionProperties.Notify(nameof(MotionPropertyEditor.HomeOrder));

    private void PreviewAppearance()
    {
        if (!_ready || !Themes.Contains(SelectedTheme) || !Languages.Contains(SelectedLanguage))
            return;

        AppAppearance.Apply(new AppPreferences(SelectedTheme, SelectedLanguage));
        Status = SelectedLanguage == "한국어"
            ? "다음 실행에도 적용하려면 설정을 저장하세요."
            : "Save preferences to keep these changes.";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!Themes.Contains(SelectedTheme) || !Languages.Contains(SelectedLanguage))
            return;

        string temporaryPath = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            foreach (var axis in ProgramAxes)
            {
                if (axis.IsResolutionValid && axis.IsMaxSpeedValid)
                    continue;
                SelectedAxis = axis;
                string required = !axis.IsResolutionValid && !axis.IsMaxSpeedValid ? "resolution and maximum speed"
                    : !axis.IsResolutionValid ? "resolution" : "maximum speed";
                throw new ArgumentException($"{axis.Name} (controller #{axis.Number}): enter a positive value for {required}.");
            }
            var motion = new MotionPreferences
            {
                MotPath = MotPath.Trim(),
                HomeOrder = HomeOrder.Trim(),
                HomeTimeoutSeconds = HomeTimeoutSeconds,
                Axes = ProgramAxes.Select(axis => new ProgramAxis(
                    axis.Name.Trim(), axis.Number, axis.MicrometersPerPulse, axis.MaxSpeed)
                {
                    JogDirection = axis.JogDirection
                }).ToArray()
            };
            _ = motion.GetHomeAxes();
            _ = motion.GetMotPath();
            var preferences = new AppPreferences(SelectedTheme, SelectedLanguage) { Motion = motion };
            bool motionChanged = motion.GetMotPath() != _motion.Configuration.GetMotPath() ||
                !motion.Axes.SequenceEqual(_motion.Configuration.Axes) ||
                motion.HomeOrder != _motion.Configuration.HomeOrder ||
                motion.HomeTimeoutSeconds != _motion.Configuration.HomeTimeoutSeconds;
            await Task.Run(() =>
            {
                if (motionChanged)
                    _motion.ApplyConfiguration(motion, SaveFile);
                else
                    SaveFile();
            });
            if (motionChanged)
            {
                _ioDrafts.Clear();
                RefreshBoardEditor();
            }
            Status = SelectedLanguage == "한국어"
                ? (motionChanged ? "모션 설정을 적용했습니다. 장비에 연결하려면 Manual에서 Initialize를 누르세요." : "설정이 저장되었습니다.")
                : (motionChanged ? "Motion settings applied. Use Initialize in Manual to connect." : "Preferences saved.");
            _ = ShowSaveFeedbackAsync(Status, isError: false);

            void SaveFile()
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                try
                {
                    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
                    File.Move(temporaryPath, SettingsPath, overwrite: true);
                }
                finally
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Status = $"Could not save settings: {ex.Message}";
            _ = ShowSaveFeedbackAsync(Status, isError: true);
        }
    }

    private async Task ShowSaveFeedbackAsync(string message, bool isError)
    {
        int version = ++_saveFeedbackVersion;
        SaveFeedback = message;
        IsSaveFeedbackError = isError;
        IsSaveFeedbackVisible = true;
        await Task.Delay(isError ? 5000 : 1000);
        if (version == _saveFeedbackVersion)
            IsSaveFeedbackVisible = false;
    }

}

public enum SystemTheme { Dark, Light }
public enum SystemLanguage { English, Korean }

public sealed class SystemPropertyEditor(SettingsContentViewModel settings)
{
    [Category("Appearance"), DisplayName("Theme")]
    public SystemTheme Theme
    {
        get => settings.SelectedTheme == "Light" ? SystemTheme.Light : SystemTheme.Dark;
        set => settings.SelectedTheme = value.ToString();
    }

    [Category("Appearance"), DisplayName("Language")]
    public SystemLanguage Language
    {
        get => settings.SelectedLanguage == "한국어" ? SystemLanguage.Korean : SystemLanguage.English;
        set => settings.SelectedLanguage = value == SystemLanguage.Korean ? "한국어" : "English";
    }
}

public sealed class MotionPropertyEditor(SettingsContentViewModel settings) : ObservableObject
{
    [Category("Homing"), DisplayName("Home order"), Description("Enter each configured axis once, separated by commas.")]
    public string HomeOrder { get => settings.HomeOrder; set => settings.HomeOrder = value; }

    [Category("Homing"), DisplayName("Timeout per axis (s)")]
    public int Timeout { get => settings.HomeTimeoutSeconds; set => settings.HomeTimeoutSeconds = value; }

    internal void Notify(string property) => OnPropertyChanged(property);
}

public sealed partial class AxisSettingsRow(string name, int number, double? micrometersPerPulse = null,
    double? maxSpeed = null) : ObservableObject
{
    internal string HomingName { get; set; } = name;
    internal event Action<AxisSettingsRow>? NameChanged;

    [ObservableProperty]
    [property: Category("Axis"), DisplayName("Name")]
    private string _name = name;

    partial void OnNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains(','))
            return;
        NameChanged?.Invoke(this);
    }

    [ObservableProperty]
    [property: Category("Axis"), DisplayName("Controller axis number")]
    private int _number = number;

    [ObservableProperty]
    [property: Category("Jog"), DisplayName("Positive jog direction")]
    private JogPositiveDirection _jogDirection = JogPositiveDirection.Left;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolutionValid))]
    [property: Browsable(false)]
    private string _resolutionInput = micrometersPerPulse?.ToString("G", CultureInfo.CurrentCulture) ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMaxSpeedValid))]
    [property: Browsable(false)]
    private string _maxSpeedInput = maxSpeed?.ToString("G", CultureInfo.CurrentCulture) ?? "";

    [Browsable(false)] public double MicrometersPerPulse => ParsePositive(ResolutionInput);
    [Browsable(false)] public double MaxSpeed => ParsePositive(MaxSpeedInput);
    [Browsable(false)] public bool IsResolutionValid => MicrometersPerPulse > 0;
    [Browsable(false)] public bool IsMaxSpeedValid => MaxSpeed > 0;

    private static double ParsePositive(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
        double.IsFinite(value) && value > 0 ? value : 0;
}
