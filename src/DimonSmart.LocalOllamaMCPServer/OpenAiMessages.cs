using System.Text.Json;

namespace DimonSmart.LocalOllamaMCPServer;

internal static class OpenAiMessages
{
    internal static List<JsonElement> ParseAndValidate(JsonElement messages, IReadOnlyList<JsonElement> history)
    {
        if (messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
            throw new ArgumentException("messages must be a non-empty JSON array.");

        var pending = history.Count > 0 ? Calls(history[history.Count - 1]) : new HashSet<string>(StringComparer.Ordinal);
        var result = new List<JsonElement>();

        foreach (var message in messages.EnumerateArray())
        {
            Validate(message);
            var role = message.GetProperty("role").GetString();
            if (pending.Count > 0)
            {
                if (role != "tool" || !pending.Remove(message.GetProperty("tool_call_id").GetString()!))
                    throw new ArgumentException("All outstanding tool_calls must receive exactly one matching tool message before continuing.");
            }
            else if (role == "tool")
            {
                throw new ArgumentException("Unexpected tool result: no matching outstanding tool_calls.");
            }
            else if (role == "assistant")
            {
                pending = Calls(message);
            }

            result.Add(message.Clone());
        }

        if (pending.Count != 0)
            throw new ArgumentException("Incomplete tool results: all outstanding tool_call_id values must be supplied in the same chat request.");

        return result;
    }

    internal static void Validate(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("role", out var roleValue) || roleValue.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Each message needs a string role.");

        var role = roleValue.GetString();
        if (role is not ("user" or "assistant" or "tool"))
            throw new ArgumentException("Supported roles: user, assistant, tool.");

        if (message.TryGetProperty("content", out var content))
        {
            if (content.ValueKind != JsonValueKind.String && content.ValueKind != JsonValueKind.Null)
                throw new ArgumentException("Only text content or null is supported.");
        }
        else if (role != "assistant")
        {
            throw new ArgumentException("user/tool messages require content.");
        }

        if (role == "tool")
        {
            if (!message.TryGetProperty("tool_call_id", out var id) ||
                id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) ||
                content.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Tool messages need tool_call_id and text content.");
        }
        else if (message.TryGetProperty("tool_call_id", out _))
            throw new ArgumentException("Only tool messages may contain tool_call_id.");

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind != JsonValueKind.Null)
        {
            if (role != "assistant" || toolCalls.ValueKind != JsonValueKind.Array ||
                toolCalls.GetArrayLength() == 0)
                throw new ArgumentException("tool_calls must be a non-empty array on an assistant message.");
            Calls(message);
        }
        else if (role == "assistant" && !message.TryGetProperty("content", out _))
            throw new ArgumentException("Assistant message needs content or tool_calls.");
    }

    private static HashSet<string> Calls(JsonElement message)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var call in calls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object ||
                !call.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()) ||
                !call.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "function" ||
                !call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object ||
                !function.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()) ||
                !function.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.String ||
                !result.Add(id.GetString()!))
                throw new ArgumentException("Invalid/duplicate function tool call (id, type, function.name and string arguments are required).");
        }
        return result;
    }
}
