using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalOllamaMCPServer;

[McpServerToolType]
internal static class OllamaTools
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "query_ollama")]
    [Description("Send a prompt to a local Ollama model and get the response. Useful for testing prompts against small local models.")]
    public static async Task<string> QueryOllama(
        [Description("The name of the model to query (e.g., 'llama3', 'mistral').")]
        string model_name,
        [Description("The prompt text to send to the model.")]
        string prompt,
        [Description("Optional parameters for the model (e.g., temperature, top_p).")]
        JsonElement? options = null,
        [Description("Optional name of the Ollama server connection to use. If omitted, the default server is used.")]
        string? connection_name = null,
        IOllamaService? ollamaService = null,
        ILogger<IOllamaService>? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ollamaService);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("QueryOllama called: model={Model}, connection={Connection}", model_name, connection_name ?? "default");

        Dictionary<string, object>? optionsDict = null;
        if (options.HasValue && options.Value.ValueKind == JsonValueKind.Object)
        {
            optionsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(options.Value.GetRawText());
        }

        var result = await ollamaService.GenerateAsync(
            model_name,
            prompt,
            optionsDict,
            connection_name).ConfigureAwait(false);

        return result;
    }

    [McpServerTool(Name = "list_ollama_connections")]
    [Description("List available Ollama server connections.")]
    public static string ListOllamaConnections(
        IOllamaService? ollamaService = null,
        ILogger<IOllamaService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ollamaService);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("ListOllamaConnections called");

        var configs = ollamaService.GetConfigurations();
        var result = JsonSerializer.Serialize(configs, IndentedOptions);

        logger.LogInformation("Returning {Count} configurations", configs.Count());

        return result;
    }
}
