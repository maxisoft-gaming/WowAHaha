using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using Lamar;
using Maxisoft.Utils.Objects;
using Maxisoft.Utils.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using WowAHaha.GameDataApi;
using WowAHaha.GameDataApi.Http;

namespace WowAHaha
{
    public class LoggingSettings
    {
        public bool EnableConsole { get; set; } = true;
        public bool EnableDebug { get; set; } =
#if _DEBUG
            true;
#else
            false;
#endif
        public bool? EnableSystemdConsole { get; set; }
        public bool EnableEventLog { get; set; }
    }


    public class ProgramConfigurations
    {
        public bool IgnoreChina { get; set; } = true;

        public int MaxWorkers { get; set; } =
#if DEBUG
            1;
#else
            Environment.ProcessorCount;
#endif
    }

    // ReSharper disable once ClassNeverInstantiated.Global

    internal class Program
    {
        private static readonly LoggingSettings LoggingSettings = new();
        private static MonotonicTimestamp _cancellationTimestamp = default;

        [Experimental("EXTEXP0003")]
        static async Task Main(string[] args)
        {
            Container container = CreateContainer();
            var logger = container.GetInstance<ILogger<Program>>();
            var cancellationTokenSource = container.GetInstance<CancellationTokenSource>();
            Console.CancelKeyPress += (_, e) =>
            {
                logger.LogInformation("Canceling due to console cancel event");
                cancellationTokenSource.Cancel();
                if (_cancellationTimestamp.IsZero)
                {
                    _cancellationTimestamp = MonotonicTimestamp.Now;
                }

                e.Cancel = (MonotonicTimestamp.Now - _cancellationTimestamp).Duration() < TimeSpan.FromSeconds(5);
            };
            CancellationToken cancellationToken = container.GetInstance<Boxed<CancellationToken>>();

            var services = container.GetAllInstances<IRunnableService>().OrderBy(static s => -s.Priority).ToImmutableList();
            if (services.Count == 0)
            {
                logger.LogError("no services configured");
            }

            await Parallel.ForEachAsync(services, cancellationToken, async (service, cancellationToken) =>
            {
                await Task.Delay(services.IndexOf(service) * 50, cancellationToken).ConfigureAwait(false);
                await service.Run(cancellationToken).ConfigureAwait(false);
            });
        }

        private static IConfiguration GetConfiguration()
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddEnvironmentVariables("AHaha_")
                .Build();
            return config;
        }

        private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static bool HasSystemd()
        {
            return (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INVOCATION_ID")));
        }

        private static Container CreateContainer()
        {
            IConfiguration config = GetConfiguration();
            config.GetSection("Logging.Settings").Bind(LoggingSettings);
            var programConfig = new ProgramConfigurations();
            Assembly assembly = typeof(Program).Assembly;
            var assemblyName = assembly.GetName().Name ?? "WowAHaha";
            Version? assemblyVersion = assembly.GetName().Version;
            config.GetSection(assemblyName).Bind(programConfig);

            var cts = new CancellationTokenSource();
            CancellationToken cancellationToken = cts.Token;

            var container = new Container(x =>
            {
                x.For<ProgramConfigurations>().Add(programConfig);
                x.For<IConfiguration>().Use(config);
                x.ForSingletonOf<CancellationTokenSource>().UseIfNone(cts);
                x.For<Boxed<CancellationToken>>().Use(context => context.GetInstance<CancellationTokenSource>().Token);
                x.ForSingletonOf<ProgramWorkerSemaphore>().UseIfNone<ProgramWorkerSemaphore>();
                //IConfigurationSection resilienceSettings = config.GetSection("Http.ResilienceSettings");
                x.ForSingletonOf<BattleNetWebApi>().Use<BattleNetWebApi>();
                x.AddHttpClient<BattleNetWebApi>(configureClient =>
                {
                    IConfigurationSection webConfig = BattleNetWebApi.GetConfigurationSection(config);
                    configureClient.BaseAddress = new Uri(webConfig.GetValue("BaseAddress", "https://us.api.blizzard.com") ?? "https://us.api.blizzard.com");
                    configureClient.Timeout = TimeSpan.FromSeconds(webConfig.GetValue<double>("Timeout", 60));
                    configureClient.DefaultRequestHeaders.Add("Accept", "application/json");
                    configureClient.DefaultRequestHeaders.Add("User-Agent",
                        webConfig.GetValue<string>("UserAgent", null!) ?? $"{assemblyName}/{assemblyVersion?.ToString() ?? "1.0"}");
                    configureClient.DefaultRequestVersion = new Version(2, 0);
                });
                /*.AddStandardResilienceHandler(options =>
                {
                    options.TotalRequestTimeout = new HttpTimeoutStrategyOptions()
                        { Timeout = TimeSpan.FromMinutes(10) };
                });*/
                x.AddLogging(log =>
                {
                    log.EnableEnrichment(configure =>
                    {
                        configure.CaptureStackTraces = true;
                        configure.IncludeExceptionMessage = true;
                        configure.UseFileInfoForStackTraces = true;
                    });
                    log.AddConfiguration(config.GetSection("Logging"));

                    if (LoggingSettings.EnableConsole)
                    {
                        log.AddConsole();
                    }

                    if (LoggingSettings.EnableDebug)
                    {
                        log.AddDebug();
                    }

                    if (LoggingSettings.EnableSystemdConsole ?? HasSystemd())
                    {
                        log.AddSystemdConsole();
                    }

                    if (IsWindows() && LoggingSettings.EnableEventLog)
                    {
#pragma warning disable CA1416
                        log.AddEventLog();
#pragma warning restore CA1416
                    }

                    log.SetMinimumLevel(LogLevel.Information);
# if DEBUG
                    log.SetMinimumLevel(LogLevel.Debug);
# endif
                });

                x.Scan(scanner =>
                {
                    scanner.TheCallingAssembly();
                    scanner.AddAllTypesOf<IRunnableService>();
                    scanner.WithDefaultConventions();
                });
            });
            return container;
        }
    }
}