using System.Collections.ObjectModel;

namespace DimonSmart.LocalOllamaMCPServer.Configuration
{
    internal sealed class AppConfig
    {
        public Collection<OllamaServerConfig> Servers { get; } = new();
        public string? DefaultServerName { get; set; }
    }
}
