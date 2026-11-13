using System.IO.Compression;
using E4A.PostGuard.Models;

namespace E4A.PostGuard.Zip;

internal static class ZipHelper
{
    public static byte[] CreateZip(IReadOnlyList<PgFile> files)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                var entryName = SanitizeEntryName(file.Name);

                // Stripping directory components can collapse distinct inputs
                // (e.g. "a/x.txt" and "b/x.txt") to the same entry name. ZipArchive
                // silently allows duplicates, which causes one entry to overwrite the
                // other on extraction, so reject the collision explicitly.
                if (!usedNames.Add(entryName))
                {
                    throw new ArgumentException(
                        $"Duplicate ZIP entry name '{entryName}' (from '{file.Name}').",
                        nameof(files));
                }

                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                file.Content.CopyTo(entryStream);
            }
        }

        return ms.ToArray();
    }

    // Strip any directory components so traversal sequences (e.g. "../../") cannot be
    // embedded as ZIP entry names in produced archives. Path.GetFileName is
    // platform-dependent — on Linux/macOS it does not treat '\' as a separator — but
    // these archives are consumed cross-platform (extracted on Windows, where '\' IS a
    // separator). So split on BOTH separators explicitly and keep only the last segment.
    private static string SanitizeEntryName(string name)
    {
        var entryName = name;
        var lastSeparator = entryName.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
        {
            entryName = entryName[(lastSeparator + 1)..];
        }

        if (string.IsNullOrEmpty(entryName) || entryName == "." || entryName == "..")
        {
            throw new ArgumentException(
                $"File name '{name}' does not resolve to a valid entry name.",
                nameof(name));
        }

        return entryName;
    }
}
