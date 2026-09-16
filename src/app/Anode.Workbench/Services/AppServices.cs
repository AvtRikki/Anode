using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Anode.Sdk;

namespace Anode.Workbench.Services;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message);

/// <summary>Workbench log, shown in the Console panel.</summary>
public sealed class LogService : ILog
{
    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Info(string message) => Add(LogLevel.Info, message);

    public void Warn(string message) => Add(LogLevel.Warning, message);

    public void Error(string message, Exception? exception = null) =>
        Add(LogLevel.Error, exception is null ? message : $"{message} ({exception.GetType().Name})");

    private void Add(LogLevel level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Entries.Add(entry);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Entries.Add(entry));
        }
    }
}

public sealed record RecentProject(string Name, string Path, DateTime OpenedAt);

/// <summary>Recently opened files, most recent first, stored in the user's application data folder.</summary>
public sealed class RecentProjectsStore(string? filePath = null)
{
    private const int Capacity = 12;

    private readonly string _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "recent.json");

    public IReadOnlyList<RecentProject> Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(_filePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Moves <paramref name="path"/> to the front of the list and saves it.</summary>
    public IReadOnlyList<RecentProject> Touch(string path, DateTime now)
    {
        var projects = Load().Where(p => !string.Equals(p.Path, path, StringComparison.Ordinal)).ToList();
        projects.Insert(0, new RecentProject(Path.GetFileNameWithoutExtension(path), path, now));
        if (projects.Count > Capacity)
        {
            projects.RemoveRange(Capacity, projects.Count - Capacity);
        }

        Save(projects);
        return projects;
    }

    /// <summary>How long ago, in the active language: "12 min ago", "yesterday".</summary>
    public static string Ago(DateTime moment, DateTime now)
    {
        var elapsed = now - moment;
        return elapsed switch
        {
            { TotalMinutes: < 1 } => Tr.T("shell.time.now"),
            { TotalHours: < 1 } => Tr.T("shell.time.minutes", (int)elapsed.TotalMinutes),
            { TotalHours: < 12 } => Tr.T("shell.time.hours", (int)elapsed.TotalHours),
            { TotalDays: < 2 } when moment.Date == now.Date.AddDays(-1) => Tr.T("shell.time.yesterday"),
            _ => Tr.T("shell.time.days", Math.Max(1, (int)elapsed.TotalDays)),
        };
    }

    private void Save(List<RecentProject> projects)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(projects));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recent projects are a convenience; losing them must never break opening a board.
        }
    }
}

/// <summary>Where the workbench keeps its per-user files.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anode");
}

/// <summary>User choices that outlive a session. Today: the interface language.</summary>
public sealed class SettingsStore(string? filePath = null)
{
    private readonly string _filePath = filePath ?? Path.Combine(AppPaths.DataDirectory, "settings.json");

    private sealed record Settings(string? Language);

    /// <summary>The stored language, or null when the user has never chosen one.</summary>
    public CultureInfo? Language
    {
        get
        {
            try
            {
                if (File.Exists(_filePath)
                    && JsonSerializer.Deserialize<Settings>(File.ReadAllText(_filePath)) is { Language: { Length: > 0 } language })
                {
                    return CultureInfo.GetCultureInfo(language);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or CultureNotFoundException)
            {
                // A broken settings file must not stop the workbench from starting.
            }

            return null;
        }

        set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(new Settings(value?.Name)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Same: the choice applies to this session even if it cannot be stored.
            }
        }
    }
}
