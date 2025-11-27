using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DimonSmart.LocalOllamaMCPServer
{
    sealed class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();

            // Configure Logging to write to Stderr so it doesn't interfere with JSON-RPC on Stdout
            services.AddLogging(configure =>
            {
                configure.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
                configure.SetMinimumLevel(LogLevel.Information);
            });

            services.AddHttpClient<IOllamaService, OllamaService>();
            services.AddSingleton<McpServer>();

            var serviceProvider = services.BuildServiceProvider();

            var server = serviceProvider.GetRequiredService<McpServer>();
            await server.RunAsync().ConfigureAwait(false);
        }
    }

    internal sealed class McpServer
    {
        private static readonly string[] RequiredFields = ["model_name", "prompt"];
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
                                        options = new { type = "object", description = "Optional parameters for the model (e.g., temperature, top_p).", additionalProperties = true }
                                    },
                                    required = RequiredFields
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

                        var result = await _ollamaService.GenerateAsync(model, prompt, options).ConfigureAwait(false);

                        return new CallToolResult
                        {
                            Content = new List<ToolContent>
                            {
                                new ToolContent { Type = "text", Text = result }
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
