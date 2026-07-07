using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using OrchestAI.Infrastructure.Tools;

namespace OrchestAI.Tests.Infrastructure;

public sealed class DatabaseToolTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> connectionStrings)
    {
        var data = connectionStrings.ToDictionary(kvp => $"ConnectionStrings:{kvp.Key}", kvp => kvp.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Fact]
    public void CreateConnection_SqlServerConnectionString_UsesSqlConnection()
    {
        var connection = AdoDatabaseQueryExecutor.CreateConnection(
            "Server=myserver.database.windows.net;Database=mydb;User Id=sa;Password=secret;");

        connection.Should().BeOfType<SqlConnection>();
    }

    [Fact]
    public void CreateConnection_DataSourceConnectionString_UsesSqlConnection()
    {
        var connection = AdoDatabaseQueryExecutor.CreateConnection(
            "Data Source=myserver;Initial Catalog=mydb;User ID=sa;Password=secret;");

        connection.Should().BeOfType<SqlConnection>();
    }

    [Fact]
    public void CreateConnection_PostgresConnectionString_UsesNpgsqlConnection()
    {
        var connection = AdoDatabaseQueryExecutor.CreateConnection(
            "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme");

        connection.Should().BeOfType<NpgsqlConnection>();
    }

    [Theory]
    [InlineData("INSERT INTO users (name) VALUES ('x')")]
    [InlineData("UPDATE users SET name = 'x'")]
    [InlineData("DELETE FROM users")]
    [InlineData("DROP TABLE users")]
    [InlineData("CREATE TABLE x (id int)")]
    [InlineData("ALTER TABLE users ADD COLUMN x int")]
    [InlineData("TRUNCATE TABLE users")]
    [InlineData("EXEC sp_helpdb")]
    [InlineData("select * from users; exec sp_evil")]
    public void IsWriteQuery_WriteOrDdlKeywords_ReturnsTrue(string query)
    {
        DatabaseTool.IsWriteQuery(query).Should().BeTrue();
    }

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("SELECT TOP 10 id, name FROM users WHERE active = 1")]
    [InlineData("select id from orders")]
    public void IsWriteQuery_ReadOnlySelect_ReturnsFalse(string query)
    {
        DatabaseTool.IsWriteQuery(query).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WriteQuery_RejectedWithoutInvokingExecutor()
    {
        var executorMock = new Mock<IDatabaseQueryExecutor>();
        var config = BuildConfiguration(new() { ["default"] = "Host=localhost;Database=orchestai;" });
        var tool = new DatabaseTool(config, executorMock.Object, NullLogger<DatabaseTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["query"] = "DELETE FROM users" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("read-only");
        executorMock.Verify(
            e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnectionString_ReturnsError()
    {
        var executorMock = new Mock<IDatabaseQueryExecutor>();
        var config = BuildConfiguration(new());
        var tool = new DatabaseTool(config, executorMock.Object, NullLogger<DatabaseTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["query"] = "SELECT * FROM users" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not configured");
    }

    [Fact]
    public async Task ExecuteAsync_QueryExceeds500Rows_TruncatedWithNote()
    {
        var executorMock = new Mock<IDatabaseQueryExecutor>();
        executorMock
            .Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), 30, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DatabaseQueryResult(true, "[...]", 500, true, null));

        var config = BuildConfiguration(new() { ["default"] = "Host=localhost;Database=orchestai;" });
        var tool = new DatabaseTool(config, executorMock.Object, NullLogger<DatabaseTool>.Instance);

        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["query"] = "SELECT * FROM big_table" });

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("truncated to 500 rows");
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutFromExecutor_ReturnsErrorResultWithoutThrowing()
    {
        var executorMock = new Mock<IDatabaseQueryExecutor>();
        executorMock
            .Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DatabaseQueryResult(false, null, 0, false, "Query timed out after 30 seconds"));

        var config = BuildConfiguration(new() { ["default"] = "Host=localhost;Database=orchestai;" });
        var tool = new DatabaseTool(config, executorMock.Object, NullLogger<DatabaseTool>.Instance);

        var act = async () => await tool.ExecuteAsync(new Dictionary<string, string> { ["query"] = "SELECT * FROM slow_view" });

        var result = await act.Should().NotThrowAsync();
        result.Subject.Success.Should().BeFalse();
        result.Subject.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task ExecuteAsync_NamedDatabaseParameter_UsesCorrespondingConnectionString()
    {
        string? capturedConnectionString = null;
        var executorMock = new Mock<IDatabaseQueryExecutor>();
        executorMock
            .Setup(e => e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, int, CancellationToken>((connStr, _, _, _, _) => capturedConnectionString = connStr)
            .ReturnsAsync(new DatabaseQueryResult(true, "[]", 0, false, null));

        var config = BuildConfiguration(new()
        {
            ["default"] = "Host=localhost;Database=orchestai;",
            ["sqlserver_demo"] = "Server=demo;Database=demo;User Id=sa;Password=secret;"
        });
        var tool = new DatabaseTool(config, executorMock.Object, NullLogger<DatabaseTool>.Instance);

        await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "SELECT * FROM users",
            ["database"] = "sqlserver_demo"
        });

        capturedConnectionString.Should().Contain("Server=demo");
    }
}
