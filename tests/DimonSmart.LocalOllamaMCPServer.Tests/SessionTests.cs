using System.Net;
using System.Text;
using System.Text.Json;

namespace DimonSmart.LocalOllamaMCPServer.Tests;

public sealed class SessionTests
{
    private static readonly Uri Endpoint = new("http://127.0.0.1:11434/v1/");

    [Fact]
    public async Task CreateConfigureResetAndCloseRetainExpectedState()
    {
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("model-a", sessions, "initial",
            J(@"[{""type"":""function"",""function"":{""name"":""get_time"",""parameters"":{""type"":""object""}}}]"),
            J(@"{""temperature"":0.2}")));

        var initial = J(await OllamaTools.GetSession(id, sessions));
        Assert.Equal("model-a", initial.GetProperty("model").GetString());
        Assert.Equal("initial", initial.GetProperty("system_prompt").GetString());
        Assert.Equal(1, initial.GetProperty("tools").GetArrayLength());

        var changed = J(await OllamaTools.ConfigureSession(id, sessions, "model-b", "", J("[]"), J("{}")));
        Assert.Equal("model-b", changed.GetProperty("model").GetString());
        Assert.Equal("", changed.GetProperty("system_prompt").GetString());
        Assert.Empty(changed.GetProperty("tools").EnumerateArray());
        Assert.Empty(changed.GetProperty("options").EnumerateObject());

        await OllamaTools.ResetSession(id, sessions);
        var current = J(await OllamaTools.GetSession(id, sessions));
        Assert.Empty(current.GetProperty("history").EnumerateArray());
        Assert.Equal("model-b", current.GetProperty("model").GetString());

