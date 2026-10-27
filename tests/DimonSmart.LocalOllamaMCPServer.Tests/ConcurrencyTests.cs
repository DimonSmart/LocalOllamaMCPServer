using System.Net;
using System.Text;
using System.Text.Json;

namespace DimonSmart.LocalOllamaMCPServer.Tests;

public sealed class ConcurrencyTests
{
    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static TestSessionManager NewSessions() => new(TimeProvider.System, TimeSpan.FromHours(1));

    [Fact]
    public async Task TwoChatsInOneSessionAreSerialized()
    {
        var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<JsonElement>();
        var count = 0;
        using var http = NewHttp(async (request, ct) =>
        {
            requests.Add(J(await request.Content!.ReadAsStringAsync(ct)));
            count++;
            if (count == 1)
            {
                firstEntered.SetResult(true);
                await releaseFirst.Task.WaitAsync(ct);
            }
            return Respond();
        });
        var client = new OpenAiOllamaClient(http);
        var sessions = NewSessions();
        var id = J(OllamaTools.CreateSession("small", sessions)).GetProperty("session_id").GetString()!;

        var first = OllamaTools.Chat(id, J(@"[{""role"":""user"",""content"":""first""}]"), sessions, client);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = OllamaTools.Chat(id, J(@"[{""role"":""user"",""content"":""second""}]"), sessions, client);

        Assert.Equal(1, count);
        releaseFirst.SetResult(true);
        await Task.WhenAll(first, second);
        Assert.Equal(2, count);
        Assert.Equal(3, requests[1].GetProperty("messages").GetArrayLength());
        Assert.Equal("ok", requests[1].GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task IndependentSessionsCanIssueHttpRequestsConcurrently()
    {
        var twoEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        using var http = NewHttp(async (_, ct) =>
        {
            if (Interlocked.Increment(ref count) == 2)
                twoEntered.TrySetResult(true);
            await release.Task.WaitAsync(ct);
            return Respond();
        });
        var client = new OpenAiOllamaClient(http);
        var sessions = NewSessions();
        var a = J(OllamaTools.CreateSession("a", sessions)).GetProperty("session_id").GetString()!;
        var b = J(OllamaTools.CreateSession("b", sessions)).GetProperty("session_id").GetString()!;

        var first = OllamaTools.Chat(a, J(@"[{""role"":""user"",""content"":""A""}]"), sessions, client);
        var second = OllamaTools.Chat(b, J(@"[{""role"":""user"",""content"":""B""}]"), sessions, client);
        try
        {
            await twoEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.TrySetResult(true);
        }
        await Task.WhenAll(first, second);
        Assert.Equal(2, count);
    }

    private static HttpResponseMessage Respond() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(@"{""choices"":[{""message"":{""role"":""assistant"",""content"":""ok""},""finish_reason"":""stop""}]}", Encoding.UTF8, "application/json")
    };

    private static HttpClient NewHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new DelegateHandler(handler)) { BaseAddress = new Uri("http://127.0.0.1:11434/v1/") };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        internal DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
