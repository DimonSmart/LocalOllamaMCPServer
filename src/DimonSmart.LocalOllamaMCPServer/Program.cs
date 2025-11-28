using System.Net.Http.Headers;
using System.Text;
using DimonSmart.LocalOllamaMCPServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalOllamaMCPServer;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure base path and configuration
        var basePath = AppContext.BaseDirectory;
        builder.Configuration
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("Ollama"));

        // Configure Logging to stderr (as required by MCP)
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Configure HttpClients for Ollama servers
        ConfigureHttpClients(builder.Services, builder.Configuration);

        // Register Ollama service
        builder.Services.AddSingleton<IOllamaService, OllamaService>();

        // Configure MCP Server with stdio transport
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo.Name = "dimonsmart-local-ollama-mcp-server";
                options.ServerInfo.Version = "1.0.0";
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }

    private static void ConfigureHttpClients(IServiceCollection services, IConfiguration configuration)
    {
        // Always register the IHttpClientFactory infrastructure
        services.AddHttpClient();

        var appConfig = configuration.GetSection("Ollama").Get<AppConfig>();
        
        if (appConfig == null)
        {
            return;
        }

        foreach (var serverConfig in appConfig.Servers)
        {
            services.AddHttpClient(serverConfig.Name, client =>
                {
                    client.BaseAddress = serverConfig.BaseUrl;
                })
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    var handler = new HttpClientHandler();
                    if (serverConfig.IgnoreSsl)
                    {
                        handler.ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    }
                    return handler;
                })
                .ConfigureHttpClient(client =>
                {
                    if (!string.IsNullOrEmpty(serverConfig.User) && !string.IsNullOrEmpty(serverConfig.Password))
                    {
                        var authenticationString = $"{serverConfig.User}:{serverConfig.Password}";
                        var base64EncodedAuthenticationString =
                            Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
                    }
                });
        }
    }
}
