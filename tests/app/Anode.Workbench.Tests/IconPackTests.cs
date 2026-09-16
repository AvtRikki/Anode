using System.Reflection;
using Anode.Sdk;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Anode.Workbench.Tests;

/// <summary>
/// The icon pack: every name draws, every constant points at a real icon, and the whole set is rendered to
/// <c>snapshots/icons.png</c> so it can be looked at rather than argued about.
/// </summary>
public class IconPackTests
{
    [Fact]
    public Task Every_name_draws_and_every_constant_resolves() => ShellWindowTests.Dispatch(_ =>
    {
        Assert.NotEmpty(Icons.Names);

        foreach (string name in Icons.Names)
        {
            Assert.NotNull(Icons.Draw(name));
        }

        // A constant that points at a missing icon would only show up as a blank button at runtime.
        var constants = typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(constants);
        Assert.Equal([], constants.Where(name => !Icons.Has(name)));

        Assert.Null(Icons.Draw("no-such-icon"));
        Assert.False(Icons.Has(null));
    });

    [Fact]
    public Task A_plugin_can_register_an_icon_of_its_own() => ShellWindowTests.Dispatch(_ =>
    {
        const string name = "test.diode";
        Assert.False(Icons.Has(name));

        Icons.Register(name, null, "M3 3 L13 8 L3 13 Z M13 3 V13");

        Assert.True(Icons.Has(name));
        Assert.NotNull(Icons.Draw(name));
        Assert.Contains(name, Icons.Names);
    });

    [Fact]
    public Task The_pack_renders_as_a_sheet() => ShellWindowTests.Dispatch(directory =>
    {
        var sheet = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Avalonia.Thickness(18) };
        foreach (string name in Icons.Names.Order(StringComparer.Ordinal))
        {
            var cell = new StackPanel { Width = 96, Spacing = 6, Margin = new Avalonia.Thickness(0, 10) };
            cell.Children.Add(new ContentControl { Content = Icons.Draw(name, 24), HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(Ui.Mono(name, "dim"));
            cell.Children[1].SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            sheet.Children.Add(cell);
        }

        var window = new Window
        {
            Width = 880,
            Height = 520,
            Content = new ScrollViewer { Content = sheet },
        };

        window.Show();
        ShellWindowTests.Snapshot(window, directory, "icons");
        window.Close();
    });
}
