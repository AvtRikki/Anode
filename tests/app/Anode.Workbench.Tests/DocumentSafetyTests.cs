using Avalonia.Controls;
using Avalonia.Threading;
using Anode.Sdk;
using Anode.Workbench.Services;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Tests;

public class DocumentSafetyTests
{
    private sealed class Document : DocumentBase
    {
        public Document(string path) => FilePath = path;
        public override string Title => Path.GetFileName(FilePath)!;
        public int SaveCount { get; private set; }
        public TaskCompletionSource<bool>? SaveGate { get; init; }
        protected override Control CreateView() => new Border();
        public override async Task<bool> SaveAsync(string? path = null)
        {
            SaveCount++;
            if (SaveGate is not null && !await SaveGate.Task)
            {
                return false;
            }

            FilePath = path ?? FilePath;
            return true;
        }
    }

    private sealed class DocumentType : IDocumentType
    {
        public string Id => "test.safety";
        public string Label => "Test";
        public IReadOnlyList<string> Extensions => [".test"];
        public bool CanCreate => true;
        public Func<string, Task> Create { get; init; } = path => File.WriteAllTextAsync(path, "new");
        public Dictionary<string, TaskCompletionSource<IDocument>> Pending { get; } = [];
        public int OpenCount { get; private set; }
        public Task CreateAsync(string path, CancellationToken cancellationToken) => Create(path);
        public Task<IDocument> OpenAsync(string path, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Pending.TryGetValue(path, out var pending) ? pending.Task : Task.FromResult<IDocument>(new Document(path));
        }
    }

    private static Task Check(Action<ShellViewModel, string> body) => ShellWindowTests.Dispatch(_ =>
    {
        string folder = Directory.CreateTempSubdirectory("anode-safety-").FullName;
        try
        {
            var shell = ShellWindowTests.Workbench(null, new RecentProjectsStore(Path.Combine(folder, "recents.json")));
            body(shell, folder);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    private static T Pump<T>(Task<T> task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }

        Assert.True(task.IsCompleted, "The operation did not complete.");
        return task.GetAwaiter().GetResult();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task Creating_a_project_preserves_existing_outputs(bool projectExists, bool sheetExists) => Check((shell, folder) =>
    {
        shell.DocumentTypes.Register(new DocumentType());
        string directory = Directory.CreateDirectory(Path.Combine(folder, "lamp")).FullName;
        string project = Path.Combine(directory, "lamp.kicad_pro");
        string sheet = Path.Combine(directory, "lamp.test");
        if (projectExists) File.WriteAllText(project, "original project");
        if (sheetExists) File.WriteAllText(sheet, "original sheet");

        Assert.Null(Pump(shell.NewProjectAsync(Path.Combine(folder, "lamp.kicad_pro"))));
        Assert.Equal(projectExists, File.Exists(project));
        Assert.Equal(sheetExists, File.Exists(sheet));
        if (projectExists) Assert.Equal("original project", File.ReadAllText(project));
        if (sheetExists) Assert.Equal("original sheet", File.ReadAllText(sheet));
        Assert.NotNull(shell.CurrentBanner);
    });

    [Fact]
    public Task A_failed_creator_publishes_neither_file_and_removes_staging() => Check((shell, folder) =>
    {
        shell.DocumentTypes.Register(new DocumentType
        {
            Create = path => { File.WriteAllText(path, "partial"); throw new IOException("failed"); },
        });
        Assert.Null(Pump(shell.NewProjectAsync(Path.Combine(folder, "lamp.kicad_pro"))));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(folder, "lamp")));
    });

    [Fact]
    public Task A_late_collision_does_not_overwrite_and_rolls_back_the_project() => Check((shell, folder) =>
    {
        string target = Path.Combine(folder, "lamp", "lamp.test");
        shell.DocumentTypes.Register(new DocumentType
        {
            Create = path => { File.WriteAllText(path, "new"); File.WriteAllText(target, "other writer"); return Task.CompletedTask; },
        });
        Assert.Null(Pump(shell.NewProjectAsync(Path.Combine(folder, "lamp.kicad_pro"))));
        Assert.Equal("other writer", File.ReadAllText(target));
        Assert.Equal(new[] { target }, Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(target)!));
    });

    [Fact]
    public Task New_sheet_does_not_overwrite_an_existing_document() => Check((shell, folder) =>
    {
        shell.DocumentTypes.Register(new DocumentType());
        string path = Path.Combine(folder, "sheet.test");
        File.WriteAllText(path, "original");
        Assert.Null(Pump(shell.NewSheetAsync(path)));
        Assert.Equal("original", File.ReadAllText(path));
    });

