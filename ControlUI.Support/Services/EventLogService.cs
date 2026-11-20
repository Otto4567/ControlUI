using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ControlUI.Support.Services;

public sealed record EventLogEntry(DateTimeOffset Time, string Level, string Source, string Message)
{
    public string LocalTime => Time.ToLocalTime().ToString("HH:mm:ss.fff");
}

public sealed class EventLogService
{
    private readonly SemaphoreSlim _fileLock = new(1);
    private readonly ObservableCollection<EventLogEntry> _entries = [];

    public EventLogService()
    {
        Entries = new ReadOnlyObservableCollection<EventLogEntry>(_entries);
    }

    public ReadOnlyObservableCollection<EventLogEntry> Entries { get; }

    public void ClearDisplay() => _entries.Clear();

    public void RecordLifecycle(string message)
    {
        var entry = new EventLogEntry(DateTimeOffset.UtcNow, "Info", "Application", message);
        AddEntry(entry);

        _fileLock.Wait();
        try
        {
            File.AppendAllText(GetLogPath(entry), SerializeLine(entry));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not save event log: {0}", ex.Message);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task WriteAsync(string level, string source, string message)
    {
        var entry = new EventLogEntry(DateTimeOffset.UtcNow, level, source, message);
        await Application.Current.Dispatcher.InvokeAsync(() => AddEntry(entry));

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(GetLogPath(entry), SerializeLine(entry)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not save event log: {0}", ex.Message);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // Fatal handlers can run without a dispatcher or while another writer owns the daily log.
    public void RecordFailure(string source, Exception error)
    {
        var entry = new EventLogEntry(DateTimeOffset.UtcNow, "Error", source, error.ToString());
        if (_fileLock.Wait(0))
        {
            try
            {
                File.AppendAllText(GetLogPath(entry), SerializeLine(entry));
                return;
            }
            finally { _fileLock.Release(); }
        }
        string fallback = Path.ChangeExtension(GetLogPath(entry), $"{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(fallback, SerializeLine(entry));
    }

    private void AddEntry(EventLogEntry entry)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > 500)
            _entries.RemoveAt(_entries.Count - 1);
    }

    private static string GetLogPath(EventLogEntry entry)
    {
        string folder = MachinePaths.Logs;
        Directory.CreateDirectory(folder);
        string date = entry.Time.ToLocalTime().ToString("yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(folder, date + ".jsonl");
    }

    private static string SerializeLine(EventLogEntry entry) =>
        JsonSerializer.Serialize(entry) + Environment.NewLine;
}
