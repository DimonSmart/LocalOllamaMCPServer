# DimonSmart.LocalOllamaMCPServer

This is a Model Context Protocol (MCP) server that provides a tool to query a local Ollama instance. It is designed to help larger models (like Claude, GPT-5) test prompts against smaller local models (like Llama 3, Mistral, etc.) running on Ollama.

## Features

* **query_ollama**: A tool that sends a prompt to a specified local model and returns the response.

## Prerequisites

* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Ollama](https://ollama.com/) running locally (default: `http://localhost:11434`)

## Installation

### One-click install in VS Code (MCP)

If you use Visual Studio Code with GitHub Copilot Chat and MCP support enabled, you can add this server with a single click:

[![Install in VS Code](https://img.shields.io/badge/VS%20Code-Install%20MCP-007ACC?style=flat&logo=visualstudiocode&logoColor=white)](https://vscode.dev/redirect?url=vscode:mcp/install?%7B%22name%22%3A%20%22DimonSmart%20Local%20Ollama%20MCP%22%2C%20%22type%22%3A%20%22stdio%22%2C%20%22command%22%3A%20%22DimonSmart.LocalOllamaMCPServer%22%7D)

VS Code will show you the MCP configuration and let you choose whether to add it to your **user** or **workspace** settings.

If you prefer the raw URL, you can use:

```text
vscode:mcp/install?%7B%22name%22%3A%20%22DimonSmart%20Local%20Ollama%20MCP%22%2C%20%22type%22%3A%20%22stdio%22%2C%20%22command%22%3A%20%22DimonSmart.LocalOllamaMCPServer%22%7D
```

And the underlying JSON configuration is:

```json
{
  "name": "DimonSmart Local Ollama MCP",
  "type": "stdio",
  "command": "DimonSmart.LocalOllamaMCPServer"
}
```

You can also paste this JSON into VS Code via the **MCP: Add Server** command from the Command Palette.

### As a .NET Tool

```bash
dotnet tool install --global DimonSmart.LocalOllamaMCPServer
```

### From Source

1. Clone the repository.
2. Build the project:

   ```bash
   dotnet build
   ```

## Usage

### Running the Server

You can run the server directly:

```bash
dotnet run --project src/DimonSmart.LocalOllamaMCPServer/DimonSmart.LocalOllamaMCPServer.csproj
```

Or if installed as a tool:

```bash
DimonSmart.LocalOllamaMCPServer
```

The server communicates via Standard Input/Output (Stdio) using the MCP protocol (JSON-RPC 2.0). It is intended to be used by an MCP client (like Claude Desktop, Cursor, or a custom agent).

### Tool: `query_ollama`

**Arguments:**

* `model_name` (string): The name of the model to query (e.g., `llama3`, `mistral`).
* `prompt` (string): The prompt text.
* `options` (object, optional): Additional parameters for Ollama (e.g., `temperature`, `top_p`).
* `connection_name` (string, optional): The name of the Ollama server connection to use. If omitted, the default server is used.

**Example Request (JSON-RPC):**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "query_ollama",
    "arguments": {
      "model_name": "llama3",
      "prompt": "Why is the sky blue?",
      "connection_name": "remote-gpu",
      "options": {
        "temperature": 0.7
      }
    }
  },
  "id": 1
}
```

### Tool: `list_ollama_connections`

Lists all configured Ollama server connections. Passwords are masked.

**Example Request:**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "list_ollama_connections",
    "arguments": {}
  },
  "id": 2
}
```

## Configuration

The server supports multiple Ollama connections via `appsettings.json` or environment variables.

### appsettings.json

You can configure multiple servers in `appsettings.json`. The `DefaultServerName` determines which server is used if `connection_name` is not provided.

```json
{
  "Ollama": {
    "DefaultServerName": "local",
    "Servers": [
      {
        "Name": "local",
        "BaseUrl": "http://localhost:11434"
      },
      {
        "Name": "remote-gpu",
        "BaseUrl": "https://my-gpu-server.com:11434",
        "User": "admin",
        "Password": "secret-password",
        "IgnoreSsl": true
      }
    ]
  }
}
```

### Environment Variables

You can also configure servers using environment variables (standard .NET configuration naming):

* `Ollama__DefaultServerName=remote-gpu`
* `Ollama__Servers__0__Name=local`
* `Ollama__Servers__0__BaseUrl=http://localhost:11434`
* `Ollama__Servers__1__Name=remote-gpu`
* `Ollama__Servers__1__BaseUrl=https://my-gpu-server.com:11434`
* `Ollama__Servers__1__User=admin`
* `Ollama__Servers__1__Password=secret`
* `Ollama__Servers__1__IgnoreSsl=true`

## Testing

The project uses **EasyVCR** to record and replay HTTP interactions.

To run tests:

```bash
dotnet test
```

**Recording new cassettes:**

1. Ensure Ollama is running locally.
2. Ensure you have the model used in tests (e.g., `phi4:latest`) pulled: `ollama pull phi4:latest`.
3. Delete the existing cassette in `tests/DimonSmart.LocalOllamaMCPServer.Tests/cassettes/`.
4. Run the tests. A new cassette will be generated.

## CI/CD

The project includes a GitHub Action workflow that:

* Builds and tests on every push to `main`.
* Publishes a NuGet package and Docker image to GitHub Packages on every tag (e.g., `v1.0.0`).

## License

MIT
