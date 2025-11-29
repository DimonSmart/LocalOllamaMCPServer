using System.Net;
using System.Text.Json;
using DimonSmart.LocalOllamaMCPServer;
using DimonSmart.LocalOllamaMCPServer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace DimonSmart.LocalOllamaMCPServer.Tests
{
    public class ErrorHandlingTests
    {
        [Fact]
        public async Task QueryOllama_ReturnsErrorMessage_WhenModelNotFound()
        {
            // Arrange
            var modelName = "non-existent-model";
            var errorJson = "{\"error\":\"model 'non-existent-model' not found, try pulling it first\"}";

            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.NotFound,
                    Content = new StringContent(errorJson)
                });

            var httpClient = new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri("http://localhost:11434")
            };

            var mockFactory = new Mock<IHttpClientFactory>();
            mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

            var appConfig = new AppConfig
            {
                DefaultServerName = "local",
                Servers = new List<OllamaServerConfig>
                {
                    new OllamaServerConfig { Name = "local", BaseUrl = new Uri("http://localhost:11434") }
                }
            };

            var mockOptions = new Mock<IOptions<AppConfig>>();
            mockOptions.Setup(o => o.Value).Returns(appConfig);

            var mockLogger = new Mock<ILogger<OllamaService>>();
            var service = new OllamaService(mockFactory.Object, mockOptions.Object, mockLogger.Object);

            // Act
            // We call the tool method directly to verify the try-catch block we added in OllamaTools
            var result = await OllamaTools.QueryOllama(
                model_name: modelName,
                prompt: "test",
                options: null,
                connection_name: null,
                ollamaService: service,
                logger: mockLogger.Object
            );

            // Assert
            Assert.StartsWith("Error:", result);
            Assert.Contains("NotFound", result);
            Assert.Contains("model 'non-existent-model' not found", result);
        }
    }
}
