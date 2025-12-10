using System;
using System.IO;
using System.Linq;
using DimonSmart.LocalOllamaMCPServer;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Moq;

namespace DimonSmart.LocalOllamaMCPServer.Tests;

public class WorkspaceFileSystemTests
{
    [Fact]
    public void TryResolveFile_AllowsPathWithinRoot()
    {
        using var temp = new TempDirectory();
        var filePath = Path.Combine(temp.RootPath, "sample.md");
        File.WriteAllText(filePath, "hello");

        var fileSystem = CreateFileSystem(temp.RootPath);

        var success = fileSystem.TryResolveFile(filePath, null, out var rootedFile, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.NotNull(rootedFile);
        Assert.Equal("sample.md", rootedFile!.RelativePath.Replace("\\", "/"));
    }

    [Fact]
    public void TryResolveFile_RejectsPathOutsideRoot()
    {
        using var temp = new TempDirectory();
        var outsideFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.md");
        File.WriteAllText(outsideFile, "outside");

        var fileSystem = CreateFileSystem(temp.RootPath);

        var success = fileSystem.TryResolveFile(outsideFile, null, out _, out var error);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void EnumerateFiles_RespectsPatternAndLimit()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.RootPath, "a.md"), "a");
        File.WriteAllText(Path.Combine(temp.RootPath, "b.md"), "b");
        File.WriteAllText(Path.Combine(temp.RootPath, "c.txt"), "c");

        var fileSystem = CreateFileSystem(temp.RootPath);

        var allMarkdown = fileSystem.EnumerateFiles("*.md", null).ToList();
        Assert.Equal(2, allMarkdown.Count);

        var limited = fileSystem.EnumerateFiles("*.md", null, maxFiles: 1).ToList();
        Assert.Single(limited);
    }

    private static WorkspaceFileSystem CreateFileSystem(string rootPath)
    {
        var root = new Root
        {
            Uri = new Uri(rootPath).AbsoluteUri,
            Name = "temp"
        };

        var logger = new Mock<ILogger>();
        Assert.True(WorkspaceFileSystem.TryCreate([root], logger.Object, out var fileSystem, out var error));
        Assert.Null(error);
        return fileSystem!;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TempDirectory()
        {
            Directory.CreateDirectory(RootPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, true);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
