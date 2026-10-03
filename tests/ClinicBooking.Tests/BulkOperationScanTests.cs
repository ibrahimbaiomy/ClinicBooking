using System.Text.RegularExpressions;

namespace ClinicBooking.Tests;

/// <summary>
/// ExecuteUpdate/ExecuteDelete bypass the save interceptor, so they would skip audit
/// stamps and turn a soft delete into a real one (D35). They are banned in src/.
/// </summary>
public partial class BulkOperationScanTests
{
    [Fact]
    public void Source_does_not_use_ExecuteUpdate_or_ExecuteDelete()
    {
        var violations = FindViolations(Path.Combine(FindRepositoryRoot(), "src"));

        Assert.True(
            violations.Count == 0,
            "ExecuteUpdate/ExecuteDelete are not allowed (they bypass the audit/soft-delete interceptor):"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Scanner_reports_file_and_line_of_a_violation()
    {
        var directory = Directory.CreateTempSubdirectory("bulk-scan");
        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Bad.cs"),
                "class Bad\n{\n    void M(DbContext db)\n    {\n        db.Specialties.ExecuteDeleteAsync();\n    }\n}\n");
            File.WriteAllText(Path.Combine(directory.FullName, "Good.cs"), "class Good { }\n");

            var violations = FindViolations(directory.FullName);

            var violation = Assert.Single(violations);
            Assert.Contains("Bad.cs:5:", violation);
            Assert.Contains("ExecuteDeleteAsync", violation);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static List<string> FindViolations(string root)
    {
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var separator = Path.DirectorySeparatorChar;
            if (file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}"))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (BulkCall().IsMatch(lines[i]))
                {
                    violations.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        return violations;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClinicBooking.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("ClinicBooking.sln not found above the test output directory.");
    }

    [GeneratedRegex(@"\.\s*Execute(Update|Delete)(Async)?\s*[(<]")]
    private static partial Regex BulkCall();
}
