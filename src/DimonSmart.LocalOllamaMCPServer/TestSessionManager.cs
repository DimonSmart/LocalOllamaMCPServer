using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace DimonSmart.LocalOllamaMCPServer;

internal sealed class TestSession
{
    internal TestSession(string model, string? systemPrompt, JsonElement? tools, JsonElement? options, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Model = model;
        SystemPrompt = systemPrompt;
        Tools = tools?.Clone();
        Options = options?.Clone();
        LastActivity = now;
    }

    internal Guid Id { get; }
    internal string Model { get; set; }
    internal string? SystemPrompt { get; set; }
    internal JsonElement? Tools { get; set; }
    internal JsonElement? Options { get; set; }
    internal List<JsonElement> History { get; } = new();
    internal DateTimeOffset LastActivity { get; set; }
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal object Snapshot() => new
    {
        session_id = Id.ToString("D"),
        model = Model,
        system_prompt = SystemPrompt,
        tools = Tools,
        options = Options,
        history = History.ToArray()
    };
}

internal sealed class TestSessionManager
{
    private readonly ConcurrentDictionary<Guid, TestSession> _sessions = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _idleTimeout;

    internal TestSessionManager(TimeProvider clock, TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));

        _clock = clock;
        _idleTimeout = idleTimeout;
    }

    internal TestSession Create(string model, string? systemPrompt = null, JsonElement? tools = null, JsonElement? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ValidateTools(tools);
        ValidateOptions(options);

        var session = new TestSession(model, systemPrompt, tools, options, _clock.GetUtcNow());
        if (!_sessions.TryAdd(session.Id, session))
            throw new InvalidOperationException("Failed to create a unique session.");

        return session;
    }

    internal async Task<T> WithSessionAsync<T>(string sessionId, Func<TestSession, Task<T>> action, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(sessionId, "D", out var id) || !_sessions.TryGetValue(id, out var session))
            throw new KeyNotFoundException($"Session '{sessionId}' not found.");

        await session.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(id, out var current) || !ReferenceEquals(current, session))
                throw new KeyNotFoundException($"Session '{sessionId}' not found.");

            if (_clock.GetUtcNow() - session.LastActivity >= _idleTimeout)
            {
                Remove(id, session);
                throw new KeyNotFoundException($"Session '{sessionId}' has expired.");
            }

            session.LastActivity = _clock.GetUtcNow();
            return await action(session).ConfigureAwait(false);
        }
        finally
        {
            session.LastActivity = _clock.GetUtcNow();
            session.Gate.Release();
        }
    }

    internal Task<object> ConfigureAsync(string sessionId, string? model, string? systemPrompt, JsonElement? tools, JsonElement? options, CancellationToken ct) =>
        WithSessionAsync(sessionId, session =>
        {
            if (model != null)
                ArgumentException.ThrowIfNullOrWhiteSpace(model);
            ValidateTools(tools);
            ValidateOptions(options);

            var changed = (model != null && model != session.Model)
                || (systemPrompt != null && systemPrompt != session.SystemPrompt)
                || (tools.HasValue && !SameJson(tools, session.Tools))
                || (options.HasValue && !SameJson(options, session.Options));

            if (model != null) session.Model = model;
            if (systemPrompt != null) session.SystemPrompt = systemPrompt;
            if (tools.HasValue) session.Tools = tools.Value.Clone();
            if (options.HasValue) session.Options = options.Value.Clone();
            if (changed) session.History.Clear();

            return Task.FromResult(session.Snapshot());
        }, ct);

    internal Task<object> ResetAsync(string sessionId, CancellationToken ct) =>
        WithSessionAsync(sessionId, session =>
        {
            session.History.Clear();
            return Task.FromResult(session.Snapshot());
        }, ct);

    internal Task<object> CloseAsync(string sessionId, CancellationToken ct) =>
        WithSessionAsync(sessionId, session =>
        {
            Remove(session.Id, session);
            return Task.FromResult<object>(new { session_id = session.Id.ToString("D"), closed = true });
        }, ct);

    internal void SweepExpired()
    {
        foreach (var pair in _sessions)
        {
            if (!pair.Value.Gate.Wait(0)) continue;
            try
            {
                if (_clock.GetUtcNow() - pair.Value.LastActivity >= _idleTimeout)
                    Remove(pair.Key, pair.Value);
            }
            finally
            {
                pair.Value.Gate.Release();
            }
        }
    }

    private void Remove(Guid id, TestSession session) =>
        ((ICollection<KeyValuePair<Guid, TestSession>>)_sessions)
            .Remove(new KeyValuePair<Guid, TestSession>(id, session));

    private static bool SameJson(JsonElement? left, JsonElement? right) =>
        left?.GetRawText() == right?.GetRawText();

    internal static void ValidateTools(JsonElement? tools)
    {
        if (!tools.HasValue) return;
        if (tools.Value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("tools must be an array of OpenAI function tool definitions.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools.Value.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.Object ||
                !tool.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != "function" ||
                !tool.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object ||
                !fn.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()) || !names.Add(name.GetString()!))
                throw new ArgumentException("Every tool must be a unique OpenAI {type:'function',function:{name,...}} definition.");

            if (fn.TryGetProperty("parameters", out var parameters) && parameters.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("function.parameters must be a JSON Schema object.");
        }
    }

    internal static void ValidateOptions(JsonElement? options)
    {
        if (!options.HasValue) return;
        if (options.Value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("options must be a JSON object.");
        foreach (var option in options.Value.EnumerateObject())
        {
            if (option.Name is "model" or "messages" or "stream" or "tools" or "tool_choice" or "n")
                throw new ArgumentException($"Option '{option.Name}' cannot be overridden.");
        }
    }
}

internal sealed class SessionExpiryService : BackgroundService
{
    private readonly TestSessionManager _sessions;

    public SessionExpiryService(TestSessionManager sessions) => _sessions = sessions;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            _sessions.SweepExpired();
    }
}
