using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

#pragma warning disable CS0618

namespace DimonSmart.LocalOllamaMCPServer.Tests;

public sealed class RootsStateTests
{
    [Fact]
    public async Task TryCreateWorkspaceFileSystemAsync_ReturnsError_WhenClientDoesNotSupportRoots()
    {
        var serverMock = new Mock<McpServer>(MockBehavior.Strict);
        serverMock.SetupGet(s => s.ClientCapabilities).Returns(new ClientCapabilities());
        serverMock
            .Setup(s => s.RegisterNotificationHandler(NotificationMethods.InitializedNotification, It.IsAny<Func<JsonRpcNotification, CancellationToken, ValueTask>>()))
            .Returns(Mock.Of<IAsyncDisposable>());
        serverMock
            .Setup(s => s.RegisterNotificationHandler(NotificationMethods.RootsListChangedNotification, It.IsAny<Func<JsonRpcNotification, CancellationToken, ValueTask>>()))
            .Returns(Mock.Of<IAsyncDisposable>());

        var state = new RootsState(serverMock.Object, NullLogger<RootsState>.Instance);

        var (fileSystem, error) = await state.TryCreateWorkspaceFileSystemAsync(NullLogger.Instance, CancellationToken.None);

        Assert.Null(fileSystem);
        Assert.Equal("Connected MCP host does not advertise roots/list support.", error);
        await state.DisposeAsync();
    }
}
#pragma warning restore CS0618
