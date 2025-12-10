using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace DimonSmart.LocalOllamaMCPServer;

[McpServerToolType]
internal static class OllamaTools
{
    private static readonly JsonSerializerOptions IndentedOptions = new() { WriteIndented = true };
    private const string DefaultDataPlaceholder = "{{data}}";
    internal static readonly char[] anyOf = new[] { '*', '?' };

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
        McpServer? server = null,
        RootsState? rootsState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ollamaService);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("QueryOllama called: model={Model}, connection={Connection}", model_name, connection_name ?? "default");

        List<string>? availableModels = null;
        try
        {
            var fetchedModels = await ollamaService.GetModelsAsync(connection_name, cancellationToken).ConfigureAwait(false);
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

        var optionsDict = ConvertOptions(options);

        try
        {
            var result = await ollamaService.GenerateAsync(
                model_name,
                prompt,
                optionsDict,
                connection_name,
                cancellationToken).ConfigureAwait(false);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing QueryOllama");
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "query_ollama_with_file")]
    [Description("Run a prompt template against one or more files inside configured roots and return model responses.")]
    public static async Task<string> QueryOllamaWithFile(
        [Description("The name of the model to query (e.g., 'llama3', 'mistral').")]
        string model_name,
        [Description("Prompt template that may include placeholders like '{{data}}', '{{file_name}}', or '{{file_path}}'.")]
        string prompt_template,
        [Description("File mask to apply. Use patterns like '*.*' for all or a specific name for a single file.")]
        string? file_path = null,
        [Description("If true, appends the file content as a separate user-style message instead of inline replacement.")]
        bool send_data_as_user_message = false,
        [Description("Limit the number of files processed when using masks (0 = no limit).")]
        int max_files = 0,
        [Description("Optional name of the Ollama server connection to use. If omitted, the default server is used.")]
        string? connection_name = null,
        IOllamaService? ollamaService = null,
        ILogger<IOllamaService>? logger = null,
        McpServer? server = null,
        RootsState? rootsState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ollamaService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt_template);
        ArgumentException.ThrowIfNullOrWhiteSpace(model_name);

        var (fileSystem, rootError) = await CreateWorkspaceFileSystemAsync(rootsState, server, logger, cancellationToken).ConfigureAwait(false);
        if (fileSystem is null)
        {
            return $"Error: {rootError}";
        }

        if (max_files < 0)
        {
            return "Error: max_files must be zero or positive.";
        }

        var mask = string.IsNullOrWhiteSpace(file_path) ? "*.*" : file_path.Trim();
        var filesToProcess = new List<WorkspaceFileSystem.WorkspaceFile>();
        var hasWildcards = HasWildcards(mask);

        if (hasWildcards)
        {
            var limit = max_files > 0 ? max_files : (int?)null;
            filesToProcess.AddRange(fileSystem.EnumerateFiles(mask, null, limit));

            if (filesToProcess.Count == 0)
            {
                return $"Error: No files matching '{mask}' were found inside the workspace roots.";
            }
        }
        else
        {
            if (!fileSystem.TryResolveFile(mask, null, out var resolved, out var resolveError))
            {
                return $"Error: {resolveError}";
            }

            filesToProcess.Add(resolved!);
        }

        logger.LogInformation(
            "QueryOllamaWithFile called: model={Model}, connection={Connection}, mask={Mask}, files={Count}, roots={Roots}",
            model_name,
            connection_name ?? "default",
            mask,
            filesToProcess.Count,
            string.Join(", ", fileSystem.Roots.Select(r => r.Name)));

        var results = new List<object>();
        var placeholderToken = DefaultDataPlaceholder;

        foreach (var file in filesToProcess)
        {
            string fileContent;
            try
            {
                fileContent = await fileSystem.ReadFileAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read file {File}", file.AbsolutePath);
                results.Add(new
                {
                    file = file.RelativePath,
                    root = file.Root.Name,
                    status = "read_error",
                    error = ex.Message
                });
                continue;
            }

            string finalPrompt;
            if (send_data_as_user_message)
            {
                finalPrompt = $"{prompt_template}\n\n[user data from {file.RelativePath}]\n{fileContent}";
            }
            else if (prompt_template.Contains(placeholderToken, StringComparison.Ordinal))
            {
                finalPrompt = prompt_template.Replace(placeholderToken, fileContent, StringComparison.Ordinal);
            }
            else
            {
                finalPrompt = $"{prompt_template}\n\n{placeholderToken}:\n{fileContent}";
            }

            string response;
            try
            {
                response = await ollamaService.GenerateAsync(
                    model_name,
                    finalPrompt,
                    null,
                    connection_name,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error executing QueryOllamaWithFile for {File}", file.RelativePath);
                response = $"Error: {ex.Message}";
            }

            results.Add(new
            {
                file = file.RelativePath,
                root = file.Root.Name,
                status = response.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ? "error" : "ok",
                prompt_preview = BuildPreview(finalPrompt),
                response
            });
        }

        var payload = new
        {
            model = model_name,
            connection = connection_name ?? "default",
            placeholder = placeholderToken,
            mask,
            files = results.Count,
            roots = fileSystem.Roots.Select(r => new { r.Name, r.Path }),
            results
        };

        return JsonSerializer.Serialize(payload, IndentedOptions);
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

        logger.LogInformation("Returning {Count} configurations", configs.Count);

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

    private static async Task<(WorkspaceFileSystem? FileSystem, string? Error)> CreateWorkspaceFileSystemAsync(
        RootsState? rootsState,
        McpServer? server,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (rootsState is not null)
        {
            return await rootsState.TryCreateWorkspaceFileSystemAsync(logger, cancellationToken).ConfigureAwait(false);
        }

        if (server is null)
        {
            return (null, "MCP server instance is not available; ensure the host supports roots/list requests.");
        }

        ListRootsResult rootsResult;
        try
        {
            rootsResult = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "The connected MCP host does not advertise roots support.");
            return (null, "Connected MCP host does not advertise roots/list support.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while requesting roots from the MCP host.");
            return (null, $"Failed to request roots from the MCP host: {ex.Message}");
        }

        if (!WorkspaceFileSystem.TryCreate(rootsResult.Roots, logger, out var fileSystem, out var conversionError))
        {
            return (null, conversionError ?? "The MCP host did not return any usable workspace roots.");
        }

        return (fileSystem, null);
    }


    private static async Task<string?> PromptForModelSelectionAsync(
        string missingModel,
        string connectionLabel,
        List<string> availableModels,
        McpServer server,
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
            Required = ["modelName"]
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
            elicitationResult = await server.ElicitAsync(request, cancellationToken).ConfigureAwait(false);
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

    private static Dictionary<string, object>? ConvertOptions(JsonElement? options)
    {
        if (options.HasValue && options.Value.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(options.Value.GetRawText());
        }

        return null;
    }

    private static bool HasWildcards(string value)
    {
        return value.IndexOfAny(anyOf) >= 0;
    }

    private static string BuildPreview(string content, int maxLength = 400)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (content.Length <= maxLength)
        {
            return content;
        }

        return string.Concat(content.AsSpan(0, maxLength), "...");
    }
}
