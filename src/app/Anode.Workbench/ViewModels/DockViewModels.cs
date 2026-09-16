using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Anode.Sdk;
using Anode.Workbench.Services;

namespace Anode.Workbench.ViewModels;

public sealed partial class PanelTabViewModel(PanelDescriptor descriptor, ShellViewModel shell) : ObservableObject
{
    public PanelDescriptor Descriptor { get; } = descriptor;

    public string Title => Descriptor.Title;

    public string SendToRailLabel => Tr.T("shell.rail.toRail");

    public Control Content => shell.PanelContent(Descriptor);

    public DockStackViewModel? Stack { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [RelayCommand]
    private void Activate() => Stack?.Select(this);

    [RelayCommand]
    private void SendToRail() => shell.SendToRail(Descriptor.Id);
}

/// <summary>A stack: tab header + body + collapse.</summary>
public sealed partial class DockStackViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public DockStackViewModel(DockStack stack, ShellViewModel shell, string? activeId, bool collapsed)
    {
        _shell = shell;
        Key = stack.Key;
        Side = stack.Side;
        foreach (var panel in stack.Panels)
        {
            Tabs.Add(new PanelTabViewModel(panel, shell) { Stack = this });
        }

        IsCollapsed = collapsed;
        ActiveTab = Tabs.FirstOrDefault(t => t.Descriptor.Id == activeId) ?? Tabs.FirstOrDefault();
    }

    public string Key { get; }

    public DockSide Side { get; }

    public ObservableCollection<PanelTabViewModel> Tabs { get; } = [];

    public string CollapseTip => Tr.T("shell.stack.collapse");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Content))]
    public partial PanelTabViewModel? ActiveTab { get; set; }

    [ObservableProperty]
    public partial bool IsCollapsed { get; set; }

    public Control? Content => IsCollapsed ? null : ActiveTab?.Content;

    public string ChevronText => IsCollapsed ? "›" : "⌄";

    public void Select(PanelTabViewModel tab)
    {
        ActiveTab = tab;
        IsCollapsed = false;
    }

    [RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    partial void OnActiveTabChanged(PanelTabViewModel? oldValue, PanelTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsActive = false;
        }

        if (newValue is not null)
        {
            newValue.IsActive = true;
        }

        _shell.RememberStack(Key, newValue?.Descriptor.Id, IsCollapsed);
    }

    partial void OnIsCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(ChevronText));
        _shell.RememberStack(Key, ActiveTab?.Descriptor.Id, value);
    }
}

/// <summary>A panel stored in the icon rail. Single click shows it over the canvas; double click pins it as a dock.</summary>
public sealed partial class RailItemViewModel(PanelDescriptor descriptor, ShellViewModel shell) : ObservableObject
{
    private Control? _icon;

    public PanelDescriptor Descriptor { get; } = descriptor;

    public bool HasIcon => Icons.Has(Descriptor.IconKey);

    public Control? Icon => _icon ??= Icons.Draw(Descriptor.IconKey);

    public string Label => Descriptor.RailLabel.Length > 0
        ? Descriptor.RailLabel
        : Descriptor.Title[..Math.Min(2, Descriptor.Title.Length)];

    public string Title => Descriptor.Title;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [RelayCommand]
    private void Toggle() => shell.ToggleSlideOver(Descriptor);

    [RelayCommand]
    private void Pin() => shell.PinFromRail(Descriptor.Id);
}

public sealed partial class PaletteViewModel(ICommandRegistry commands) : ObservableObject
{
    public ObservableCollection<CommandMatch> Results { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedIndex { get; set; } = -1;

    public CommandMatch? Selected => SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    public void Refresh()
    {
        Results.Clear();
        foreach (var match in CommandMatcher.Match(commands.Commands.Where(SafeCanExecute), Query))
        {
            Results.Add(match);
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
    }

    public void Move(int delta)
    {
        if (Results.Count > 0)
        {
            SelectedIndex = (SelectedIndex + delta + Results.Count) % Results.Count;
        }
    }

    partial void OnQueryChanged(string value) => Refresh();

    private static bool SafeCanExecute(CommandDescriptor command)
    {
        try
        {
            return command.CanExecute();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
