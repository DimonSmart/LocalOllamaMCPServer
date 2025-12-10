using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DimonSmart.LocalOllamaMCPServer;

internal sealed class RootsState : IAsyncDisposable
{
    private readonly ILogger<RootsState> _logger;
    private readonly McpServer _server;
    private List<Root> _currentRoots = [];
    private string? _lastError;
    private bool _hasFetchedRoots;
    private IAsyncDisposable? _initializedRegistration;
    private IAsyncDisposable? _rootsChangedRegistration;

    public RootsState(
        McpServer server,
        ILogger<RootsState> logger)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(logger);

        _server = server;
        _logger = logger;

        RegisterNotificationHandlers();
    }

    private void RegisterNotificationHandlers()
    {
        _initializedRegistration = _server.RegisterNotificationHandler(
            NotificationMethods.InitializedNotification,
            async (notification, cancellationToken) =>
            {
                _logger.LogDebug("Refreshing workspace roots after '{Source}' notification.", notification.Method);
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            });

        _rootsChangedRegistration = _server.RegisterNotificationHandler(
            NotificationMethods.RootsListChangedNotification,
            async (notification, cancellationToken) =>
            {
                _logger.LogDebug("Refreshing workspace roots after '{Source}' notification.", notification.Method);
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            });
    }

    public async Task<(WorkspaceFileSystem? FileSystem, string? Error)> TryCreateWorkspaceFileSystemAsync(
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logger);

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (_currentRoots.Count == 0)
            return (null, _lastError ?? "The MCP host did not expose any usable file-system roots.");

        if (!WorkspaceFileSystem.TryCreate(_currentRoots, logger, out var fileSystem, out var conversionError))
            return (null, conversionError ?? _lastError ?? "The MCP host did not expose any usable file-system roots.");

        return (fileSystem, null);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_hasFetchedRoots) return;

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _currentRoots = [];
        _lastError = null;
        _hasFetchedRoots = true;

        if (_server.ClientCapabilities?.Roots is null)
        {
            _lastError = "Connected MCP host does not advertise roots/list support.";
            _logger.LogDebug("Client does not advertise roots/list support.");
            return;
        }

        var rootsResult = await TryRequestRootsAsync(cancellationToken).ConfigureAwait(false);
        if (rootsResult is null) return;

        _currentRoots = rootsResult.Roots?.ToList() ?? [];

        if (_currentRoots.Count == 0)
        {
            _lastError = "The MCP host did not report any workspace roots.";
            _logger.LogWarning("MCP host responded with zero workspace roots.");
            return;
        }

        _logger.LogInformation("Cached {Count} workspace roots.", _currentRoots.Count);
    }

    private async Task<ListRootsResult?> TryRequestRootsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _server
                .RequestRootsAsync(new ListRootsRequestParams(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _lastError = "Connected MCP host rejected roots/list requests.";
            _logger.LogWarning(ex, "MCP host rejected roots/list request.");
        }
        catch (Exception ex)
        {
            _lastError = $"Failed to request roots from the MCP host: {ex.Message}";
            _logger.LogError(ex, "Unexpected error while requesting roots from the MCP host.");
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_initializedRegistration is not null)
        {
            await _initializedRegistration.DisposeAsync().ConfigureAwait(false);
        }

        if (_rootsChangedRegistration is not null)
        {
            await _rootsChangedRegistration.DisposeAsync().ConfigureAwait(false);
        }
    }
}
