using System.IO.Compression;

namespace CursorAI.BackupTool;

internal static class ArchiveStorage
{
    public static Task CreateAsync(string sourceRoot, string archivePath, IEnumerable<string> excludedDirectories)
        => Task.Run(() => Create(sourceRoot, archivePath, excludedDirectories));

    public static Task ExtractAsync(string archivePath, string destinationRoot)
        => Task.Run(() => Extract(archivePath, destinationRoot));

    private static void Create(string sourceRoot, string archivePath, IEnumerable<string> excludedDirectories)
    {
        var sourceFull = NormalizeDirectory(sourceRoot);
        var excludes = excludedDirectories.Select(NormalizeDirectory).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);

        var pending = new Stack<string>();
        pending.Push(sourceRoot);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (IsExcluded(directory, excludes)) continue;
                var info = new DirectoryInfo(directory);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                if (IsExcluded(file, excludes)) continue;
                var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
                var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                entry.LastWriteTime = File.GetLastWriteTime(file);

                using var source = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 1024,
                    FileOptions.SequentialScan);
                using var target = entry.Open();
                source.CopyTo(target, 1024 * 1024);
            }
        }
    }

    private static void Extract(string archivePath, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        var destinationFull = NormalizeDirectory(destinationRoot);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(destinationFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"백업 아카이브에 허용되지 않은 경로가 있습니다: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            try { File.SetLastWriteTime(destination, entry.LastWriteTime.LocalDateTime); } catch { }
        }
    }

    private static bool IsExcluded(string path, IReadOnlyList<string> excludes)
    {
        var full = Path.GetFullPath(path);
        foreach (var exclude in excludes)
        {
            var trimmed = exclude.TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), trimmed, StringComparison.OrdinalIgnoreCase)) return true;
            if (full.StartsWith(exclude, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string NormalizeDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}