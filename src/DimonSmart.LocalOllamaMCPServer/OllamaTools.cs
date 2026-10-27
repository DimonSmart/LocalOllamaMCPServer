using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalOllamaMCPServer;

[McpServerToolType]
internal static class OllamaTools
{
    [McpServerTool(Name = "list_models")]
    [Description("List models available from the local Ollama OpenAI-compatible /v1/models endpoint.")]
    public static async Task<string> ListModels(OpenAiOllamaClient client, CancellationToken cancellationToken = default) =>
        (await client.ListModelsAsync(cancellationToken).ConfigureAwait(false)).GetRawText();

    [McpServerTool(Name = "create_session")]
    [Description("Create an independent prompt-testing session. tools uses OpenAI Chat Completions function tool definitions; options are top-level generation parameters.")]
    public static string CreateSession(
        [Description("Local Ollama model ID.")] string model,
        TestSessionManager sessions,
        [Description("Optional system prompt (separate from message history).")] string? system_prompt = null,
        [Description("Optional array of OpenAI function tools.")] JsonElement? tools = null,
        [Description("Optional JSON object with top-level Chat Completions generation parameters.")] JsonElement? options = null) =>
        JsonSerializer.Serialize(sessions.Create(model, system_prompt, tools, options).Snapshot());

    [McpServerTool(Name = "configure_session")]
    [Description("Change a session's model, system prompt, tools or generation options. Omitted values remain unchanged; any effective change clears history. Use an empty string to clear system_prompt, [] for tools, {} for options.")]
    public static async Task<string> ConfigureSession(
        [Description("Session UUID.")] string session_id,
        TestSessionManager sessions,
        [Description("Optional new model ID.")] string? model = null,
        [Description("Optional new system prompt; empty string clears it.")] string? system_prompt = null,
        [Description("Optional full replacement tool definitions; [] removes all tools.")] JsonElement? tools = null,
        [Description("Optional full replacement generation options object.")] JsonElement? options = null,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await sessions.ConfigureAsync(session_id, model, system_prompt, tools, options, cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "chat")]
    [Description("Send new user/assistant/tool messages to a stateful Ollama session. A pending function-call batch must be answered with all matching tool_call_id results in this request. Returns the unmodified assistant message and raw completion.")]
    public static Task<string> Chat(
        [Description("Session UUID.")] string session_id,
        [Description("Non-empty array of new OpenAI chat messages (roles: user, assistant, tool).")] JsonElement messages,
        TestSessionManager sessions,
        OpenAiOllamaClient client,
        CancellationToken cancellationToken = default) =>
        sessions.WithSessionAsync(session_id, async session =>
        {
            var appended = OpenAiMessages.ParseAndValidate(messages, session.History);
            var response = await client.CompleteAsync(session, appended, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            session.History.AddRange(appended);
            session.History.Add(response.Message);
            var result = new Dictionary<string, object?>
            {
                ["session_id"] = session.Id.ToString("D"),
                ["message"] = response.Message,
                ["finish_reason"] = response.FinishReason,
                ["raw_response"] = response.RawResponse
            };
            if (response.Usage.HasValue)
                result["usage"] = response.Usage.Value;
            return JsonSerializer.Serialize(result);
        }, cancellationToken);

    [McpServerTool(Name = "get_session")]
    [Description("Read the current session configuration and entire text/function-call history without contacting Ollama.")]
    public static async Task<string> GetSession(
        [Description("Session UUID.")] string session_id,
        TestSessionManager sessions,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await sessions.WithSessionAsync(session_id, session =>
            Task.FromResult(session.Snapshot()), cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "reset_session")]
    [Description("Erase history but retain the session's model, system prompt, tools and options.")]
    public static async Task<string> ResetSession(
        [Description("Session UUID.")] string session_id,
        TestSessionManager sessions,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await sessions.ResetAsync(session_id, cancellationToken).ConfigureAwait(false));

    [McpServerTool(Name = "close_session")]
    [Description("Delete a local test session and its history.")]
    public static async Task<string> CloseSession(
        [Description("Session UUID.")] string session_id,
        TestSessionManager sessions,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await sessions.CloseAsync(session_id, cancellationToken).ConfigureAwait(false));
}
