using System.Text.Json.Serialization;

namespace DimonSmart.LocalOllamaMCPServer
{
    internal sealed class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("method")]
        public required string Method { get; set; }

        [JsonPropertyName("params")]
        public object? Params { get; set; }

        [JsonPropertyName("id")]
        public object? Id { get; set; }
    }

    internal sealed class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("result")]
        public object? Result { get; set; }

        [JsonPropertyName("error")]
        public JsonRpcError? Error { get; set; }

        [JsonPropertyName("id")]
        public object? Id { get; set; }
    }

    internal sealed class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public required string Message { get; set; }
    }

    internal sealed class InitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2024-11-05";

        [JsonPropertyName("capabilities")]
        public required ServerCapabilities Capabilities { get; set; }

        [JsonPropertyName("serverInfo")]
        public required ServerInfo ServerInfo { get; set; }
    }

    internal sealed class ServerCapabilities
    {
        [JsonPropertyName("tools")]
        public required ToolsCapability Tools { get; set; }
    }

    internal sealed class ToolsCapability
    {
        [JsonPropertyName("listChanged")]
        public bool ListChanged { get; set; }
    }

    internal sealed class ServerInfo
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("version")]
        public required string Version { get; set; }
    }

    internal sealed class ToolListResult
    {
        [JsonPropertyName("tools")]
        public required IReadOnlyList<Tool> Tools { get; init; }
    }

    internal sealed class Tool
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("description")]
        public required string Description { get; set; }

        [JsonPropertyName("inputSchema")]
        public required object InputSchema { get; set; }
    }

    internal sealed class CallToolParams
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("arguments")]
        public Dictionary<string, object>? Arguments { get; init; }
    }

    internal sealed class CallToolResult
    {
        [JsonPropertyName("content")]
        public required IReadOnlyList<ToolContent> Content { get; init; }

        [JsonPropertyName("isError")]
        public bool IsError { get; set; }
    }

    internal sealed class ToolContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        public required string Text { get; set; }
    }
}
