using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Anode.Sdk;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Panels;

/// <summary>
/// The project, two ways. <em>Design</em> answers "what is this project made of": the schematic with the hierarchy of
/// its sheets, the board, the rules, the project's own libraries, what it produced. <em>Files</em> answers "what is
/// actually on disk": the folders as they are, with backups and caches out of the way until asked for.
///
/// The shell knows folders and names; what is inside a file is known only by the plugin that owns the format, so the
/// sheet hierarchy comes from <see cref="IProjectStructureRegistry"/> and is read one level at a time, as nodes open.
/// </summary>
public sealed class ProjectTreePanel : ContentControl
{
    private const int MaxDepth = 12;

    private static readonly string[] BoardExtensions = [".kicad_pcb"];
    private static readonly string[] SchematicExtensions = [".kicad_sch"];
    private static readonly string[] RuleExtensions = [".kicad_dru", ".kicad_wks"];

    /// <summary>The project file names the project, which the header already says; it appears under Files only.</summary>
    private const string ProjectExtension = ".kicad_pro";
    private static readonly string[] LibraryExtensions = [".kicad_sym", ".kicad_mod"];
    private static readonly string[] LibraryTables = ["sym-lib-table", "fp-lib-table"];
    private static readonly string[] OutputExtensions = [".gbr", ".drl", ".net", ".csv", ".pos", ".zip", ".pdf", ".step", ".stp", ".wrl"];
    private static readonly string[] OutputFolders = ["gerber", "gerbers", "output", "outputs", "production", "bom", "fab", "plot"];

    private readonly ShellViewModel _shell;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private string? _seededFor;
    private bool _filesView;
    private bool _showEverything;
    private string _filter = string.Empty;

    public ProjectTreePanel(ShellViewModel shell)
    {
        _shell = shell;
        shell.ActiveDocumentChanged += Render;
        shell.ProjectChanged += Render;
        Tr.Changed += Render;
        Render();
    }

    /// <summary>A row of the tree. Children are a function, so a level is only read once it is opened.</summary>
    private sealed record Node(string Key, string Title, string IconKey)
    {
        public string? Detail { get; init; }

        public string? Path { get; init; }

        /// <summary>The appearance of <see cref="Path"/> this row stands for; see <see cref="ProjectNode.Instance"/>.</summary>
        public string? Instance { get; init; }

        public bool IsSection { get; init; }

        public Func<IReadOnlyList<Node>>? Children { get; init; }
    }

    private void Render()
    {
        if (_shell.ProjectDirectory is not { } directory)
        {
            var empty = new StackPanel { Spacing = 6 };
            empty.Children.Add(Ui.Text(Tr.T("shell.project.emptyTitle"), "heading"));
            var hint = Ui.Text(Tr.T("shell.project.emptyHint"), "dim");
            hint.TextWrapping = TextWrapping.Wrap;
            empty.Children.Add(hint);
            Content = empty;
            return;
        }

        var root = new StackPanel { Spacing = 10 };
        root.Children.Add(Header(directory));
        root.Children.Add(Switch());
        root.Children.Add(Filter());

        var rows = new StackPanel { Spacing = 1 };
        var nodes = _filesView ? Entries(directory) : Design(directory);
        if (_filter.Length > 0)
        {
            nodes = [.. nodes.Select(n => Matching(n, 0)).OfType<Node>()];
        }

        if (nodes.Count == 0)
        {
            rows.Children.Add(Ui.Text(Tr.T(_filter.Length > 0 ? "shell.project.nothing" : "shell.project.emptyHint"), "dim"));
        }

        AddRows(rows, nodes, 0);
        root.Children.Add(rows);

        if (_filesView)
        {
            var toggle = Ui.TagButton(Tr.T(_showEverything ? "shell.project.showFewer" : "shell.project.showAll"), "neutral", () =>
            {
                _showEverything = !_showEverything;
                Render();
            });
            toggle.HorizontalAlignment = HorizontalAlignment.Left;
            toggle.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(toggle);
        }

        Content = new ScrollViewer { Content = root };
    }

    private Control Header(string directory)
    {
        var header = new StackPanel();
        header.Children.Add(Ui.Text(_shell.ProjectName, "heading"));

        // The folder usually carries the project's own name; saying it twice tells the reader nothing.
        string folder = FileName(directory);
        if (!string.Equals(folder, _shell.ProjectName, StringComparison.Ordinal))
        {
            header.Children.Add(Ui.Mono(folder, "dim"));
        }

        return header;
    }

