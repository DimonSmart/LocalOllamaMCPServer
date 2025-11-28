namespace DimonSmart.LocalOllamaMCPServer.Configuration
{
    public class AppConfig
    {
        public List<OllamaServerConfig> Servers { get; set; } = new();
        public string? DefaultServerName { get; set; }
    }
}
