using System.Text;
using System.Text.Json;
using DimonSmart.LocalOllamaMCPServer.Configuration;
using Microsoft.Extensions.Options;

namespace DimonSmart.LocalOllamaMCPServer
{
    internal interface IOllamaService
    {
        Task<string> GenerateAsync(string model, string prompt, Dictionary<string, object>? options, string? connectionName = null);
        IEnumerable<OllamaServerConfig> GetConfigurations();
    }

    internal sealed class OllamaService : IOllamaService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppConfig _config;

        public OllamaService(IHttpClientFactory httpClientFactory, IOptions<AppConfig> config)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        }

        public IEnumerable<OllamaServerConfig> GetConfigurations()
        {
            return _config.Servers.Select(s => new OllamaServerConfig
            {
                Name = s.Name,
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
}
