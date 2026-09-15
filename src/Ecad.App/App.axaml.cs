using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecad.App.ViewModels;

namespace Ecad.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            if (desktop.Args is [var path, ..] && File.Exists(path))
            {
                _ = viewModel.OpenAsync(path);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
