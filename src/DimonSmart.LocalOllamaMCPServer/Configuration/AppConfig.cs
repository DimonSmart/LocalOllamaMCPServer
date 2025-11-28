using System.Collections.ObjectModel;

namespace DimonSmart.LocalOllamaMCPServer.Configuration
{
    internal sealed class AppConfig
    {
        public List<OllamaServerConfig> Servers { get; set; } = new();
        public string? DefaultServerName { get; set; }
    }
}
