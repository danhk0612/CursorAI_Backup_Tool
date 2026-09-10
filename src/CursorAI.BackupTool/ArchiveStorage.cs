using System.IO.Compression;

namespace CursorAI.BackupTool;

internal sealed record ArchiveWriteProgress(long FilesProcessed, long BytesProcessed);

internal static class ArchiveStorage
{
    public static Task CreateAsync(
        string sourceRoot,
        string archivePath,
        IEnumerable<string> excludedDirectories,
        IProgress<ArchiveWriteProgress>? progress = null)
        => Task.Run(() => Create(sourceRoot, archivePath, excludedDirectories, progress));

    public static Task ExtractAsync(string archivePath, string destinationRoot)
        => Task.Run(() => Extract(archivePath, destinationRoot));

    public static bool ContainsPath(string archivePath, string relativePath)
    {
        var prefix = relativePath.Replace('\\', '/').Trim('/');
        if (prefix.Length == 0) return false;
        prefix += "/";

        using var archive = ZipFile.OpenRead(archivePath);
        return archive.Entries.Any(entry =>
            entry.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static void Create(
        string sourceRoot,
        string archivePath,
        IEnumerable<string> excludedDirectories,
        IProgress<ArchiveWriteProgress>? progress)
    {
        var excludes = excludedDirectories.Select(NormalizeDirectory).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        using var output = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        long filesProcessed = 0;
        long bytesProcessed = 0;
        long bytesSinceFlush = 0;

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

                var info = new FileInfo(file);
                var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');

                // The archive is primarily a file-count container, not a compression feature.
                // NoCompression avoids spending minutes deflating extension trees made of many small files.
                var entry = archive.CreateEntry(relative, CompressionLevel.NoCompression);
                entry.LastWriteTime = info.LastWriteTime;

                using var source = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 1024,
                    FileOptions.SequentialScan);
                using var target = entry.Open();
                source.CopyTo(target, 1024 * 1024);

                filesProcessed++;
                bytesProcessed += info.Length;
                bytesSinceFlush += info.Length;

                if (bytesSinceFlush >= 64L * 1024 * 1024)
                {
                    target.Flush();
                    output.Flush();
                    bytesSinceFlush = 0;
                }

                if (filesProcessed % 100 == 0 || bytesSinceFlush == 0)
                    progress?.Report(new ArchiveWriteProgress(filesProcessed, bytesProcessed));
            }
        }

        progress?.Report(new ArchiveWriteProgress(filesProcessed, bytesProcessed));
        archive.Dispose();
        output.Flush();
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
