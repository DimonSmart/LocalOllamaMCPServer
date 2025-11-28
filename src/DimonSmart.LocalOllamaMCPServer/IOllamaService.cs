using DimonSmart.LocalOllamaMCPServer.Configuration;

namespace DimonSmart.LocalOllamaMCPServer
{
    internal interface IOllamaService
    {
        Task<string> GenerateAsync(string model, string prompt, Dictionary<string, object>? options, string? connectionName = null);
        IEnumerable<OllamaServerConfig> GetConfigurations();
    }
}
