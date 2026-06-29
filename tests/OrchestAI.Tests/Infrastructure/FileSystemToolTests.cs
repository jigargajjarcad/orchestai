using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Tools;

namespace OrchestAI.Tests.Infrastructure;

public sealed class FileSystemToolTests : IDisposable
{
    private readonly string _sandboxPath;
    private readonly FileSystemTool _tool;

    public FileSystemToolTests()
    {
        _sandboxPath = Path.Combine(Path.GetTempPath(), $"orchestai-test-{Guid.NewGuid():N}");

        var options = Options.Create(new ToolOptions
        {
            FileSystem = new FileSystemOptions { SandboxPath = _sandboxPath }
        });
        _tool = new FileSystemTool(options, NullLogger<FileSystemTool>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandboxPath))
            Directory.Delete(_sandboxPath, recursive: true);
    }

    [Fact]
    public void ToolName_IsFileWrite()
    {
        _tool.ToolName.Should().Be("file_write");
    }

    [Fact]
    public void GetInputSchema_RequiresOperationAndPath()
    {
        var schema = _tool.GetInputSchema();
        schema.Required.Should().Contain("operation");
        schema.Required.Should().Contain("path");
        schema.Properties.Should().ContainKey("operation");
        schema.Properties.Should().ContainKey("path");
        schema.Properties.Should().ContainKey("content");
    }

    [Fact]
    public async Task ExecuteAsync_WriteOperation_CreatesFileAndReturnsSuccess()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "write",
            ["path"] = "hello.txt",
            ["content"] = "Hello, World!"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("hello.txt");
        result.Output.Should().Contain("bytes");
        File.Exists(Path.Combine(_sandboxPath, "hello.txt")).Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(_sandboxPath, "hello.txt"))).Should().Be("Hello, World!");
    }

    [Fact]
    public async Task ExecuteAsync_WriteOperation_CreatesSubdirectories()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "write",
            ["path"] = "subdir/nested/file.txt",
            ["content"] = "nested content"
        });

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_sandboxPath, "subdir", "nested", "file.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ReadExistingFile_ReturnsContent()
    {
        var filePath = Path.Combine(_sandboxPath, "read-me.txt");
        await File.WriteAllTextAsync(filePath, "test content");

        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "read",
            ["path"] = "read-me.txt"
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("test content");
    }

    [Fact]
    public async Task ExecuteAsync_ReadNonExistentFile_ReturnsFailure()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "read",
            ["path"] = "does-not-exist.txt"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_ListOperation_ReturnsFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_sandboxPath, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(_sandboxPath, "b.txt"), "b");

        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "list",
            ["path"] = "."
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("a.txt");
        result.Output.Should().Contain("b.txt");
    }

    [Fact]
    public async Task ExecuteAsync_PathWithDoubleDot_ReturnsFailure()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "read",
            ["path"] = "../escape.txt"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("traversal");
    }

    [Fact]
    public async Task ExecuteAsync_RootedPath_ReturnsFailure()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "read",
            ["path"] = "/etc/passwd"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("traversal");
    }

    [Fact]
    public async Task ExecuteAsync_MissingOperation_ReturnsFailure()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["path"] = "file.txt"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("operation");
    }

    [Fact]
    public async Task ExecuteAsync_MissingPath_ReturnsFailure()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "read"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("path");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownOperation_ReturnsFailure()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "delete",
            ["path"] = "file.txt"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown operation");
    }

    [Fact]
    public async Task ExecuteAsync_WriteWithoutContent_ReturnsFailure()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["operation"] = "write",
            ["path"] = "file.txt"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("content");
    }
}
