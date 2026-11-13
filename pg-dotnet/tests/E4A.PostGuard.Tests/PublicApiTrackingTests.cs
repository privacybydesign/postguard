using System.Reflection;
using System.Xml.Linq;

namespace E4A.PostGuard.Tests;

/// <summary>
/// The public surface is tracked in <c>src/PublicAPI.Shipped.txt</c> and
/// <c>src/PublicAPI.Unshipped.txt</c> and guarded by Microsoft.CodeAnalysis.PublicApiAnalyzers.
/// The analyzer only guards what it is wired up to see, so these tests cover the wiring itself
/// and check the tracked types against the assembly that ships.
/// </summary>
public class PublicApiTrackingTests
{
    [Fact]
    public void EveryExportedType_IsDeclaredInThePublicApiFiles()
    {
        var declared = DeclaredEntries();
        var declaredTypes = declared.Where(entry => !entry.Contains("->")).ToHashSet(StringComparer.Ordinal);

        var exported = typeof(PostGuard).Assembly.GetExportedTypes()
            .Select(DeclaredName)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = exported.Except(declaredTypes).Order(StringComparer.Ordinal).ToArray();
        var stale = declaredTypes.Except(exported).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            undeclared.Length == 0,
            $"public types missing from the PublicAPI files: {string.Join(", ", undeclared)}");
        Assert.True(
            stale.Length == 0,
            $"PublicAPI files declare types the assembly no longer exports: {string.Join(", ", stale)}");
    }

    [Theory]
    [InlineData("PublicAPI.Shipped.txt")]
    [InlineData("PublicAPI.Unshipped.txt")]
    public void ApiFile_TracksNullability(string fileName)
    {
        // Without the header the analyzer records every reference type as oblivious, so a
        // nullable-annotation change would slip through unreviewed. Dropping it from the
        // unshipped file still builds green, so nothing but this test catches that.
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "src", fileName));
        Assert.Equal("#nullable enable", lines.FirstOrDefault());
    }

    [Fact]
    public void Csproj_KeepsTheAnalyzerWiredUpAndFailingTheBuild()
    {
        // Parsed rather than substring-matched: reformatting an element, or moving
        // WarningsAsErrors into a second PropertyGroup, leaves the wiring fully intact
        // and must not red-light this test.
        var csproj = XDocument.Load(Path.Combine(RepoRoot(), "src", "E4A.PostGuard.csproj"));

        Assert.Contains("Microsoft.CodeAnalysis.PublicApiAnalyzers", Includes(csproj, "PackageReference"));
        Assert.Contains("PublicAPI.Shipped.txt", Includes(csproj, "AdditionalFiles"));
        Assert.Contains("PublicAPI.Unshipped.txt", Includes(csproj, "AdditionalFiles"));

        // Left as warnings, an undeclared API change scrolls past in a green build.
        var warningsAsErrors = string.Join(';', csproj.Descendants("WarningsAsErrors").Select(element => element.Value));
        Assert.Contains("RS0016", warningsAsErrors, StringComparison.Ordinal);
        Assert.Contains("RS0017", warningsAsErrors, StringComparison.Ordinal);
    }

    /// <summary>The <c>Include</c> attribute of every <paramref name="elementName"/> item in the project.</summary>
    private static string[] Includes(XDocument csproj, string elementName) =>
        csproj.Descendants(elementName)
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .ToArray();

    /// <summary>Shipped plus unshipped additions, minus the entries marked <c>*REMOVED*</c>.</summary>
    private static HashSet<string> DeclaredEntries()
    {
        const string removedPrefix = "*REMOVED*";
        var root = Path.Combine(RepoRoot(), "src");

        var entries = new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" }
            .SelectMany(name => File.ReadAllLines(Path.Combine(root, name)))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        var declared = entries
            .Where(line => !line.StartsWith(removedPrefix, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var removed in entries.Where(line => line.StartsWith(removedPrefix, StringComparison.Ordinal)))
        {
            declared.Remove(removed[removedPrefix.Length..]);
        }

        return declared;
    }

    /// <summary>The name a type is written under in the PublicAPI files.</summary>
    private static string DeclaredName(Type type)
    {
        var name = type.FullName!.Replace('+', '.');
        var arity = name.IndexOf('`');
        return arity < 0
            ? name
            : $"{name[..arity]}<{string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name))}>";
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "E4A.PostGuard.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
