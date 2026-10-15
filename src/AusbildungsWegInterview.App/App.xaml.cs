using System.Windows;
using AusbildungsWegInterview.Infrastructure.Config;

namespace AusbildungsWegInterview.App;

/// <summary>
/// WPF application entry point. Applies the persisted UI theme and language,
/// builds the service bundle once, shows the main window.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply the persisted theme before any window is created so the very
        // first frame is already skinned.
        ThemeApplier.Apply(ConfigurationStore.SafeLoad().Theme);

        _services = AppServiceFactory.Create();
        var window = new MainWindow(_services); // the window builds its view model (MVVM)
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
            _ = _services.DisposeAsync().AsTask();
        base.OnExit(e);
    }
}
