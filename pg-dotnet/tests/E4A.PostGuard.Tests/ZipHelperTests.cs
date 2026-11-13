using System.IO.Compression;
using System.Text;
using E4A.PostGuard.Models;
using E4A.PostGuard.Zip;

namespace E4A.PostGuard.Tests;

public class ZipHelperTests
{
    private static PgFile File(string name, string content) =>
        new(name, new MemoryStream(Encoding.UTF8.GetBytes(content)));

    private static Dictionary<string, string> ReadZip(byte[] zip)
    {
        var entries = new Dictionary<string, string>();
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            entries[entry.FullName] = reader.ReadToEnd();
        }
        return entries;
    }

    [Fact]
    public void SingleFile_RoundTrips()
    {
        var zip = ZipHelper.CreateZip([File("hello.txt", "world")]);

        var entries = ReadZip(zip);
        Assert.Single(entries);
        Assert.Equal("world", entries["hello.txt"]);
    }

    [Fact]
    public void MultipleFiles_AllPresentWithContent()
    {
        var zip = ZipHelper.CreateZip([
            File("a.txt", "alpha"),
            File("dir/b.txt", "beta"),
            File("c.bin", "gamma"),
        ]);

        var entries = ReadZip(zip);
        Assert.Equal(3, entries.Count);
        Assert.Equal("alpha", entries["a.txt"]);
        // Directory components are stripped, so "dir/b.txt" is stored as "b.txt".
        Assert.Equal("beta", entries["b.txt"]);
        Assert.Equal("gamma", entries["c.bin"]);
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("dir/nested/file.txt", "file.txt")]
    [InlineData("/absolute/path.txt", "path.txt")]
    // Windows-style separators must be stripped too: these archives are consumed
    // cross-platform, so backslash traversal must not survive creation on Linux/macOS.
    [InlineData("..\\..\\etc\\passwd", "passwd")]
    [InlineData("dir\\nested\\file.txt", "file.txt")]
    [InlineData("C:\\Windows\\evil.txt", "evil.txt")]
    [InlineData("mixed/dir\\file.txt", "file.txt")]
    public void SanitizesEntryNames_StrippingDirectoryComponents(string name, string expected)
    {
        var zip = ZipHelper.CreateZip([File(name, "payload")]);

        var entries = ReadZip(zip);
        var entryName = Assert.Single(entries.Keys);
        Assert.Equal(expected, entryName);
        Assert.DoesNotContain("..", entryName);
        Assert.Equal("payload", entries[entryName]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("dir/")]
    [InlineData("../")]
    [InlineData("dir\\")]
    [InlineData("..")]
    [InlineData(".")]
    public void ThrowsArgumentException_WhenNameHasNoFileComponent(string name)
    {
        Assert.Throws<ArgumentException>(() => ZipHelper.CreateZip([File(name, "payload")]));
    }

    [Fact]
    public void ThrowsArgumentException_WhenSanitizedNamesCollide()
    {
        // "a/x.txt" and "b/x.txt" both flatten to "x.txt"; ZipArchive would silently
        // allow the duplicate and lose one entry on extraction, so reject it.
        Assert.Throws<ArgumentException>(() => ZipHelper.CreateZip([
            File("a/x.txt", "first"),
            File("b/x.txt", "second"),
        ]));
    }

    [Fact]
    public void EmptyFileList_ProducesValidEmptyZip()
    {
        var zip = ZipHelper.CreateZip([]);

        Assert.NotEmpty(zip); // a valid (empty) zip still has an end-of-central-directory record
        Assert.Empty(ReadZip(zip));
    }

    [Fact]
    public void PreservesEntryNames_IncludingDuplicatesContent()
    {
        var binary = new byte[256];
        for (var i = 0; i < binary.Length; i++)
        {
            binary[i] = (byte)i;
        }

        var zip = ZipHelper.CreateZip([new PgFile("payload.bin", new MemoryStream(binary))]);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("payload.bin", entry.FullName);

        using var ms = new MemoryStream();
        entry.Open().CopyTo(ms);
        Assert.Equal(binary, ms.ToArray());
    }
}
