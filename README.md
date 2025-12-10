# DimonSmart.LocalOllamaMCPServer

[![.NET](https://github.com/DimonSmart/LocalOllamaMCPServer/actions/workflows/dotnet.yml/badge.svg)](https://github.com/DimonSmart/LocalOllamaMCPServer/actions/workflows/dotnet.yml)
[![NuGet](https://img.shields.io/nuget/v/DimonSmart.LocalOllamaMCPServer.svg)](https://www.nuget.org/packages/DimonSmart.LocalOllamaMCPServer/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

A Model Context Protocol (MCP) server that provides tools to query local Ollama instances. Built with the official [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) SDK from Anthropic + Microsoft, it enables larger models (like Claude, GPT-4) to test prompts against smaller local models (like Llama 3, Mistral, etc.) running on Ollama.

## Features

* **query_ollama** - Send prompts to local Ollama models and get responses
* **query_ollama_with_file** - Test a prompt template on one or more files from explicit roots
* **list_ollama_connections** - List all configured Ollama server connections
* **list_ollama_models** - Inspect which models are available on each Ollama server
* Interactive elicitation fallback when a requested model is missing (MCP 0.4.1 preview)
* Full MCP specification compliance with proper JSON-RPC 2.0 framing
* Support for multiple Ollama server connections with authentication
* Automatic tool schema generation from method signatures
* SSL certificate validation control for self-signed certificates
* 1-hour default timeout for long-running model inference requests

## Prerequisites

* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [Ollama](https://ollama.com/) running locally (default: `http://localhost:11434`)

## Installation

### As a .NET Tool

Install the tool globally:

```bash
dotnet tool install --global DimonSmart.LocalOllamaMCPServer
```

To update to the latest version:

```bash
dotnet tool update --global DimonSmart.LocalOllamaMCPServer
```

### One-click install in VS Code (MCP)

After installing the .NET tool, you can add this server to VS Code or VS Code Insiders with a single click:

[![Install in VS Code](https://img.shields.io/badge/VS%20Code-Install%20MCP-007ACC?style=flat&logo=visualstudiocode&logoColor=white)](https://vscode.dev/redirect?url=vscode:mcp/install?%7B%22name%22%3A%20%22DimonSmart%20Local%20Ollama%20MCP%22%2C%20%22type%22%3A%20%22stdio%22%2C%20%22command%22%3A%20%22DimonSmart.LocalOllamaMCPServer%22%7D)
[![Install in VS Code Insiders](https://img.shields.io/badge/VS%20Code%20Insiders-Install%20MCP-24BF60?style=flat&logo=visualstudiocode&logoColor=white)](https://vscode.dev/redirect?url=vscode-insiders:mcp/install?%7B%22name%22%3A%20%22DimonSmart%20Local%20Ollama%20MCP%22%2C%20%22type%22%3A%20%22stdio%22%2C%20%22command%22%3A%20%22DimonSmart.LocalOllamaMCPServer%22%7D)

VS Code will show you the MCP configuration and let you choose whether to add it to your **user** or **workspace** settings.

If you prefer the raw URLs, you can use:

**VS Code:**
```text
vscode:mcp/install?%7B%22name%22%3A%20%22DimonSmart%20Local%20Ollama%20MCP%22%2C%20%22type%22%3A%20%22stdio%22%2C%20%22command%22%3A%20%22DimonSmart.LocalOllamaMCPServer%22%7D
```

**VS Code Insiders:**
```text
vscode-insiders:mcp/install?%7B%22name%22%3A%20%22DimonSmart%20Local%20Ollama%20MCP%22%2C%20%22type%22%3A%20%22stdio%22%2C%20%22command%22%3A%20%22DimonSmart.LocalOllamaMCPServer%22%7D
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

To check the version and configuration file location:

```bash
DimonSmart.LocalOllamaMCPServer --version
```

The server communicates via Standard Input/Output (stdio) using the MCP protocol. It is designed to be used by MCP clients such as:

- [Claude Desktop](https://claude.ai/download)
- [GitHub Copilot in VS Code](https://github.com/features/copilot)
- [Cursor](https://cursor.sh/)
- Custom agents and AI applications

### Available Tools

#### `query_ollama`

Send a prompt to a local Ollama model and receive the response.

**Parameters:**

* `model_name` (string, required) - Name of the Ollama model (e.g., `llama3`, `mistral`, `phi4`)
* `prompt` (string, required) - The prompt text to send to the model
* `options` (object, optional) - Model parameters such as `temperature`, `top_p`, etc.
* `connection_name` (string, optional) - Name of the Ollama server connection. Uses default if omitted.

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

**Elicitation example when the model is missing**

If you call `query_ollama` with a model that does not exist on the selected connection, the server now issues an MCP `elicitation/create` request asking the user to choose one of the available models. This keeps the conversation inside the same tool call instead of failing immediately.

1. Client sends an invalid tool call:

    ```json
    {
      "jsonrpc": "2.0",
      "method": "tools/call",
      "params": {
        "name": "query_ollama",
        "arguments": {
          "model_name": "llama3-invalid",
          "prompt": "hello",
          "connection_name": "local"
        }
      },
      "id": 42
    }
    ```

2. The server looks up the real models (for example `llama3` and `mistral`) and prompts the client:

    ```json
    {
      "jsonrpc": "2.0",
      "method": "elicitation/create",
      "params": {
        "message": "The model 'llama3-invalid' was not found on 'local'. Please choose another model to continue.",
        "requested_schema": {
          "required": ["modelName"],
          "properties": {
            "modelName": {
              "title": "Select an Ollama model",
              "description": "Pick a model available on 'local'.",
              "enum": ["llama3", "mistral"],
              "default": "llama3"
            }
          }
        }
      },
      "id": "elicitation-42"
    }
    ```

3. After the user selects a model, the client replies:

    ```json
    {
      "jsonrpc": "2.0",
      "method": "elicitation/response",
      "params": {
        "request_id": "elicitation-42",
        "accepted": true,
        "content": {
          "modelName": "mistral"
        }
      }
    }
    ```

4. The server re-runs the original tool call with the selected model (`mistral`) and returns the final `tools/call` result. If the user cancels or the client does not support elicitation, the tool call exits with a descriptive error message.

#### `query_ollama_with_file`

Test a prompt template against one or more files that live under the workspace roots advertised by the MCP host (via the `roots/list` request). The server requests those roots at runtime and refuses to read anything outside of them.

**Placeholders in the prompt template:**

* `{{data}}` (default) - replaced with the file contents unless `send_data_as_user_message=true`
* `{{file_name}}` - replaced with the file name
* `{{file_path}}` - replaced with the relative path inside the root

**Parameters:**

* `model_name` (string, required) - Target model
* `prompt_template` (string, required) - Template containing the placeholders above
* `file_path` (string, optional) - Path to a single file (absolute or relative to a root). Required when `run_for_all=false`
* `root_name` (string, optional) - Limit search to a specific root (helps disambiguate relative paths)
* `run_for_all` (bool, optional) - If `true`, process every matching file instead of a single one
* `file_pattern` (string, optional) - Pattern for `run_for_all` (default: `*.md`)
* `placeholder` (string, optional) - Custom placeholder to replace with file content (default: `{{data}}`)
* `send_data_as_user_message` (bool, optional) - Append file content as a separate user-style message instead of inline replacement
* `max_files` (int, optional) - Limit how many files are processed when `run_for_all=true` (0 = no limit)
* `options` (object, optional) - Model options
* `connection_name` (string, optional) - Ollama server connection (default if omitted)

**Example Request (process a single markdown file):**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "query_ollama_with_file",
    "arguments": {
      "model_name": "llama3",
      "prompt_template": "Summarize {{file_name}} in 3 bullet points. Content: {{data}}",
      "file_path": "docs/example.md"
    }
  },
  "id": 99
}
```

**Example Request (process all markdown files under a root):**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "query_ollama_with_file",
    "arguments": {
      "model_name": "mistral",
      "prompt_template": "Classify the tone of {{file_path}}. Respond with 'positive', 'neutral', or 'negative'.",
      "run_for_all": true,
      "file_pattern": "*.md",
      "max_files": 5
    }
  },
  "id": 100
}
```

If the connected host does not support `roots/list` or returns an empty list, the tool fails with an error instead of touching the filesystem.

#### `list_ollama_connections`

List all configured Ollama server connections. Passwords are masked for security.

**Parameters:** None

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

#### `list_ollama_models`

List all models that the selected Ollama server reports through `/api/tags`. This is especially handy when deciding which model to select during elicitation.

**Parameters:**

* `connection_name` (string, optional) - Name of the Ollama server connection. Uses default if omitted.

**Example Request:**

```json
{
  "jsonrpc": "2.0",
  "method": "tools/call",
  "params": {
    "name": "list_ollama_models",
    "arguments": {
      "connection_name": "remote-gpu"
    }
  },
  "id": 3
}
```

## Configuration

The server supports multiple Ollama instances through configuration. You can configure connections using either `appsettings.json` or environment variables. If no configuration is provided, the server will automatically use default settings with a local Ollama server at `http://localhost:11434`.

### Configuration via appsettings.json

Create or edit `appsettings.json` in the tool's installation directory. The `DefaultServerName` specifies which server to use when `connection_name` is not provided in tool calls.

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

**Server Configuration Properties:**

- `Name` - Unique identifier for the server connection
- `BaseUrl` - Ollama server URL
- `User` (optional) - Username for Basic authentication
- `Password` (optional) - Password for Basic authentication
- `IgnoreSsl` (optional) - Set to `true` to accept self-signed SSL certificates

### Workspace roots for prompt testing

The MCP host (e.g., the VS Code MCP extension) is responsible for advertising filesystem boundaries via `roots/list`. This server requests the list of roots at runtime and restricts all file operations to that set. If the host does not expose any roots, tools such as `query_ollama_with_file` return an error instead of reading arbitrary paths.

Relative `file_path` values are resolved against the reported roots; provide `root_name` to disambiguate when you have multiple workspaces mounted.

### Configuration via Environment Variables

You can also configure servers using environment variables following standard .NET configuration naming conventions:

```bash
Ollama__DefaultServerName=remote-gpu
Ollama__Servers__0__Name=local
Ollama__Servers__0__BaseUrl=http://localhost:11434
Ollama__Servers__1__Name=remote-gpu
Ollama__Servers__1__BaseUrl=https://my-gpu-server.com:11434
Ollama__Servers__1__User=admin
Ollama__Servers__1__Password=secret
Ollama__Servers__1__IgnoreSsl=true
```

## Development

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/DimonSmart/LocalOllamaMCPServer.git
   cd LocalOllamaMCPServer
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Run locally:
   ```bash
   dotnet run --project src/DimonSmart.LocalOllamaMCPServer/DimonSmart.LocalOllamaMCPServer.csproj
   ```

### Running Tests

The project uses **EasyVCR** to record and replay HTTP interactions for reliable testing.

Run all tests:

```bash
dotnet test
```

**Recording new cassettes:**

1. Ensure Ollama is running locally
2. Pull the test model: `ollama pull phi4:latest`
3. Delete the existing cassette in `tests/DimonSmart.LocalOllamaMCPServer.Tests/cassettes/`
4. Run the tests to generate a new cassette

### CI/CD

The project includes GitHub Actions workflows that:

* Build and test on every push to `main`
* Publish NuGet package to NuGet.org on version tags (e.g., `v2.0.0`)
* Create GitHub releases with artifacts

## Technology Stack

- **.NET 8.0** - Target framework
- **[ModelContextProtocol SDK](https://github.com/modelcontextprotocol/csharp-sdk)** - Official MCP implementation
- **Microsoft.Extensions.Hosting** - Application lifetime management
- **Microsoft.Extensions.Http** - HTTP client factory
- **EasyVCR** - HTTP recording for tests

## Related Projects

- [Model Context Protocol](https://modelcontextprotocol.io/) - Official MCP documentation
- [Ollama](https://ollama.com/) - Run large language models locally
- [MCP Servers Registry](https://github.com/modelcontextprotocol/servers) - Collection of MCP servers

## License

MIT
