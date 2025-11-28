namespace DimonSmart.LocalOllamaMCPServer.Configuration
{
    public class OllamaServerConfig
    {
        public string Name { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string? User { get; set; }
        public string? Password { get; set; }
        public bool IgnoreSsl { get; set; }
    }
}
