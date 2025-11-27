using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DimonSmart.LocalOllamaMCPServer
{
    internal interface IOllamaService
    {
        Task<string> GenerateAsync(string model, string prompt, Dictionary<string, object>? options);
    }

    internal sealed class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _baseUri;

        public OllamaService(HttpClient httpClient, string baseUrl = "http://localhost:11434")
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

            _httpClient = httpClient;
            _baseUri = new Uri(baseUrl.TrimEnd('/'));
        }

        public async Task<string> GenerateAsync(string model, string prompt, Dictionary<string, object>? options)
        {
            var requestBody = new
            {
                model = model,
                prompt = prompt,
                stream = false,
                options = options
            };

            var json = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var requestUri = new Uri(_baseUri, "/api/generate");
            var response = await _httpClient.PostAsync(requestUri, content).ConfigureAwait(false);
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

            return responseString; // Fallback to full JSON if "response" field is missing
        }
    }
}
