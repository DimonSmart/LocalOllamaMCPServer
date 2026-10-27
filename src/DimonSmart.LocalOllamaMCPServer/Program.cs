using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalOllamaMCPServer;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            Console.WriteLine($"DimonSmart.LocalOllamaMCPServer v{version}");
            return;
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        var idleMinutes = builder.Configuration.GetValue<int?>("Sessions:IdleTimeoutMinutes") ?? 60;
        var timeoutSeconds = builder.Configuration.GetValue<int?>("Ollama:RequestTimeoutSeconds") ?? 3600;
        if (idleMinutes <= 0 || timeoutSeconds <= 0)
            throw new ArgumentException("Session idle timeout and Ollama request timeout must be positive.");

        builder.Services.AddSingleton(new TestSessionManager(TimeProvider.System, TimeSpan.FromMinutes(idleMinutes)));
        builder.Services.AddHostedService<SessionExpiryService>();
        builder.Services.AddHttpClient<OpenAiOllamaClient>(client =>
        {
            client.BaseAddress = new Uri("http://127.0.0.1:11434/v1/");
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });

        builder.Services.AddMcpServer(options =>
            {
                options.ServerInfo ??= new()
                {
                    Name = "dimonsmart-local-ollama-mcp-server",
                    Version = "3.0.0"
                };
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }
}
