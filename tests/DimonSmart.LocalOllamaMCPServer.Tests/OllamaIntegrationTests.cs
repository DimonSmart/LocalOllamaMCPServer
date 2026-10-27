using System.Text.Json;

namespace DimonSmart.LocalOllamaMCPServer.Tests;

public sealed class OllamaIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task LocalOllamaCanCompleteAChatWhenExplicitlyEnabled()
    {
        var model = Environment.GetEnvironmentVariable("OLLAMA_INTEGRATION_MODEL");
        if (string.IsNullOrWhiteSpace(model))
            return;

        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:11434/v1/"), Timeout = TimeSpan.FromMinutes(5) };
        var client = new OpenAiOllamaClient(http);
        var sessions = new TestSessionManager(TimeProvider.System, TimeSpan.FromHours(1));
        var session = sessions.Create(model, "Answer briefly.");
        var messages = JsonSerializer.SerializeToElement(new[]
        {
            new { role = "user", content = "Reply with the word test." }
        });
        var output = await OllamaTools.Chat(session.Id.ToString("D"), messages, sessions, client);
        using var response = JsonDocument.Parse(output);
        Assert.Equal("assistant", response.RootElement.GetProperty("message").GetProperty("role").GetString());
    }
}
