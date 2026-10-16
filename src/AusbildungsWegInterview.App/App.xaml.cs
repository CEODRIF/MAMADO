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
        // Bounded teardown on the way out. WPF does not wait for async disposal, so
        // a plain fire-and-forget could let the process exit while the pipeline
        // still has a transcription subprocess running (orphaned child). The entire
        // dispose chain is ConfigureAwait(false) — thread-pool only, no dispatcher —
        // so this bounded wait cannot deadlock; it simply gives the cancel→kill
        // path up to 2 s, after which the exit wins and the OS reclaims the rest.
        try
        {
            var services = _services; // null-guarded: OnExit must never throw
            if (services is not null)
                services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Exiting wins: capture, subprocesses and temp files are best-effort.
        }
        base.OnExit(e);
    }
}
