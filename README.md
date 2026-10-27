# LocalOllamaMCPServer v3.0

A local, stateful MCP server for testing small Ollama models using a more capable AI agent. The MCP client and MCP server must run on the computer hosting Ollama. The AI agent may use a cloud model.

Tech stack: .NET 8, C# 10, official ModelContextProtocol 2.3.0, stdio transport. Calls are made exclusively to http://127.0.0.1:11434/v1/. There is no remote Ollama configuration, workspace/file access, native Ollama API, or execution of function tools by the server.

## Installation

Install and start Ollama, and pull at least one model. For example:

~~~sh
ollama pull qwen3:4b
ollama serve
~~~

Install/update the .NET global tool once version 3.0.0 has been published to NuGet:

~~~sh
dotnet tool install --global DimonSmart.LocalOllamaMCPServer --version 3.0.0
dotnet tool update --global DimonSmart.LocalOllamaMCPServer --version 3.0.0
~~~

To build from source:

~~~sh
dotnet run --project src/DimonSmart.LocalOllamaMCPServer
~~~

Example MCP client configuration:

~~~json
{
  "mcpServers": {
    "local-ollama": {
      "command": "DimonSmart.LocalOllamaMCPServer",
      "args": []
    }
  }
}
~~~

Standard output is reserved for MCP messages; logging goes to standard error.

## Available MCP tools

| Tool | Operation |
| --- | --- |
| list_models | Fetch /v1/models and return the raw OpenAI-compatible model listing |
| create_session | Create UUID session with model, system_prompt, tools, options |
| configure_session | Replace only specified fields; effective changes reset history |
| chat | Send new messages, retain history and return one chat completion |
| get_session | Read session settings and full history without calling Ollama |
| reset_session | Clear history only |
| close_session | Delete session |

All tool results are JSON text. Errors are surfaced to the agent.

## Example: test a function-calling prompt

Call create_session:

~~~json
{
  "model": "qwen3:4b",
  "system_prompt": "Use get_weather for weather queries.",
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_weather",
        "description": "Get the weather for a city",
        "parameters": {
          "type": "object",
          "properties": { "city": { "type": "string" } },
          "required": ["city"]
        }
      }
    }
  ],
  "options": { "temperature": 0.2, "seed": 42 }
}
~~~

Use the returned session_id in chat:

~~~json
{
  "session_id": "<UUID from create_session>",
  "messages": [{ "role": "user", "content": "Weather in Málaga?" }]
}
~~~

If message.tool_calls is returned, the calling AI agent simulates the tool output and calls chat again:

~~~json
{
  "session_id": "<UUID from create_session>",
  "messages": [
    {
      "role": "tool",
      "tool_call_id": "<id from the original function call>",
      "content": "{\"temperature_c\":22}"
    }
  ]
}
~~~

For multiple pending tool_calls, all matching unique tool_call_id results must appear in one chat request, before any subsequent user/assistant message. Unknown, repeated, conflicting and missing IDs are rejected without changing history.

The chat output contains session_id, the complete assistant message (including any extra provider fields), finish_reason, usage if supplied and raw_response containing the entire JSON completion. Function argument strings and identifiers are preserved exactly. OpenAI-style text and function-tool messages are supported; multimodal content is not.

Configure an existing session to retry a modified prompt. Unspecified fields are unchanged. Passing system_prompt as an empty string clears it; tools as [] removes tools; options as {} removes generation settings. A configuration change clears the previous dialogue. Independent sessions can be used for parallel experiments.

## Configuration

Packaged appsettings.json provides defaults and can be overridden with environment variables:

| Setting | Default | Environment |
| --- | --- | --- |
| Sessions:IdleTimeoutMinutes | 60 | Sessions__IdleTimeoutMinutes |
| Ollama:RequestTimeoutSeconds | 3600 | Ollama__RequestTimeoutSeconds |

Sessions are memory-only and expire after inactivity; a periodic cleanup pass evicts unused sessions. Operations in one session are sequential; sessions can run in parallel. Ollama errors, cancellation, malformed completions and timeouts leave message history unchanged.

Options are passed as **top-level Chat Completions JSON fields** (e.g. temperature, top_p, max_tokens, seed, reasoning_effort), not in native Ollama options. Actual availability depends on Ollama and the selected model. The request fields model, messages, stream, tools and tool_choice cannot be overridden. Unsupported options are reported as provider errors. No guarantee of function-calling support for every model is made.

## Test and release

~~~sh
dotnet restore
dotnet build
dotnet test
dotnet pack src/DimonSmart.LocalOllamaMCPServer -c Release
~~~

Normal tests use a fake HTTP endpoint. The optional real-Ollama test requires OLLAMA_INTEGRATION_MODEL to specify a locally installed model. GitHub Actions keeps the existing NuGet release-on-version-tag workflow.
