using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace Anode.Workbench.Controls;

/// <summary>
/// Panel and document views live longer than the containers that show them: a panel moves between a dock stack, the
/// rail slide-over and back, a document moves between panes. A control may have only one parent, and the old container
/// keeps it until its next layout pass, so it is taken out of the old one before being handed to the new one.
/// </summary>
public static class ControlHost
{
    public static T? Detach<T>(T? control)
        where T : Control
    {
        switch (control?.GetVisualParent())
        {
            case ContentPresenter presenter:
                presenter.Content = null;
                presenter.UpdateChild();
                break;
            case ContentControl content:
                content.Content = null;
                break;
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
        }

        return control;
    }
}
