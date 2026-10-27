# Development guide

LocalOllamaMCPServer v3.0 targets .NET 8 and C# 10. MCP uses the official ModelContextProtocol 2.3.0 and stdio; only stdout is MCP protocol output.

Core invariants:
- Use only the local OpenAI-compatible Ollama API at http://127.0.0.1:11434/v1/.
- Never execute model-defined function tools: the calling MCP AI agent simulates their outputs.
- Do not add MCP Roots, file processing, multi-server support, remote authentication, SSL bypass, native /api/chat or model-selection elicitation.
- Store system prompt separately from dialogue history and prepend it to every request.
- Store full assistant JSON, preserving tool_calls and string arguments. Do not create synthetic usage statistics.
- Require complete and unique matching tool_call_id results before resuming model inference.
- Serialize operations per session, not globally; errors and cancellation do not append history.
- Configuration changes reset history; cleanup idle sessions.
- Test against a mock HttpMessageHandler; real Ollama checks are optional.
- Use only C# 10 syntax (no collection expressions or raw string literals).
- Preserve .NET Tool build, test and NuGet publish workflows.
