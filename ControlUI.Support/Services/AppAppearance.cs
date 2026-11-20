using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;

namespace ControlUI.Support.Services;

public sealed record AppPreferences(string Theme, string Language)
{
    public MotionPreferences Motion { get; init; } = new();
    public static readonly AppPreferences Default = new("Dark", "English");
    public static string PathName => MachinePaths.Settings;

    public static AppPreferences Load() => Load(PathName);

    public static AppPreferences Load(string path)
    {
        if (!File.Exists(path))
            return Default;

        return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(path))
            is { Theme: "Dark" or "Light", Language: "English" or "한국어" } value
            ? value : throw new JsonException("Unsupported theme or language.");
    }
}

public sealed record MotionPreferences
{
    public string MotPath { get; init; } = "";
    public string HomeOrder { get; init; } = "Y,X,Z";
    public int HomeTimeoutSeconds { get; init; } = 120;
    public IReadOnlyList<ProgramAxis> Axes { get; init; } =
        [new("Y", 0, 0.1, 1000), new("X", 1, 1, 500), new("Z", 2, 1, 500)];

    public string GetMotPath() => string.IsNullOrWhiteSpace(MotPath)
        ? Path.Combine(AppContext.BaseDirectory, "Config", "motion.mot")
        : Path.GetFullPath(MotPath, AppContext.BaseDirectory);

    public int[] GetHomeAxes()
    {
        if (Axes is null || Axes.Count == 0 || Axes.Any(axis => axis is null ||
            string.IsNullOrWhiteSpace(axis.Name) || axis.Name.Contains(',') || axis.Number < 0 ||
            !double.IsFinite(axis.MicrometersPerPulse) || axis.MicrometersPerPulse <= 0 ||
            !double.IsFinite(axis.MaxSpeed) || axis.MaxSpeed <= 0))
            throw new ArgumentException("Enter at least one axis with a name, nonnegative number, resolution and speed.");
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var numbers = new HashSet<int>();
        foreach (var axis in Axes)
            if (!byName.TryAdd(axis.Name, axis.Number) || !numbers.Add(axis.Number))
                throw new ArgumentException("Axis names and controller numbers must be unique.");

        var names = (HomeOrder ?? "").Split(',', StringSplitOptions.TrimEntries);
        if (names.Length != Axes.Count || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Axes.Count ||
            names.Any(name => !byName.ContainsKey(name)))
            throw new ArgumentException("Enter each configured axis once in Home Order, separated by commas.");
        if (HomeTimeoutSeconds is < 1 or > 3600)
            throw new ArgumentException("Home timeout must be between 1 and 3600 seconds.");
        return names.Select(name => byName[name]).ToArray();
    }
}

public sealed record ProgramAxis(string Name, int Number, double MicrometersPerPulse, double MaxSpeed)
{
    public JogPositiveDirection? JogDirection { get; init; }

    public JogPositiveDirection GetJogDirection() => JogDirection ?? Name switch
    {
        "Y" => JogPositiveDirection.Up,
        "Z" => JogPositiveDirection.Down,
        _ => JogPositiveDirection.Left
    };

    public double MillimetersPerPulse => MicrometersPerPulse / 1000;
}

public enum JogPositiveDirection
{
    Up,
    Down,
    Left,
    Right
}

public static class AppAppearance
{
    private static ResourceDictionary? _current;

    public static void Apply(AppPreferences preferences)
    {
        bool dark = preferences.Theme == "Dark";
        bool korean = preferences.Language == "한국어";
        string skin = dark ? "SkinDark" : "SkinDefault";
        string palette = dark ? "AppearanceDark" : "AppearanceLight";
        string strings = korean ? "StringsKo" : "StringsEn";
        var resources = new ResourceDictionary();
        foreach (string path in new[]
        {
            $"pack://application:,,,/HandyControl;component/Themes/{skin}.xaml",
            "pack://application:,,,/HandyControl;component/Themes/Theme.xaml",
            $"pack://application:,,,/ControlUI.Support;component/Themes/{palette}.xaml",
            $"pack://application:,,,/ControlUI.Support;component/Themes/{strings}.xaml"
        })
        {
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path) });
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_current is null)
            dictionaries.Add(resources);
        else
            dictionaries[dictionaries.IndexOf(_current)] = resources;
        _current = resources;

        var culture = CultureInfo.GetCultureInfo(korean ? "ko-KR" : "en-US");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
