using System.Reflection;
using System.Text.RegularExpressions;

namespace E4A.PostGuard.Tests;

/// <summary>
/// <c>E4A.PostGuard</c> has no version of its own: it is published on every <c>pg-core</c>
/// release, at <c>pg-core</c>'s version, with the <c>pg-ffi</c> built from the same commit. The
/// csproj reads that version out of <c>pg-core/Cargo.toml</c> with a regular expression, and this
/// test holds the result against the manifest's <c>[package]</c> table, so a reordered manifest
/// or a hand-written <c>&lt;Version&gt;</c> cannot ship a different number.
/// </summary>
public class PgCoreVersionTests
{
    [Fact]
    public void AssemblyVersion_IsPgCoresVersion()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "pg-core", "Cargo.toml"));
        var package = Regex.Match(manifest, @"(?ms)^\[package\]\s*$(.*?)(?=^\[|\z)");
        Assert.True(package.Success, "pg-core/Cargo.toml has no [package] table");
        var version = Regex.Match(package.Groups[1].Value, @"(?m)^version\s*=\s*""([^""]+)""");
        Assert.True(version.Success, "pg-core/Cargo.toml's [package] table has no version");

        var informational = typeof(PostGuard).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(informational);

        // The SDK appends "+<commit>" to the informational version.
        Assert.Equal(version.Groups[1].Value, informational!.Split('+')[0]);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "pg-core", "Cargo.toml")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
