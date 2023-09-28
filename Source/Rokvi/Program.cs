namespace Rokvi;

using Rokvi.Options;
using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Extensions.Hosting;
using System.Globalization;
using Serilog.Formatting.Compact;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = CreateBootstrapLogger();
        IHost? host = null;

        try
        {
            Log.Information("Initialising.");
            host = CreateHostBuilder(args).Build();

            host.LogApplicationStarted();
            await host.RunAsync().ConfigureAwait(false);
            host.LogApplicationStopped();

            return 0;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception exception)
#pragma warning restore CA1031 // Do not catch general exception types
        {
            host!.LogApplicationTerminatedUnexpectedly(exception);

            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        new HostBuilder()
            .UseContentRoot(Directory.GetCurrentDirectory())
            .ConfigureHostConfiguration(
                configurationBuilder => configurationBuilder.AddCustomBootstrapConfiguration(args))
            .ConfigureAppConfiguration(
                (hostingContext, configurationBuilder) =>
                {
                    hostingContext.HostingEnvironment.ApplicationName = AssemblyInformation.Current.Product;
                    configurationBuilder.AddCustomConfiguration(hostingContext.HostingEnvironment, args);
                })
            .UseSerilog(ConfigureReloadableLogger)
            .UseDefaultServiceProvider(
                (context, options) =>
                {
                    var isDevelopment = context.HostingEnvironment.IsDevelopment();
                    options.ValidateScopes = isDevelopment;
                    options.ValidateOnBuild = isDevelopment;
                })
            .ConfigureWebHost(ConfigureWebHostBuilder)
            .UseConsoleLifetime();

    private static void ConfigureWebHostBuilder(IWebHostBuilder webHostBuilder) =>
        webHostBuilder
            .UseKestrel(
                (builderContext, options) =>
                {
                    options.AddServerHeader = false;
                    options.Configure(
                        builderContext.Configuration.GetRequiredSection(nameof(ApplicationOptions.Kestrel)),
                        reloadOnChange: false);
                })
            // Used for IIS and IIS Express for in-process hosting. Use UseIISIntegration for out-of-process hosting.
            .UseIIS()
            .UseStartup<Startup>();

    /// <summary>
    /// Creates a logger used during application initialisation.
    /// <see href="https://nblumhardt.com/2020/10/bootstrap-logger/"/>.
    /// </summary>
    /// <returns>A logger that can load a new configuration.</returns>
    private static ReloadableLogger CreateBootstrapLogger() =>
        new LoggerConfiguration()
            .WriteTo.Console(formatProvider: new CultureInfo("is-IS"))
            .WriteTo.Debug(formatProvider: new CultureInfo("is-IS"))
            .CreateBootstrapLogger();

    /// <summary>
    /// Configures a logger used during the applications lifetime.
    /// <see href="https://nblumhardt.com/2020/10/bootstrap-logger/"/>.
    /// </summary>
    private static void ConfigureReloadableLogger(
        HostBuilderContext context,
        IServiceProvider services,
        LoggerConfiguration configuration) =>
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.WithProperty("Application", context.HostingEnvironment.ApplicationName)
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .WriteTo.Conditional(
                x => context.HostingEnvironment.IsProduction(),
                x => x.ApplicationInsights(
                    services.GetRequiredService<TelemetryConfiguration>(),
                    TelemetryConverter.Traces))
            .WriteTo.Conditional(
                x => context.HostingEnvironment.IsDevelopment(),
                x => x.Console(new CompactJsonFormatter()).WriteTo.Debug(new CompactJsonFormatter()))
            .WriteTo.Sentry("https://7538722615144de3f646d1674e195311@o188653.ingest.sentry.io/4505949364551680", formatProvider: new CultureInfo("is-IS"));
}