    [Fact]
    public Task Concurrent_opens_share_one_document_and_busy_tracks_other_files() => Check((shell, folder) =>
    {
        var type = new DocumentType();
        shell.DocumentTypes.Register(type);
        string a = Path.Combine(folder, "a.test"), b = Path.Combine(folder, "b.test");
        type.Pending[a] = new();
        type.Pending[b] = new();
        var first = shell.OpenAsync(a);
        var duplicate = shell.OpenAsync(Path.Combine(folder, ".", "a.test"));
        var second = shell.OpenAsync(b);
        Assert.Equal(2, type.OpenCount);
        Assert.True(shell.IsBusy);
        type.Pending[a].SetResult(new Document(a));
        Assert.Same(Pump(first), Pump(duplicate));
        Assert.Single(shell.Documents);
        Assert.True(shell.IsBusy);
        type.Pending[b].SetResult(new Document(b));
        Assert.NotNull(Pump(second));
        Assert.Equal(2, shell.Documents.Count);
        Assert.False(shell.IsBusy);
    });

    [Fact]
    public Task Failed_shared_open_releases_the_path_for_retry() => Check((shell, folder) =>
    {
        var type = new DocumentType();
        shell.DocumentTypes.Register(type);
        string path = Path.Combine(folder, "a.test");
        type.Pending[path] = new();
        var first = shell.OpenAsync(path);
        var second = shell.OpenAsync(path);
        type.Pending[path].SetException(new IOException("unreadable"));
        Assert.Null(Pump(first));
        Assert.Null(Pump(second));
        Assert.False(shell.IsBusy);
        type.Pending.Remove(path);
        Assert.NotNull(Pump(shell.OpenAsync(path)));
        Assert.Single(shell.Documents);
    });

    [Fact]
    public Task Save_as_cannot_take_another_tabs_path_but_can_keep_its_own() => Check((shell, folder) =>
    {
        var a = new Document(Path.Combine(folder, "a.test"));
        var b = new Document(Path.Combine(folder, "b.test"));
        shell.AddDocument(a);
        shell.AddDocument(b);
        shell.PickSavePath = _ => Task.FromResult<string?>(Path.Combine(folder, ".", "b.test"));
        Assert.False(Pump(shell.SaveAsync(a, askForPath: true)));
        Assert.Equal(0, a.SaveCount);
        Assert.EndsWith("a.test", a.FilePath);
        Assert.NotNull(shell.CurrentBanner);
        Assert.True(Pump(shell.SaveAsync(b, askForPath: true)));
        Assert.Equal(1, b.SaveCount);
    });

    [Fact]
    public Task Opening_and_saving_reserve_their_destination_until_finished() => Check((shell, folder) =>
    {
        var type = new DocumentType();
        shell.DocumentTypes.Register(type);
        string target = Path.Combine(folder, "target.test");
        type.Pending[target] = new();
        var a = new Document(Path.Combine(folder, "a.test")) { SaveGate = new() };
        shell.AddDocument(a);
        shell.PickSavePath = _ => Task.FromResult<string?>(target);
        var opening = shell.OpenAsync(target);
        Assert.False(Pump(shell.SaveAsync(a, askForPath: true)));
        type.Pending[target].SetException(new IOException("failed"));
        Assert.Null(Pump(opening));
        var saving = shell.SaveAsync(a, askForPath: true);
        Assert.False(saving.IsCompleted);
        Assert.Null(Pump(shell.OpenAsync(target)));
        var b = new Document(Path.Combine(folder, "b.test"));
        shell.AddDocument(b);
        Assert.False(Pump(shell.SaveAsync(b, askForPath: true)));
        a.SaveGate.SetResult(false);
        Assert.False(Pump(saving));
        type.Pending.Remove(target);
        Assert.NotNull(Pump(shell.OpenAsync(target)));
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_project_with_a_broken_or_cyclic_child_opens_with_a_check(bool cycle) => Check((_, folder) =>
    {
        GraphicsOptions.Renderer = RendererKind.Skia;
        var shell = ShellWindowTests.Workbench(PanelScopeTests.PluginsRoot,
            new RecentProjectsStore(Path.Combine(folder, "plugin-recents.json")));
        string root = Path.Combine(folder, "root.kicad_sch");
        File.WriteAllText(Path.Combine(folder, "root.kicad_pro"), "{}");
        string child = cycle ? "root.kicad_sch" : "bad.kicad_sch";
        File.WriteAllText(root, $"(kicad_sch (version 20260206) (uuid root) (paper \"A4\") (lib_symbols) " +
            $"(sheet (uuid child) (at 20 20) (size 10 10) (property \"Sheetfile\" \"{child}\")))");
        if (!cycle) File.WriteAllText(Path.Combine(folder, child), "(kicad_sch");
        var document = Pump(shell.OpenAsync(root));
        Assert.NotNull(document);
        string title = Tr.T(cycle ? "sch.hierarchy.Cycle" : "sch.hierarchy.Unreadable");
        Assert.Contains(document.Issues, issue => issue.Title == title);
        Assert.NotNull(document.Overview);
    });

}
