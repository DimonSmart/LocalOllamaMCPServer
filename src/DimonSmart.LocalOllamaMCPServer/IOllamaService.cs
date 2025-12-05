using DimonSmart.LocalOllamaMCPServer.Configuration;
using System.Threading;

namespace DimonSmart.LocalOllamaMCPServer
{
    internal interface IOllamaService
    {
        Task<string> GenerateAsync(string model, string prompt, Dictionary<string, object>? options, string? connectionName = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyCollection<string>> GetModelsAsync(string? connectionName = null, CancellationToken cancellationToken = default);
        IReadOnlyCollection<OllamaServerConfig> GetConfigurations();
    }
}
