using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
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
#pragma warning disable CS0618
        IMcpServer? server = null,
#pragma warning restore CS0618
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ollamaService);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("QueryOllama called: model={Model}, connection={Connection}", model_name, connection_name ?? "default");

        List<string>? availableModels = null;
        try
        {
            var fetchedModels = await ollamaService.GetModelsAsync(connection_name).ConfigureAwait(false);
            availableModels = fetchedModels?
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error retrieving model list. Proceeding without validation.");
        }

        if (availableModels is { Count: > 0 })
        {
            var connectionLabel = connection_name ?? "default";
            var modelExists = availableModels.Any(m => string.Equals(m, model_name, StringComparison.OrdinalIgnoreCase));

            if (!modelExists)
            {
                if (server is null)
                {
                    return $"Error: Model '{model_name}' not found.";
                }

                var replacement = await PromptForModelSelectionAsync(
                    model_name,
                    connectionLabel,
                    availableModels,
                    server,
                    logger,
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(replacement))
                {
                    return $"Error: Model '{model_name}' not found and no alternative was selected.";
                }

                model_name = replacement;
            }
        }

        Dictionary<string, object>? optionsDict = null;
        if (options.HasValue && options.Value.ValueKind == JsonValueKind.Object)
        {
            optionsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(options.Value.GetRawText());
        }

        try
        {
            var result = await ollamaService.GenerateAsync(
                model_name,
                prompt,
                optionsDict,
                connection_name).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing QueryOllama");
            return $"Error: {ex.Message}";
        }
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

    [McpServerTool(Name = "list_ollama_models")]
    [Description("List available models on an Ollama server.")]
    public static async Task<string> ListOllamaModels(
        [Description("Optional name of the Ollama server connection to use. If omitted, the default server is used.")]
        string? connection_name = null,
        IOllamaService? ollamaService = null,
        ILogger<IOllamaService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ollamaService);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("ListOllamaModels called: connection={Connection}", connection_name ?? "default");

        try
        {
            var models = await ollamaService.GetModelsAsync(connection_name).ConfigureAwait(false);
            return JsonSerializer.Serialize(models, IndentedOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing ListOllamaModels");
            return $"Error: {ex.Message}";
        }
    }

    private static async Task<string?> PromptForModelSelectionAsync(
        string missingModel,
        string connectionLabel,
        List<string> availableModels,
#pragma warning disable CS0618
        IMcpServer server,
#pragma warning restore CS0618
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Model '{Model}' was not found on '{Connection}'. Starting elicitation.", missingModel, connectionLabel);

        if (availableModels.Count == 0)
        {
            logger.LogWarning("No models available to present for connection '{Connection}'.", connectionLabel);
            return null;
        }

        var schema = new ElicitRequestParams.RequestSchema
        {
            Required = new List<string> { "modelName" }
        };

        schema.Properties["modelName"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
        {
            Title = "Select an Ollama model",
            Description = $"Pick a model available on '{connectionLabel}'.",
            Enum = availableModels,
            Default = availableModels[0]
        };

        var request = new ElicitRequestParams
        {
            Message = $"The model '{missingModel}' was not found on '{connectionLabel}'. Please choose another model to continue.",
            RequestedSchema = schema
        };

        ElicitResult elicitationResult;
        try
        {
#pragma warning disable CS0618
            elicitationResult = await server.ElicitAsync(request, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Elicitation failed or is not supported by the client.");
            return null;
        }

        if (!elicitationResult.IsAccepted || elicitationResult.Content is null)
        {
            logger.LogInformation("Elicitation declined or cancelled by the user.");
            return null;
        }

        if (elicitationResult.Content.TryGetValue("modelName", out var selectedModelElement))
        {
            var selectedModel = selectedModelElement.GetString();
            if (!string.IsNullOrWhiteSpace(selectedModel))
            {
                logger.LogInformation("User selected model '{Model}' via elicitation.", selectedModel);
                return selectedModel;
            }
        }

        logger.LogWarning("Elicitation response did not contain a valid 'modelName'.");
        return null;
    }
}
