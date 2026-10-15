using System.Windows;

namespace AusbildungsWegInterview.App;

/// <summary>
/// WPF application entry point. Builds the service bundle once, shows the main
/// floating window, and disposes services on exit. No StartupUri is used so the
/// window can receive the service bundle.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = AppServices.Create();
        var window = new MainWindow(_services);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_services is not null)
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // best-effort cleanup on exit
        }
        finally
        {
            _services = null;
        }
        base.OnExit(e);
    }
}
