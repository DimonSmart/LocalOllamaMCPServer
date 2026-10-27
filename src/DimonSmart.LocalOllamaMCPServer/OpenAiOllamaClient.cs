using System.Net.Http.Json;
using System.Text.Json;

namespace DimonSmart.LocalOllamaMCPServer;

internal sealed record CompletionResult(
    JsonElement Message,
    string? FinishReason,
    JsonElement? Usage,
    JsonElement RawResponse);

internal sealed class OpenAiOllamaClient
{
    private readonly HttpClient _client;

    public OpenAiOllamaClient(HttpClient client) => _client = client;

    internal async Task<JsonElement> ListModelsAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(new Uri("models", UriKind.Relative), cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<CompletionResult> CompleteAsync(TestSession session, IReadOnlyList<JsonElement> newMessages, CancellationToken cancellationToken)
    {
        var messages = new List<JsonElement>();
        if (!string.IsNullOrEmpty(session.SystemPrompt))
            messages.Add(JsonSerializer.SerializeToElement(new { role = "system", content = session.SystemPrompt }));

        messages.AddRange(session.History);
        messages.AddRange(newMessages);

        var request = new Dictionary<string, object?>
        {
            ["model"] = session.Model,
            ["messages"] = messages,
            ["stream"] = false
        };

        if (session.Tools is { ValueKind: JsonValueKind.Array } tools && tools.GetArrayLength() > 0)
            request["tools"] = tools;

        if (session.Options is { ValueKind: JsonValueKind.Object } options)
            foreach (var option in options.EnumerateObject())
                request.Add(option.Name, option.Value);

        using var response = await _client.PostAsJsonAsync("chat/completions", request, cancellationToken).ConfigureAwait(false);
        var raw = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);

        if (!raw.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 || choices[0].ValueKind != JsonValueKind.Object ||
            !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            throw new JsonException("Ollama returned no valid Chat Completions choice/message.");

        if (!message.TryGetProperty("role", out var role) || role.GetString() != "assistant")
            throw new JsonException("Ollama choice.message must have role 'assistant'.");

        OpenAiMessages.Validate(message);

        string? finishReason = null;
        if (choices[0].TryGetProperty("finish_reason", out var finish) && finish.ValueKind != JsonValueKind.Null)
            finishReason = finish.GetString();

        JsonElement? usage = null;
        if (raw.TryGetProperty("usage", out var usageValue))
            usage = usageValue.Clone();

        return new CompletionResult(message.Clone(), finishReason, usage, raw);
    }

    private static async Task<JsonElement> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama OpenAI API returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}", null, response.StatusCode);

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Ollama response must be a JSON object.");

        return json.RootElement.Clone();
    }
}