    /// <summary>Design or files: the same folder, read two ways.</summary>
    private Control Switch()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(Tab("shell.project.view.design", !_filesView, false));
        row.Children.Add(Tab("shell.project.files", _filesView, true));
        return row;

        Button Tab(string key, bool active, bool files)
        {
            var button = new Button { Classes = { "stackTab" }, Content = Tr.T(key) };
            button.Classes.Set("active", active);
            button.Click += (_, _) =>
            {
                if (_filesView != files)
                {
                    _filesView = files;
                    Render();
                }
            };

            return button;
        }
    }

    private Control Filter()
    {
        var box = new TextBox
        {
            Classes = { "filter" },
            Text = _filter,
            PlaceholderText = Tr.T("shell.project.filterHint"),
        };

        box.TextChanged += (_, _) =>
        {
            string text = box.Text ?? string.Empty;
            if (!string.Equals(text, _filter, StringComparison.Ordinal))
            {
                _filter = text;
                Render();
                // Rendering replaces the box; put the caret back where the typing was.
                if (Content is ScrollViewer { Content: StackPanel panel }
                    && panel.Children.OfType<TextBox>().FirstOrDefault() is { } fresh)
                {
                    fresh.Focus();
                    fresh.CaretIndex = fresh.Text?.Length ?? 0;
                }
            }
        };

        return box;
    }

    // ——— The design view: sections of what a project is made of ———

    private IReadOnlyList<Node> Design(string directory)
    {
        var files = SafeFiles(directory);
        var sections = new List<Node>();

        Section(sections, "schematic", Icons.Sheets, files.Where(f => Is(f, SchematicExtensions)).Select(Schematic));
        Section(sections, "board", Icons.Board, files.Where(f => Is(f, BoardExtensions)).Select(f => File(f, Icons.Board)));
        Section(sections, "rules", Icons.Settings, files.Where(f => Is(f, RuleExtensions)).Select(f => File(f, Icons.Settings)));
        Section(sections, "libraries", Icons.Component, Libraries(directory, files));
        Section(sections, "outputs", Icons.Save, Outputs(directory, files));
        Section(sections, "other", Icons.File, files.Where(Other).Select(f => File(f, Icons.File)));

        // A project opens showing what it holds: the sections stand open until the reader closes one. Sheets stay
        // shut, because opening one reads another file.
        if (!string.Equals(_seededFor, directory, StringComparison.Ordinal))
        {
            _seededFor = directory;
            foreach (var section in sections)
            {
                _expanded.Add(section.Key);
            }
        }

        return sections;

        static void Section(List<Node> sections, string key, string icon, IEnumerable<Node> children)
        {
            var list = children.ToList();
            if (list.Count == 0)
            {
                return;
            }

            sections.Add(new Node($"section:{key}", Tr.T($"shell.project.section.{key}").ToUpperInvariant(), icon)
            {
                Detail = list.Count > 1 ? list.Count.ToString(Tr.Culture) : null,
                IsSection = true,
                Children = () => list,
            });
        }
    }

    /// <summary>A schematic and, under it, the sheets its own plugin reports — an instance at a time, not a file list.</summary>
    private Node Schematic(string path) => new(path, System.IO.Path.GetFileNameWithoutExtension(path), Icons.Sheets)
    {
        Path = path,
        Detail = KindTag(path),
        Children = () => Described(path, null),
    };

    /// <summary>
    /// The level under one appearance of a file. The same sheet file can sit under two parents, so the key carries
    /// the appearance as well — otherwise opening one would open both.
    /// </summary>
    private IReadOnlyList<Node> Described(string path, string? instance) =>
    [
        .. _shell.ProjectStructure.Describe(path, instance).Select((node, index) => new Node($"{path}|{instance}|{index}:{node.Title}", node.Title, node.IconKey)
        {
            Detail = node.Detail,
            Path = node.Path,
            Instance = node.Instance,
            Children = node.Path is { } child && Is(child, SchematicExtensions)
                ? () => Described(child, node.Instance)
                : node.Children.Count > 0 ? () => Wrap(path, node.Children) : null,
        }),
    ];

    private static IReadOnlyList<Node> Wrap(string owner, IReadOnlyList<ProjectNode> nodes) =>
    [
        .. nodes.Select((node, index) => new Node($"{owner}|{index}:{node.Title}", node.Title, node.IconKey)
        {
            Detail = node.Detail,
            Path = node.Path,
            Children = node.Children.Count > 0 ? () => Wrap(owner, node.Children) : null,
        }),
    ];

    private IEnumerable<Node> Libraries(string directory, IReadOnlyList<string> files)
    {
        foreach (string file in files.Where(f => Is(f, LibraryExtensions) || LibraryTables.Contains(FileName(f), StringComparer.Ordinal)))
        {
            yield return File(file, Icons.Component);
        }

        foreach (string folder in SafeDirectories(directory).Where(d => d.EndsWith(".pretty", StringComparison.OrdinalIgnoreCase)))
        {
            string library = folder;
            yield return new Node(folder, FileName(folder), Icons.Component)
            {
                Detail = SafeFiles(folder).Count(f => Is(f, [".kicad_mod"])).ToString(Tr.Culture),
                Children = () => [.. SafeFiles(library).Where(f => Is(f, [".kicad_mod"])).Select(f => File(f, Icons.Pad))],
            };
        }
    }

    private IEnumerable<Node> Outputs(string directory, IReadOnlyList<string> files)
    {
        foreach (string folder in SafeDirectories(directory).Where(IsOutputFolder))
        {
            string output = folder;
            yield return new Node(folder, FileName(folder), Icons.Folder)
            {
                Children = () => Entries(output),
            };
        }

        foreach (string file in files.Where(f => Is(f, OutputExtensions)))
        {
            yield return File(file, Icons.Save);
        }
    }

    private bool Other(string file) =>
        Extension(file) != ProjectExtension
        && !Is(file, SchematicExtensions) && !Is(file, BoardExtensions) && !Is(file, RuleExtensions)
        && !Is(file, LibraryExtensions) && !Is(file, OutputExtensions)
        && !LibraryTables.Contains(FileName(file), StringComparer.Ordinal) && !IsNoise(file);

    // ——— The files view: the folders as they are ———

    private IReadOnlyList<Node> Entries(string directory)
    {
        var nodes = new List<Node>();
        foreach (string folder in SafeDirectories(directory).Where(d => _showEverything || !IsNoise(d)).OrderBy(FileName, StringComparer.CurrentCultureIgnoreCase))
        {
            string child = folder;
            nodes.Add(new Node(folder, FileName(folder), Icons.Folder) { Children = () => Entries(child) });
        }

        foreach (string file in SafeFiles(directory).Where(f => _showEverything || !IsNoise(f)).OrderBy(FileName, StringComparer.CurrentCultureIgnoreCase))
        {
            nodes.Add(DiskFile(file));
        }

        return nodes;
    }

    // ——— Rows ———

    private void AddRows(StackPanel host, IReadOnlyList<Node> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            bool open = _expanded.Contains(node.Key) || (_filter.Length > 0 && node.Children is not null);
            host.Children.Add(Row(node, depth, open));

            if (open && depth < MaxDepth && node.Children is { } children)
            {
                var below = children();
                if (_filter.Length > 0)
                {
                    below = [.. below.Select(n => Matching(n, depth + 1)).OfType<Node>()];
                }

                AddRows(host, below, depth + 1);
            }
        }
    }

    private Control Row(Node node, int depth, bool open)
    {
        var line = new DockPanel { LastChildFill = true };

        var twisty = new Button
        {
            Classes = { "flat" },
            Width = 14,
            Height = 16,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(depth * 13, 0, 2, 0),
            Content = node.Children is null ? null : Icons.Draw(open ? Icons.ChevronDown : Icons.ChevronRight, 9),
            IsHitTestVisible = node.Children is not null,
        };

        twisty.Click += (_, e) =>
        {
            Toggle(node);
            e.Handled = true;
        };

        DockPanel.SetDock(twisty, Dock.Left);
        line.Children.Add(twisty);

        if (!node.IsSection && Icons.Draw(node.IconKey, 14) is { } icon)
        {
            icon.Margin = new Thickness(0, 0, 6, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
        }

        if (node.Detail is { Length: > 0 } detail)
        {
            var note = Ui.Mono(detail, "faint");
            note.Margin = new Thickness(8, 0, 0, 0);
            note.MaxWidth = 84;
            DockPanel.SetDock(note, Dock.Right);
            line.Children.Add(note);
        }

        var title = node.IsSection ? Ui.Overline(node.Title) : Ui.Text(node.Title, Openable(node) ? string.Empty : "faint");
        title.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(title);

        var row = new Button { Classes = { "row" }, Padding = new Thickness(2, 3), Content = line };
        // A reused sheet is one tab; only the row of the appearance it is showing reads as the open one.
        if (node.Path is { } path && string.Equals(path, _shell.ActiveDocument?.FilePath, StringComparison.Ordinal)
            && (node.Instance is null || string.Equals(node.Instance, _shell.ActiveDocument?.Instance, StringComparison.Ordinal)))
        {
            row.Classes.Add("selected");
        }

        row.Click += (_, _) =>
        {
            if (Openable(node) && node.Path is { } file)
            {
                _ = Open(file, node.Instance);
            }
            else
            {
                Toggle(node);
            }
        };

        return row;
    }

    /// <summary>
    /// Opens a node and draws the tree again: switching a tab to another appearance of its sheet does not change the
    /// active document, so nothing else would move the highlight to the row that was clicked.
    /// </summary>
    private async Task Open(string file, string? instance)
    {
        await _shell.OpenAsync(file, instance);
        Render();
    }

    private void Toggle(Node node)
    {
        if (node.Children is null)
        {
            return;
        }

        if (!_expanded.Remove(node.Key))
        {
            _expanded.Add(node.Key);
        }

        Render();
    }

    private bool Openable(Node node) => node.Path is { } path && _shell.DocumentTypes.FindFor(path) is not null;

    /// <summary>The node, kept only if it or something under it matches the filter.</summary>
    private Node? Matching(Node node, int depth)
    {
        bool hit = node.Title.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
            || (node.Detail?.Contains(_filter, StringComparison.CurrentCultureIgnoreCase) ?? false);

        if (hit || depth >= MaxDepth || node.Children is null)
        {
            return hit ? node : null;
        }

        var kept = node.Children().Select(n => Matching(n, depth + 1)).OfType<Node>().ToList();
        return kept.Count > 0 ? node with { Children = () => kept } : null;
    }

    // ——— Files on disk ———

    /// <summary>
    /// A file under a section. The name drops the extension so it survives a narrow dock, and a short tag says what
    /// the file is — in a project whose sheet, board and project file share one name, the tag is the whole difference.
    /// </summary>
    private static Node File(string path, string icon) => new(path, System.IO.Path.GetFileNameWithoutExtension(path), icon)
    {
        Path = path,
        Detail = KindTag(path),
    };

    /// <summary>"lamp.kicad_sch" is a "sch"; anything not of KiCad keeps its own extension.</summary>
    private static string KindTag(string path)
    {
        string extension = Extension(path).TrimStart('.');
        return extension.StartsWith("kicad_", StringComparison.Ordinal) ? extension["kicad_".Length..] : extension;
    }

    /// <summary>A file in the files view: there a name is the whole name, as it is on disk.</summary>
    private static Node DiskFile(string path) => new(path, FileName(path), IconFor(path))
    {
        Path = path,
    };

    private static string FileName(string path) => System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));

    private static string Extension(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant();

    private static bool Is(string path, IReadOnlyList<string> extensions) => extensions.Contains(Extension(path), StringComparer.Ordinal);

    private static bool IsOutputFolder(string folder) => OutputFolders.Contains(FileName(folder).ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>Backups, autosaves and caches: real files, but not what a project is about.</summary>
    private static bool IsNoise(string path)
    {
        string name = FileName(path);
        return name.StartsWith('.') || name.StartsWith('~') || name.EndsWith("-bak", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-backups", StringComparison.OrdinalIgnoreCase) || name is "fp-info-cache"
            || Extension(path) is ".bak" or ".kicad_prl" or ".lck";
    }

    private static string IconFor(string path) => Extension(path) switch
    {
        ".kicad_pcb" => Icons.Board,
        ".kicad_sch" => Icons.Sheets,
        ".kicad_sym" or ".kicad_mod" => Icons.Component,
        ".kicad_pro" or ".kicad_dru" or ".kicad_wks" => Icons.Settings,
        _ => Icons.File,
    };

    private static IReadOnlyList<string> SafeFiles(string directory)
    {
        try
        {
            return [.. Directory.EnumerateFiles(directory)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> SafeDirectories(string directory)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(directory)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
