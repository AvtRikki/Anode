using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Anode.Sdk;

namespace Anode.Workbench.ViewModels;

/// <summary>
/// The window's own texts, so XAML binds instead of hardcoding. Everything is re-read when the language changes.
/// </summary>
public sealed class ShellStrings : ObservableObject
{
    private static readonly string[] Names = [.. typeof(ShellStrings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)];

    public string ProjectSwitcher => Tr.T("shell.project.switcher");

    public string Branch => Tr.T("shell.project.branch");

    public string SearchPlaceholder => Tr.T("shell.search.placeholder");

    public string HintLeftDock => Tr.T("shell.hint.leftDock");

    public string HintRightDock => Tr.T("shell.hint.rightDock");

    public string HintBottomDock => Tr.T("shell.hint.bottomDock");

    public string RailMore => Tr.T("shell.rail.more");

    public string SlideOverPin => Tr.T("shell.slideOver.pin");

    public string SlideOverPinHint => Tr.T("shell.slideOver.pinHint");

    public string TabsEmptyTitle => Tr.T("shell.tabs.emptyTitle");

    public string TabsEmptyHint => Tr.T("shell.tabs.emptyHint");

    public string TabClose => Tr.T("shell.tabs.close");

    public string PalettePlaceholder => Tr.T("shell.palette.placeholder");

    public string PaletteEmpty => Tr.T("shell.palette.empty");

    public string PaletteFooter => Tr.T("shell.palette.footer");

    public string PaletteBeta => Tr.T("shell.palette.beta");

    public string StatusBusy => Tr.T("shell.status.busy");

    public void RaiseAll()
    {
        foreach (string name in Names)
        {
            OnPropertyChanged(name);
        }
    }
}
