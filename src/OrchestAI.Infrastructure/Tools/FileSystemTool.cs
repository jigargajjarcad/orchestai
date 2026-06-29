using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Tools;

public sealed class FileSystemTool : IMcpTool
{
    private readonly IOptions<ToolOptions> _options;
    private readonly ILogger<FileSystemTool> _logger;

    public string ToolName => "file_write";
    public string Description => "Read or write files in the agent workspace. Use for saving generated content or reading existing files.";

    public FileSystemTool(IOptions<ToolOptions> options, ILogger<FileSystemTool> logger)
    {
        _options = options;
        _logger = logger;
        EnsureSandboxExists();
    }

    public ToolInputSchema GetInputSchema() => new(
        Type: "object",
        Properties: new Dictionary<string, ToolProperty>
        {
            ["operation"] = new("string", "Operation: write, read, or list",
                Enum: ["write", "read", "list"]),
            ["path"] = new("string", "Relative path within the agent workspace"),
            ["content"] = new("string", "File content (required for write operation)")
        },
        Required: ["operation", "path"]
    );

    public async Task<McpToolResult> ExecuteAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("operation", out var operation))
            return new McpToolResult(false, string.Empty, "Missing required parameter: operation");

        if (!parameters.TryGetValue("path", out var path))
            return new McpToolResult(false, string.Empty, "Missing required parameter: path");

        if (path.Contains("..") || Path.IsPathRooted(path))
            return new McpToolResult(false, string.Empty, "Path traversal not allowed");

        var sandboxPath = Path.GetFullPath(_options.Value.FileSystem.SandboxPath);
        var fullPath = Path.GetFullPath(Path.Combine(sandboxPath, path));

        if (!fullPath.StartsWith(sandboxPath, StringComparison.OrdinalIgnoreCase))
            return new McpToolResult(false, string.Empty, "Path traversal not allowed");

        try
        {
            return operation switch
            {
                "write" => await WriteAsync(fullPath, path, parameters, cancellationToken),
                "read" => await ReadAsync(fullPath, path, cancellationToken),
                "list" => ListFiles(sandboxPath, fullPath),
                _ => new McpToolResult(false, string.Empty, $"Unknown operation: {operation}. Use write, read, or list.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FileSystemTool {Operation} failed for path '{Path}'", operation, path);
            return new McpToolResult(false, string.Empty, $"FileSystem error: {ex.Message}");
        }
    }

    private async Task<McpToolResult> WriteAsync(
        string fullPath,
        string relativePath,
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("content", out var content))
            return new McpToolResult(false, string.Empty, "Missing required parameter: content (for write operation)");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);

        var bytes = System.Text.Encoding.UTF8.GetByteCount(content);
        _logger.LogInformation("FileSystemTool wrote {Bytes} bytes to '{Path}'", bytes, relativePath);
        return new McpToolResult(true, $"File written: {relativePath} ({bytes} bytes)");
    }

    private static async Task<McpToolResult> ReadAsync(
        string fullPath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
            return new McpToolResult(false, string.Empty, $"File not found: {relativePath}");

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return new McpToolResult(true, content);
    }

    private static McpToolResult ListFiles(string sandboxPath, string fullPath)
    {
        var directory = Directory.Exists(fullPath) ? fullPath : sandboxPath;
        if (!Directory.Exists(directory))
            return new McpToolResult(true, "(empty workspace)");

        var files = Directory.GetFiles(directory)
            .Select(f => Path.GetRelativePath(sandboxPath, f));
        return new McpToolResult(true, string.Join('\n', files));
    }

    private void EnsureSandboxExists()
    {
        try
        {
            Directory.CreateDirectory(_options.Value.FileSystem.SandboxPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create sandbox directory '{Path}'",
                _options.Value.FileSystem.SandboxPath);
        }
    }
}
