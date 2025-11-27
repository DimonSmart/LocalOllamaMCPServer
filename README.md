# DimonSmart.LocalOllamaMCPServer

This is a Model Context Protocol (MCP) server that provides a tool to query a local Ollama instance. It is designed to help larger models (like Claude, GPT-5) test prompts against smaller local models (like Llama 3, Mistral, etc.) running on Ollama.

## Features

- **query_ollama**: A tool that sends a prompt to a specified local model and returns the response.

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Ollama](https://ollama.com/) running locally (default: `http://localhost:11434`)

## Installation

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

- `model_name` (string): The name of the model to query (e.g., `llama3`, `mistral`).
- `prompt` (string): The prompt text.
- `options` (object, optional): Additional parameters for Ollama (e.g., `temperature`, `top_p`).

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
      "options": {
        "temperature": 0.7
      }
    }
  },
  "id": 1
}
```

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
- Builds and tests on every push to `main`.
- Publishes a NuGet package and Docker image to GitHub Packages on every tag (e.g., `v1.0.0`).

## License

MIT
