using EasyVCR;
using DimonSmart.LocalOllamaMCPServer;
using DimonSmart.LocalOllamaMCPServer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace DimonSmart.LocalOllamaMCPServer.Tests
{
    public class OllamaServiceTests
    {
        [Fact]
        public async Task GenerateAsync_ReturnsResponse_WhenCalledWithValidParams()
        {
            // Arrange
            var cassettePath = Path.Combine(GetProjectRoot(), "cassettes");
            if (!Directory.Exists(cassettePath))
            {
                Directory.CreateDirectory(cassettePath);
            }

            var vcr = new VCR(new AdvancedSettings
            {
                MatchRules = MatchRules.Default
            });

            var cassette = new Cassette(cassettePath, "ollama_generate_test");
            vcr.Insert(cassette);

            if (File.Exists(Path.Combine(cassettePath, "ollama_generate_test.json")))
            {
                vcr.Replay();
            }
            else
            {
                vcr.Record();
            }

            var httpClient = vcr.Client;
            // Ensure the client has a base address as expected by the service
            httpClient.BaseAddress = new Uri("http://localhost:11434");

            // Mock IHttpClientFactory
            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            // Mock IOptions<AppConfig>
            var appConfig = new AppConfig
            {
                DefaultServerName = "test",
            };
            appConfig.Servers.Add(new OllamaServerConfig
            {
                Name = "test",
                BaseUrl = new Uri("http://localhost:11434")
            });

            var mockOptions = new Mock<IOptions<AppConfig>>();
            mockOptions.Setup(o => o.Value).Returns(appConfig);

            // Mock ILogger
            var mockLogger = new Mock<ILogger<OllamaService>>();

            var service = new OllamaService(mockFactory.Object, mockOptions.Object, mockLogger.Object);

            var options = new Dictionary<string, object>
            {
                { "temperature", 0.7 }
            };

            // Act
            string result;
            try
            {
                result = await service.GenerateAsync("phi4:latest", "Say hello", options);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to query Ollama. Ensure Ollama is running and the specified model is pulled if recording.", ex);
            }

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        private static string GetProjectRoot()
        {
            var path = Directory.GetCurrentDirectory();
            var directory = new DirectoryInfo(path);
            while (directory != null && directory.GetFiles("*.csproj").Length == 0)
            {
                directory = directory.Parent;
            }
            return directory?.FullName ?? path;
        }
    }
}
