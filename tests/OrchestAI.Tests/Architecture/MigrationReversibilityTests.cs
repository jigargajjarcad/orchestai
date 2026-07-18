using FluentAssertions;

namespace OrchestAI.Tests.Architecture;

// Enforces the migration-reversibility policy from ADR-016: every migration's Down() must either
// perform real migrationBuilder work (purely additive, structurally reversible) or throw
// NotSupportedException with a documented reason (irreversible — data transformation, destructive
// change). All 13 migrations that exist as of this test's introduction are purely additive and
// already have EF-generated, fully-reversible Down() bodies (confirmed by reading every one) — this
// test exists to catch the FIRST future migration that ships an empty or thoughtless Down(), not
// because any current migration violates the policy.
public sealed class MigrationReversibilityTests
{
    [Fact]
    public void EveryMigration_DeclaresExplicitDownBehavior()
    {
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).Equals("AppDbContextModelSnapshot.cs", StringComparison.Ordinal))
            .ToList();

        migrationFiles.Should().NotBeEmpty("the migrations source directory must resolve to real files");

        var violations = new List<string>();
        foreach (var file in migrationFiles)
        {
            var downBody = ExtractDownBody(File.ReadAllText(file));

            var hasRealWork = downBody.Contains("migrationBuilder.", StringComparison.Ordinal);
            var hasDocumentedThrow = downBody.Contains("throw new NotSupportedException(", StringComparison.Ordinal);

            if (!hasRealWork && !hasDocumentedThrow)
                violations.Add(Path.GetFileName(file));
        }

        violations.Should().BeEmpty(
            "every migration's Down() must either perform real migrationBuilder work (reversible) " +
            "or throw NotSupportedException with a documented reason (irreversible) — see ADR-016. " +
            $"Violating files: {string.Join(", ", violations)}");
    }

    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrchestAI.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException(
                "Could not locate repository root (OrchestAI.sln) from " + AppContext.BaseDirectory);

        return Path.Combine(dir.FullName, "src", "OrchestAI.Infrastructure", "Migrations");
    }

    private static string ExtractDownBody(string source)
    {
        const string marker = "protected override void Down(MigrationBuilder migrationBuilder)";
        var startIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (startIndex < 0)
            return string.Empty;

        var braceStart = source.IndexOf('{', startIndex);
        var depth = 0;
        var i = braceStart;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        return source.Substring(braceStart, i - braceStart + 1);
    }
}
