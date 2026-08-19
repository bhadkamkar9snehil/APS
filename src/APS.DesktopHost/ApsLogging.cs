using APS.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace APS.DesktopHost;

/// <summary>
/// Desktop-host logging composition root. Application/infrastructure/UI code logs exclusively
/// through Microsoft.Extensions.Logging (ILogger&lt;T&gt;). Serilog is the single provider/sink
/// implementation, confined to this file - not a second application logging API.
/// </summary>
internal static class ApsLogging
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static IHostBuilder UseApsLogging(this IHostBuilder builder, LocalApplicationPaths paths)
    {
        paths.EnsureDirectories();

        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "APS")
            .Enrich.WithProperty("MachineName", Environment.MachineName)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .WriteTo.File(
                paths.LogFilePattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 25 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: OutputTemplate)
            .CreateLogger();

        // Host.CreateDefaultBuilder registers Console/Debug/EventSource/EventLog providers. APS
        // Planner is a desktop app with one deliberate logging pipeline, so remove those implicit
        // providers before installing Serilog. Every ILogger<T> event then has exactly one backend.
        return builder
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseSerilog(logger, dispose: true);
    }
}
