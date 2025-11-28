using System.Text;
using System.Text.Json;
using DimonSmart.LocalOllamaMCPServer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DimonSmart.LocalOllamaMCPServer;


internal sealed class OllamaService : IOllamaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppConfig _config;
    private readonly ILogger<OllamaService> _logger;

    public OllamaService(IHttpClientFactory httpClientFactory, IOptions<AppConfig> config, ILogger<OllamaService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _config = config?.Value ?? new AppConfig();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Ensure we have at least a default server
        if (_config.Servers == null || _config.Servers.Count == 0)
        {
            _config.Servers = new List<OllamaServerConfig>
            {
                new OllamaServerConfig
                {
                    Name = "local",
                    BaseUrl = new Uri("http://localhost:11434")
                }
            };
            _config.DefaultServerName = "local";
        }

        _logger.LogInformation("OllamaService initialized with {Count} servers", _config.Servers.Count);
    }

    public IEnumerable<OllamaServerConfig> GetConfigurations()
    {
        return _config.Servers.Select(s => new OllamaServerConfig
        {
            Name = s.Name ?? string.Empty,
            BaseUrl = s.BaseUrl,
            User = s.User,
            Password = string.IsNullOrEmpty(s.Password) ? null : "******",
            IgnoreSsl = s.IgnoreSsl
        });
    }

    public async Task<string> GenerateAsync(string model, string prompt, Dictionary<string, object>? options, string? connectionName = null)
    {
        var serverName = connectionName;
        if (string.IsNullOrWhiteSpace(serverName))
        {
            serverName = _config.DefaultServerName;
        }

        if (string.IsNullOrWhiteSpace(serverName))
        {
            var first = _config.Servers.FirstOrDefault();
            if (first != null)
            {
                serverName = first.Name;
            }
            else
            {
                throw new InvalidOperationException("No Ollama servers configured.");
            }
        }

        var serverConfig = _config.Servers.FirstOrDefault(s => s.Name.Equals(serverName, StringComparison.OrdinalIgnoreCase));
        if (serverConfig == null)
        {
            throw new ArgumentException($"Ollama server configuration '{serverName}' not found.");
        }

        var httpClient = _httpClientFactory.CreateClient(serverName!);

        var requestBody = new
        {
            model = model,
            prompt = prompt,
            stream = false,
            options = options
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        // HttpClient BaseAddress is already set by the factory configuration
        var response = await httpClient.PostAsync(new Uri("api/generate", UriKind.Relative), content).ConfigureAwait(false);
        var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Ollama API error: {response.StatusCode} - {responseString}");
        }

        using var doc = JsonDocument.Parse(responseString);
        if (doc.RootElement.TryGetProperty("response", out var responseText))
        {
            return responseText.GetString() ?? "";
        }

        return responseString;
    }
}
