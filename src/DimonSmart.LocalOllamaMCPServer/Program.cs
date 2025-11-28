using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DimonSmart.LocalOllamaMCPServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DimonSmart.LocalOllamaMCPServer
{
    sealed class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();

            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            services.Configure<AppConfig>(configuration.GetSection("Ollama"));

            // Configure Logging
            services.AddLogging(configure =>
            {
                configure.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
                configure.SetMinimumLevel(LogLevel.Information);
            });

            // Register HttpClients dynamically
            var appConfig = configuration.GetSection("Ollama").Get<AppConfig>() ?? new AppConfig();

            // Add default server from environment variable if not present in config but env var exists
            var envBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");
            if (!string.IsNullOrEmpty(envBaseUrl) && !appConfig.Servers.Any(s => s.BaseUrl.ToString() == envBaseUrl))
            {
                // This is a bit tricky since we are binding from config section. 
                // But we can just rely on the fact that if OLLAMA_BASE_URL is set, it might be used if we map it correctly.
                // For now, let's stick to the explicit config or standard env vars mapping if the user maps them to Ollama:Servers:0:...
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
                        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    }
                    return handler;
                })
                .ConfigureHttpClient(client =>
                {
                    if (!string.IsNullOrEmpty(serverConfig.User) && !string.IsNullOrEmpty(serverConfig.Password))
                    {
                        var authenticationString = $"{serverConfig.User}:{serverConfig.Password}";
                        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);
                    }
                });
            }

            services.AddSingleton<IOllamaService, OllamaService>();
            services.AddSingleton<McpServer>();

            var serviceProvider = services.BuildServiceProvider();

            var server = serviceProvider.GetRequiredService<McpServer>();
            await server.RunAsync().ConfigureAwait(false);
        }
    }

    internal sealed class McpServer
    {
        private static readonly string[] RequiredFields = ["model_name", "prompt"];
        private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };
        private readonly IOllamaService _ollamaService;
        private readonly ILogger<McpServer> _logger;

        public McpServer(IOllamaService ollamaService, ILogger<McpServer> logger)
        {
            _ollamaService = ollamaService;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            _logger.LogInformation("MCP Server starting...");
            var input = Console.OpenStandardInput();
            var output = Console.OpenStandardOutput();

            using var reader = new StreamReader(input);
            using var writer = new StreamWriter(output) { AutoFlush = true };

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var request = JsonSerializer.Deserialize<JsonRpcRequest>(line);
                    if (request == null) continue;

                    _logger.LogInformation("Received request: {Method}", request.Method);

                    object? responseResult = null;
                    JsonRpcError? error = null;

                    try
                    {
                        responseResult = await HandleRequestAsync(request).ConfigureAwait(false);
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogError(ex, "Invalid argument in request");
                        error = new JsonRpcError { Code = -32602, Message = ex.Message };
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogError(ex, "Error handling request");
                        error = new JsonRpcError { Code = -32603, Message = ex.Message };
                    }

                    if (request.Id != null)
                    {
                        var response = new JsonRpcResponse
                        {
                            Id = request.Id,
                            Result = responseResult,
                            Error = error
                        };

                        var jsonResponse = JsonSerializer.Serialize(response);
                        await writer.WriteLineAsync(jsonResponse).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing line");
                }
            }
        }

        private async Task<object?> HandleRequestAsync(JsonRpcRequest request)
        {
            switch (request.Method)
            {
                case "initialize":
                    return new InitializeResult
                    {
                        ServerInfo = new ServerInfo
                        {
                            Name = "dimonsmart-local-ollama-mcp-server",
                            Version = "1.0.0"
                        },
                        Capabilities = new ServerCapabilities
                        {
                            Tools = new ToolsCapability { ListChanged = false }
                        }
                    };

                case "notifications/initialized":
                    return null;

                case "tools/list":
                    return new ToolListResult
                    {
                        Tools = new List<Tool>
                        {
                            new Tool
                            {
                                Name = "query_ollama",
                                Description = "Send a prompt to a local Ollama model and get the response. Useful for testing prompts against small local models.",
                                InputSchema = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        model_name = new { type = "string", description = "The name of the model to query (e.g., 'llama3', 'mistral')." },
                                        prompt = new { type = "string", description = "The prompt text to send to the model." },
                                        options = new { type = "object", description = "Optional parameters for the model (e.g., temperature, top_p).", additionalProperties = true },
                                        connection_name = new { type = "string", description = "Optional name of the Ollama server connection to use. If omitted, the default server is used." }
                                    },
                                    required = RequiredFields
                                }
                            },
                            new Tool
                            {
                                Name = "list_ollama_connections",
                                Description = "List available Ollama server connections.",
                                InputSchema = new
                                {
                                    type = "object",
                                    properties = new { },
                                    required = Array.Empty<string>()
                                }
                            }
                        }
                    };

                case "tools/call":
                    var callParams = JsonSerializer.Deserialize<CallToolParams>(JsonSerializer.Serialize(request.Params));
                    if (callParams?.Name == "query_ollama")
                    {
                        if (callParams.Arguments == null)
                            throw new ArgumentException("Arguments are required");

                        if (!callParams.Arguments.TryGetValue("model_name", out var modelObj))
                            throw new ArgumentException("model_name is required");
                        var model = modelObj?.ToString() ?? throw new ArgumentException("model_name cannot be null");

                        if (!callParams.Arguments.TryGetValue("prompt", out var promptObj))
                            throw new ArgumentException("prompt is required");
                        var prompt = promptObj?.ToString() ?? throw new ArgumentException("prompt cannot be null");

                        Dictionary<string, object>? options = null;
                        if (callParams.Arguments.TryGetValue("options", out var optionsObj) && optionsObj is JsonElement optionsElement)
                        {
                            options = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsElement.GetRawText());
                        }

                        string? connectionName = null;
                        if (callParams.Arguments.TryGetValue("connection_name", out var connObj))
                        {
                            connectionName = connObj?.ToString();
                        }

                        var result = await _ollamaService.GenerateAsync(model, prompt, options, connectionName).ConfigureAwait(false);

                        return new CallToolResult
                        {
                            Content = new List<ToolContent>
                            {
                                new ToolContent { Type = "text", Text = result }
                            },
                            IsError = false
                        };
                    }
                    else if (callParams?.Name == "list_ollama_connections")
                    {
                        var configs = _ollamaService.GetConfigurations();
                        var json = JsonSerializer.Serialize(configs, IndentedOptions);
                        return new CallToolResult
                        {
                            Content = new List<ToolContent>
                            {
                                new ToolContent { Type = "text", Text = json }
                            },
                            IsError = false
                        };
                    }
                    throw new InvalidOperationException($"Tool not found: {callParams?.Name}");

                default:
                    // For unknown methods, we can return null or throw. 
                    // But for notifications we should just ignore.
                    if (request.Method.StartsWith("notifications/", StringComparison.Ordinal)) return null;
                    throw new InvalidOperationException($"Method not found: {request.Method}");
            }
        }
    }
}
