using EasyVCR;
using DimonSmart.LocalOllamaMCPServer;
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
            var service = new OllamaService(httpClient, "http://localhost:11434");

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
