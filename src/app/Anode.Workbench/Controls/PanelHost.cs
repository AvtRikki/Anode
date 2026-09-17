using Avalonia;
using Avalonia.Controls;
using Anode.Sdk;
using Anode.Workbench.ViewModels;

namespace Anode.Workbench.Controls;

/// <summary>
/// The one place a panel view is allowed to live. A panel has a single view that moves between a dock stack and the
/// slide-over over the canvas, and a control may have only one parent. Handing the view over by binding it into a new
/// container is not enough: the old container keeps holding it and puts it back at its next layout pass, which
/// Avalonia answers by tearing the window down. So a host <em>claims</em> the view through the workbench, the
/// workbench revokes the claim of whoever held it before, and a container that is no longer on screen can no longer
/// reach for it.
/// </summary>
public sealed class PanelHost : Decorator
{
    public static readonly StyledProperty<PanelDescriptor?> PanelProperty =
        AvaloniaProperty.Register<PanelHost, PanelDescriptor?>(nameof(Panel));

    public static readonly StyledProperty<ShellViewModel?> WorkbenchProperty =
        AvaloniaProperty.Register<PanelHost, ShellViewModel?>(nameof(Workbench));

    /// <summary>The panel to show, or null to show nothing (a collapsed stack, a closed slide-over).</summary>
    public PanelDescriptor? Panel
    {
        get => GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    /// <summary>The workbench the view is claimed from; bound from the window's data context.</summary>
    public ShellViewModel? Workbench
    {
        get => GetValue(WorkbenchProperty);
        set => SetValue(WorkbenchProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PanelProperty || change.Property == WorkbenchProperty)
        {
            Refresh();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Workbench?.ReleasePanels(this);
        Child = null;
    }

    /// <summary>Takes the panel back once the place is free again: the slide-over closed, or a stack was replaced.</summary>
    internal void Reclaim()
    {
        if (Child is null && Panel is { } panel && Workbench is { } workbench && !workbench.IsPanelShown(panel.Id))
        {
            Child = workbench.ClaimPanel(panel, this);
        }
    }

    /// <summary>Drops the view without touching the workbench: called by the workbench as it hands it to a new host.</summary>
    internal void Revoke() => Child = null;

    private void Refresh()
    {
        if (Workbench is not { } workbench || Panel is not { } panel)
        {
            Workbench?.ReleasePanels(this);
            Child = null;
            return;
        }

        Child = workbench.ClaimPanel(panel, this);
    }
}
