namespace DimonSmart.LocalOllamaMCPServer.Configuration
{
    internal sealed class OllamaServerConfig
    {
        public string Name { get; set; } = string.Empty;
        public Uri BaseUrl { get; set; } = new Uri("http://localhost:11434");
        public string? User { get; set; }
        public string? Password { get; set; }
        public bool IgnoreSsl { get; set; }
    }
}
