using System.Windows;
using APS.Application;
using APS.DesktopHost.Updates;
using APS.Infrastructure;
using APS.UI.State;
using APS.UI.Theme;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace APS.DesktopHost;

public partial class App : System.Windows.Application
{
    private const string UpdateRepositoryUrl = "https://github.com/bhadkamkar9snehil/APS";
    private readonly IHost _host;
    private readonly LocalApplicationPaths _paths;
    private readonly ILogger<App> _log;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _paths = LocalApplicationPaths.ForCurrentUser();

        _host = Host.CreateDefaultBuilder()
            .UseApsLogging(_paths)
            .ConfigureServices((context, services) =>
            {
                services.AddWpfBlazorWebView();
                services.AddApsInfrastructure(context.Configuration);
                services.AddSingleton<VelopackUpdateService>(sp => new VelopackUpdateService(
                    new UpdateManager(new GithubSource(UpdateRepositoryUrl, accessToken: null, prerelease: false)),
                    () => Dispatcher.BeginInvoke(new Action(() => Shutdown())),
                    sp.GetRequiredService<ILogger<VelopackUpdateService>>()));
                services.AddSingleton<IUpdateService>(sp => sp.GetRequiredService<VelopackUpdateService>());
                services.AddHostedService<UpdateCheckWorker>();
                services.AddSingleton<PlannerWorkspaceState>();
                services.AddScoped<PlannerCockpitState>();
                services.AddScoped<ThemeService>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        _log = _host.Services.GetRequiredService<ILogger<App>>();

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _log.LogInformation("Starting APS Planner. DataDirectory={DataDirectory}", _paths.DataDirectory);

            // Must run before StartAsync(): OperationCommitmentHostedService (and any other
            // BackgroundService) starts querying the database as soon as the host starts, racing
            // a migration that runs afterward.
            await _host.Services.MigrateApsDatabaseAsync();
            _log.LogInformation("APS database migration check completed.");

            await _host.StartAsync();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _log.LogInformation("APS Planner startup completed.");
        }
        catch (Exception exception)
        {
            _log.LogCritical(exception, "The application failed to start.");
            MessageBox.Show(
                $"APS Planner could not start.\n\n{exception.Message}\n\nDetails were saved to:\n{_paths.LogDirectory}",
                "APS Planner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _log.LogError(e.Exception, "Unhandled exception on the UI dispatcher.");
            MessageBox.Show(
                $"Something went wrong on this page.\n\n{e.Exception.Message}\n\nYou can keep using the app - try a different page or reload.",
                "APS Planner",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
            // Crash reporting must never become a second crash source.
        }

        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            _log.LogCritical(e.ExceptionObject as Exception, "Unhandled exception on a background thread. IsTerminating={IsTerminating}", e.IsTerminating);
        }
        catch
        {
            // Crash reporting must never become a second crash source.
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            _log.LogError(e.Exception, "An unobserved Task exception was raised.");
        }
        catch
        {
            // Crash reporting must never become a second crash source.
        }

        e.SetObserved();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        try
        {
            _log.LogInformation("Stopping APS Planner.");
            await _host.StopAsync();
        }
        finally
        {
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
