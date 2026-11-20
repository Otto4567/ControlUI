using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace ControlUI.Support.Services;

public static class MachinePaths
{
    private sealed record StorageOptions(string DataDirectory);
    private static readonly Lazy<string> RootPath = new(ResolveRoot);
    public static string Root => RootPath.Value;
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Workspace => Path.Combine(Root, "workspace.json");
    public static string Logs => Path.Combine(Root, "Logs");

    private static string ResolveRoot()
    {
        string optionsPath = Path.Combine(AppContext.BaseDirectory, "machine-paths.json");
        if (!File.Exists(optionsPath))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ControlUI");

        var options = JsonSerializer.Deserialize<StorageOptions>(File.ReadAllText(optionsPath));
        if (string.IsNullOrWhiteSpace(options?.DataDirectory))
            throw new InvalidDataException($"Specify DataDirectory in {optionsPath}.");
        return Path.GetFullPath(options.DataDirectory, AppContext.BaseDirectory);
    }

    public static void Initialize()
    {
        string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ControlUI");
        if (StringComparer.OrdinalIgnoreCase.Equals(Root, defaultRoot) && !Directory.Exists(Root))
        {
            // Give the machine's users access to the new application data directory.
            var security = new DirectorySecurity();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Modify, InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Root).Create(security);
        }
        Directory.CreateDirectory(Root);
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControlUI");
        ImportLegacy(legacy, Root, WindowsIdentity.GetCurrent().User!.Value);
    }

    internal static void ImportLegacy(string legacy, string destination, string userId)
    {
        if (!Directory.Exists(legacy) || StringComparer.OrdinalIgnoreCase.Equals(
                Path.TrimEndingDirectorySeparator(legacy), Path.TrimEndingDirectorySeparator(destination)))
            return;
        string marker = Path.Combine(destination, $".imported-{userId}");
        if (File.Exists(marker))
            return;

        foreach (string name in new[] { "settings.json", "workspace.json" })
        {
            string source = Path.Combine(legacy, name);
            if (File.Exists(source))
                CopyOnce(source, Path.Combine(destination, name));
        }
        string oldLogs = Path.Combine(legacy, "Logs");
        if (Directory.Exists(oldLogs))
        {
            string logs = Path.Combine(destination, "Logs");
            Directory.CreateDirectory(logs);
            foreach (string source in Directory.EnumerateFiles(oldLogs, "*.jsonl"))
            {
                string target = Path.Combine(logs, Path.GetFileName(source));
                if (!CopyOnce(source, target))
                    CopyOnce(source, Path.Combine(logs, $"{Path.GetFileNameWithoutExtension(source)}.{userId}.jsonl"));
            }
        }
        using var completed = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
    }

    // Publish only complete files. A competing first launch or another account may already own the destination.
    private static bool CopyOnce(string source, string target)
    {
        if (File.Exists(target))
            return false;
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            try { File.Move(temporary, target); }
            catch (IOException) when (File.Exists(target)) { return false; }
            return true;
        }
        finally { File.Delete(temporary); }
    }
}
