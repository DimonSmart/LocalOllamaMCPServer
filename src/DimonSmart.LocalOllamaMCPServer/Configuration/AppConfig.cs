namespace DimonSmart.LocalOllamaMCPServer.Configuration
{
    internal sealed class AppConfig
    {
        public List<OllamaServerConfig> Servers { get; set; } = [];
        public string? DefaultServerName { get; set; }
    }
}
