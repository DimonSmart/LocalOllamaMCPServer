using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace DimonSmart.LocalOllamaMCPServer;

internal sealed class WorkspaceFileSystem
{
    internal sealed record WorkspaceRoot(string Name, string Path);

    internal sealed record WorkspaceFile(string AbsolutePath, WorkspaceRoot Root, string RelativePath)
    {
        public string FileName => Path.GetFileName(AbsolutePath);
    }

    private readonly IReadOnlyList<WorkspaceRoot> _roots;
    private readonly ILogger _logger;

    private WorkspaceFileSystem(IReadOnlyList<WorkspaceRoot> roots, ILogger logger)
    {
        _roots = roots;
        _logger = logger;
    }

    public IReadOnlyList<WorkspaceRoot> Roots => _roots;

    public bool HasRoots => _roots.Count > 0;

    public static bool TryCreate(
        IEnumerable<Root>? protocolRoots,
        ILogger logger,
        out WorkspaceFileSystem? workspaceFileSystem,
        out string? error)
    {
        var resolvedRoots = ConvertRoots(protocolRoots, logger);
        if (resolvedRoots.Count == 0)
        {
            workspaceFileSystem = null;
            error = "The MCP host did not expose any usable file-system roots.";
            return false;
        }

        workspaceFileSystem = new WorkspaceFileSystem(resolvedRoots, logger);
        error = null;
        return true;
    }

    public bool TryResolveFile(
        string filePath,
        string? rootName,
        out WorkspaceFile? rootedFile,
        out string? error)
    {
        rootedFile = null;
        error = null;

        if (!HasRoots)
        {
            error = "No workspace roots are available.";
            return false;
        }

        var candidateRoots = ResolveCandidateRoots(rootName, out var rootError);
        if (candidateRoots.Count == 0)
        {
            error = rootError ?? "No matching roots found.";
            return false;
        }

        string normalizedInput;
        try
        {
            normalizedInput = NormalizePath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid path '{filePath}': {ex.Message}";
            return false;
        }

        if (Path.IsPathRooted(filePath))
        {
            var root = FindRootForPath(normalizedInput, candidateRoots);
            if (root is null)
            {
                error = $"Path '{filePath}' is outside the allowed roots.";
                return false;
            }

            if (!File.Exists(normalizedInput))
            {
                error = $"File '{normalizedInput}' does not exist.";
                return false;
            }

            rootedFile = BuildWorkspaceFile(normalizedInput, root);
            return true;
        }

        var matches = new List<WorkspaceFile>();
        foreach (var root in candidateRoots)
        {
            string combined;
            try
            {
                combined = NormalizePath(Path.Combine(root.Path, filePath));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                _logger.LogWarning(ex, "Skipping invalid relative path '{Relative}' under root '{Root}'.", filePath, root.Path);
                continue;
            }

            if (File.Exists(combined))
            {
                matches.Add(BuildWorkspaceFile(combined, root));
            }
        }

        if (matches.Count == 0)
        {
            error = $"File '{filePath}' was not found under the allowed roots.";
            return false;
        }

        if (matches.Count > 1 && string.IsNullOrWhiteSpace(rootName))
        {
            error = $"File '{filePath}' is ambiguous across multiple roots. Provide root_name.";
            return false;
        }

        rootedFile = matches[0];
        return true;
    }

    public IReadOnlyList<WorkspaceFile> EnumerateFiles(string searchPattern, string? rootName, int? maxFiles = null)
    {
        if (!HasRoots)
        {
            return [];
        }

        var candidateRoots = ResolveCandidateRoots(rootName, out _);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<WorkspaceFile>();

        foreach (var root in candidateRoots)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root.Path, searchPattern, SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Failed to enumerate files inside root '{Root}'.", root.Path);
                continue;
            }

            foreach (var file in files)
            {
                var normalized = NormalizePath(file);
                if (!seen.Add(normalized))
                {
                    continue;
                }

                items.Add(BuildWorkspaceFile(normalized, root));
                if (maxFiles.HasValue && maxFiles.Value > 0 && items.Count >= maxFiles.Value)
                {
                    return items;
                }
            }
        }

        return items;
    }

    public async Task<string> ReadFileAsync(WorkspaceFile file, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Reading workspace file {File}", file.RelativePath);

        var info = new FileInfo(file.AbsolutePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"File '{file.AbsolutePath}' does not exist.", file.AbsolutePath);
        }

        using var stream = new FileStream(file.AbsolutePath, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static List<WorkspaceRoot> ConvertRoots(IEnumerable<Root>? protocolRoots, ILogger logger)
    {
        var roots = new List<WorkspaceRoot>();
        if (protocolRoots is null)
        {
            return roots;
        }

        foreach (var candidate in protocolRoots)
        {
            if (TryConvertRoot(candidate, logger, out var root))
            {
                roots.Add(root);
            }
        }

        return roots;
    }

    private static bool TryConvertRoot(Root? candidate, ILogger logger, out WorkspaceRoot root)
    {
        root = default!;
        if (candidate is null || string.IsNullOrWhiteSpace(candidate.Uri))
        {
            logger.LogWarning("Skipping root with missing URI.");
            return false;
        }

        if (!Uri.TryCreate(candidate.Uri, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("Skipping root '{Uri}' because it is not a valid absolute URI.", candidate.Uri);
            return false;
        }

        if (!uri.IsFile)
        {
            logger.LogWarning("Skipping root '{Uri}' because scheme '{Scheme}' is not supported.", candidate.Uri, uri.Scheme);
            return false;
        }

        var path = NormalizeDirectoryPath(uri.LocalPath);
        if (!Directory.Exists(path))
        {
            logger.LogWarning("Skipping root '{Path}' because the directory does not exist.", path);
            return false;
        }

        var name = string.IsNullOrWhiteSpace(candidate.Name) ? new DirectoryInfo(path).Name : candidate.Name!;
        root = new WorkspaceRoot(name, path);
        return true;
    }

    private List<WorkspaceRoot> ResolveCandidateRoots(string? rootName, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(rootName))
        {
            return _roots.ToList();
        }

        var matches = _roots.Where(r => string.Equals(r.Name, rootName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            error = $"Root '{rootName}' is not available.";
        }

        return matches;
    }

    private static WorkspaceRoot? FindRootForPath(string path, IEnumerable<WorkspaceRoot> roots)
    {
        foreach (var root in roots)
        {
            if (IsPathUnderRoot(path, root.Path))
            {
                return root;
            }
        }

        return null;
    }

    private static WorkspaceFile BuildWorkspaceFile(string absolutePath, WorkspaceRoot root)
    {
        var relative = Path.GetRelativePath(root.Path, absolutePath);
        return new WorkspaceFile(absolutePath, root, relative);
    }

    private static bool IsPathUnderRoot(string targetPath, string rootPath)
    {
        var normalizedTarget = NormalizePath(targetPath);
        var normalizedRoot = NormalizeDirectoryPath(rootPath);
        if (normalizedTarget.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        return normalizedTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }
}