        var closed = J(await OllamaTools.CloseSession(id, sessions));
        Assert.True(closed.GetProperty("closed").GetBoolean());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => OllamaTools.GetSession(id, sessions));
    }

    [Fact]
    public async Task ChatSendsSystemPromptHistoryOptionsAndPreservesRawCompletion()
    {
        var requests = new List<string>();
        var responses = new Queue<string>(new[]
        {
            Completion(@"{""role"":""assistant"",""content"":""Hi""}", "stop",
                @"""usage"":{""prompt_tokens"":5,""completion_tokens"":2,""total_tokens"":7},"),
            Completion(@"{""role"":""assistant"",""content"":""Again"",""reasoning"":""brief""}", "stop")
        });
        using var http = Http(async (request, ct) =>
        {
            Assert.Equal("/v1/chat/completions", request.RequestUri!.AbsolutePath);
            requests.Add(await request.Content!.ReadAsStringAsync(ct));
            return Reply(responses.Dequeue());
        });
        var client = new OpenAiOllamaClient(http);
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("small", sessions, "Be concise", options: J(@"{""temperature"":0.3,""seed"":42}")));

        var first = J(await OllamaTools.Chat(id, J(@"[{""role"":""user"",""content"":""Hello""}]"), sessions, client));
        Assert.Equal("Hi", first.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(7, first.GetProperty("usage").GetProperty("total_tokens").GetInt32());
        Assert.Equal(7, first.GetProperty("raw_response").GetProperty("usage").GetProperty("total_tokens").GetInt32());

        var second = J(await OllamaTools.Chat(id, J(@"[{""role"":""user"",""content"":""Next""}]"), sessions, client));
        Assert.Equal("brief", second.GetProperty("message").GetProperty("reasoning").GetString());
        Assert.False(second.TryGetProperty("usage", out _));

        var sentFirst = J(requests[0]);
        Assert.Equal("small", sentFirst.GetProperty("model").GetString());
        Assert.False(sentFirst.GetProperty("stream").GetBoolean());
        Assert.Equal(0.3, sentFirst.GetProperty("temperature").GetDouble(), 5);
        Assert.Equal(42, sentFirst.GetProperty("seed").GetInt32());
        Assert.Equal("system", sentFirst.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Be concise", sentFirst.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(2, sentFirst.GetProperty("messages").GetArrayLength());

        var sentSecond = J(requests[1]);
        Assert.Equal(4, sentSecond.GetProperty("messages").GetArrayLength());
        Assert.Equal("Hi", sentSecond.GetProperty("messages")[2].GetProperty("content").GetString());

        var history = J(await OllamaTools.GetSession(id, sessions)).GetProperty("history");
        Assert.Equal(4, history.GetArrayLength());
        Assert.Equal("assistant", history[3].GetProperty("role").GetString());
    }

    [Fact]
    public async Task MultipleToolCallsRequireExactBatchBeforeModelContinues()
    {
        var toolMessage = JsonSerializer.Serialize(new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = new[]
            {
                new { id = "c1", type = "function", function = new { name = "lookup", arguments = "{\"query\":\"one\"}" } },
                new { id = "c2", type = "function", function = new { name = "lookup", arguments = "{\"query\":\"two\"}" } }
            }
        });
        var calls = 0;
        var sent = new List<string>();
        using var http = Http(async (request, ct) =>
        {
            sent.Add(await request.Content!.ReadAsStringAsync(ct));
            calls++;
            return Reply(calls == 1
                ? Completion(toolMessage, "tool_calls")
                : Completion(@"{""role"":""assistant"",""content"":""Both processed""}", "stop"));
        });
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("small", sessions,
            tools: J(@"[{""type"":""function"",""function"":{""name"":""lookup"",""description"":""Find an item"",""parameters"":{""type"":""object"",""properties"":{""query"":{""type"":""string""}}}}}]")));
        var client = new OpenAiOllamaClient(http);

        var first = J(await OllamaTools.Chat(id, J(@"[{""role"":""user"",""content"":""Search twice""}]"), sessions, client));
        Assert.Equal(2, first.GetProperty("message").GetProperty("tool_calls").GetArrayLength());
        Assert.Equal("{\"query\":\"two\"}", first.GetProperty("message").GetProperty("tool_calls")[1].GetProperty("function").GetProperty("arguments").GetString());

        var incomplete = J(@"[{""role"":""tool"",""tool_call_id"":""c1"",""content"":""found""}]");
        await Assert.ThrowsAsync<ArgumentException>(() => OllamaTools.Chat(id, incomplete, sessions, client));
        await Assert.ThrowsAsync<ArgumentException>(() => OllamaTools.Chat(id,
            J(@"[{""role"":""tool"",""tool_call_id"":""wrong"",""content"":""x""},{""role"":""tool"",""tool_call_id"":""c2"",""content"":""y""}]"),
            sessions, client));
        await Assert.ThrowsAsync<ArgumentException>(() => OllamaTools.Chat(id,
            J(@"[{""role"":""tool"",""tool_call_id"":""c1"",""content"":""x""},{""role"":""tool"",""tool_call_id"":""c1"",""content"":""x""}]"),
            sessions, client));
        Assert.Equal(1, calls);

        var continued = J(await OllamaTools.Chat(id,
            J(@"[{""role"":""tool"",""tool_call_id"":""c2"",""content"":""two found""},{""role"":""tool"",""tool_call_id"":""c1"",""content"":""one found""}]"),
            sessions, client));
        Assert.Equal("Both processed", continued.GetProperty("message").GetProperty("content").GetString());
        Assert.Equal(2, calls);
        var request = J(sent[1]);
        Assert.Equal(4, request.GetProperty("messages").GetArrayLength());
        Assert.Equal("tool", request.GetProperty("messages")[2].GetProperty("role").GetString());
        Assert.Equal("c1", request.GetProperty("messages")[3].GetProperty("tool_call_id").GetString());

        var history = J(await OllamaTools.GetSession(id, sessions)).GetProperty("history");
        Assert.Equal(5, history.GetArrayLength());
    }

    [Fact]
    public async Task ConfigChangeClearsHistoryAndOmittedParametersRemainUnchanged()
    {
        using var http = Http((_, _) => Task.FromResult(Reply(Completion(@"{""role"":""assistant"",""content"":""ok""}", "stop"))));
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("a", sessions, "old", options: J(@"{""temperature"":0.5}")));
        await OllamaTools.Chat(id, J(@"[{""role"":""user"",""content"":""hi""}]"), sessions, new OpenAiOllamaClient(http));

        var unchanged = J(await OllamaTools.ConfigureSession(id, sessions, null, null, null, null));
        Assert.Equal(2, unchanged.GetProperty("history").GetArrayLength());

        var changed = J(await OllamaTools.ConfigureSession(id, sessions, system_prompt: "new"));
        Assert.Empty(changed.GetProperty("history").EnumerateArray());
        Assert.Equal("a", changed.GetProperty("model").GetString());
        Assert.Equal(0.5, changed.GetProperty("options").GetProperty("temperature").GetDouble(), 5);
    }

    [Fact]
    public async Task SessionsHaveIndependentHistories()
    {
        using var http = Http((_, _) => Task.FromResult(Reply(Completion(@"{""role"":""assistant"",""content"":""ok""}", "stop"))));
        var client = new OpenAiOllamaClient(http);
        var sessions = NewSessions();
        var a = NewId(OllamaTools.CreateSession("model-a", sessions));
        var b = NewId(OllamaTools.CreateSession("model-b", sessions));
        await Task.WhenAll(
            OllamaTools.Chat(a, J(@"[{""role"":""user"",""content"":""one""}]"), sessions, client),
            OllamaTools.Chat(b, J(@"[{""role"":""user"",""content"":""two""}]"), sessions, client));
        Assert.Equal("one", J(await OllamaTools.GetSession(a, sessions)).GetProperty("history")[0].GetProperty("content").GetString());
        Assert.Equal("two", J(await OllamaTools.GetSession(b, sessions)).GetProperty("history")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ProviderFailureDoesNotMutateHistoryAndRetryWorks()
    {
        var calls = 0;
        using var http = Http((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1 ? Reply(@"{""error"":""unsupported""}", HttpStatusCode.BadRequest) :
                Reply(Completion(@"{""role"":""assistant"",""content"":""ok""}", "stop")));
        });
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("small", sessions));
        var client = new OpenAiOllamaClient(http);
        var messages = J(@"[{""role"":""user"",""content"":""hello""}]");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => OllamaTools.Chat(id, messages, sessions, client));
        Assert.Contains("unsupported", ex.Message);
        Assert.Empty(J(await OllamaTools.GetSession(id, sessions)).GetProperty("history").EnumerateArray());
        await OllamaTools.Chat(id, messages, sessions, client);
        Assert.Equal(2, J(await OllamaTools.GetSession(id, sessions)).GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task MalformedProviderResponseDoesNotMutateHistory()
    {
        using var http = Http((_, _) => Task.FromResult(Reply(@"{""choices"":[]}")));
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("small", sessions));
        await Assert.ThrowsAsync<JsonException>(() => OllamaTools.Chat(id,
            J(@"[{""role"":""user"",""content"":""test""}]"), sessions, new OpenAiOllamaClient(http)));
        Assert.Empty(J(await OllamaTools.GetSession(id, sessions)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task CancellationDoesNotMutateHistory()
    {
        using var http = Http(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Reply("{}");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("small", sessions));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OllamaTools.Chat(id,
            J(@"[{""role"":""user"",""content"":""test""}]"), sessions, new OpenAiOllamaClient(http), cts.Token));
        Assert.Empty(J(await OllamaTools.GetSession(id, sessions)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task HttpTimeoutDoesNotMutateHistory()
    {
        using var http = Http(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Reply("{}");
        });
        http.Timeout = TimeSpan.FromMilliseconds(40);
        var sessions = NewSessions();
        var id = NewId(OllamaTools.CreateSession("small", sessions));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OllamaTools.Chat(id,
            J(@"[{""role"":""user"",""content"":""test""}]"), sessions, new OpenAiOllamaClient(http)));
        Assert.Empty(J(await OllamaTools.GetSession(id, sessions)).GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task ExpiredSessionsAreEvictedAndActiveSessionsRemain()
    {
        var clock = new ManualClock();
        var sessions = new TestSessionManager(clock, TimeSpan.FromMinutes(60));
        var expired = NewId(OllamaTools.CreateSession("a", sessions));
        clock.Advance(TimeSpan.FromMinutes(30));
        var active = NewId(OllamaTools.CreateSession("b", sessions));
        clock.Advance(TimeSpan.FromMinutes(31));
        sessions.SweepExpired();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => OllamaTools.GetSession(expired, sessions));
        Assert.Equal("b", J(await OllamaTools.GetSession(active, sessions)).GetProperty("model").GetString());
        clock.Advance(TimeSpan.FromMinutes(61));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => OllamaTools.GetSession(active, sessions));
    }

    [Fact]
    public async Task ModelListUsesOpenAiEndpoint()
    {
        using var http = Http((request, _) =>
        {
            Assert.Equal("/v1/models", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Reply(@"{""object"":""list"",""data"":[{""id"":""qwen-small"",""object"":""model""}]}"));
        });
        var data = J(await OllamaTools.ListModels(new OpenAiOllamaClient(http)));
        Assert.Equal("qwen-small", data.GetProperty("data")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void InvalidToolsAndReservedOptionsAreRejected()
    {
        var sessions = NewSessions();
        Assert.Throws<ArgumentException>(() => OllamaTools.CreateSession("x", sessions, tools: J(@"[{""name"":""not-wrapped""}]")));
        Assert.Throws<ArgumentException>(() => OllamaTools.CreateSession("x", sessions, options: J(@"{""tool_choice"":""auto""}")));
        Assert.Throws<ArgumentException>(() => OllamaTools.CreateSession("x", sessions, options: J(@"{""messages"":[]}")));
        Assert.Throws<ArgumentException>(() => OllamaTools.CreateSession(" ", sessions));
    }

    private static TestSessionManager NewSessions() => new(TimeProvider.System, TimeSpan.FromHours(1));
    private static string NewId(string snapshot) => J(snapshot).GetProperty("session_id").GetString()!;
    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string Completion(string message, string reason, string additional = "") =>
        "{\"choices\":[{\"message\":" + message + ",\"finish_reason\":\"" + reason + "\"}]," + additional.TrimEnd(',') + (additional.Length > 0 ? "" : "\"id\":\"test\"") + "}";

    private static HttpResponseMessage Reply(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new DelegateHandler(handler)) { BaseAddress = Endpoint };

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        internal DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan span) => _now += span;
    }
}
