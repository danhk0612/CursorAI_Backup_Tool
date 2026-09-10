using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CursorAI.BackupTool;

internal sealed class BackupService
{
    private const string RoamingArchiveName = "roaming.zip";
    private const string LocalArchiveName = "local.zip";
    private const string UserArchiveName = "cursor-user.zip";

    private readonly string _backupRoot = Path.Combine(AppContext.BaseDirectory, "backups");
    private readonly string _roamingCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor");
    private readonly string _localCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cursor");
    private readonly string _userCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor");

    public string BackupRoot => _backupRoot;

    public bool IsCursorRunning()
    {
        var processes = Process.GetProcessesByName("Cursor");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public void StopCursor()
    {
        foreach (var process in Process.GetProcessesByName("Cursor"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    public async Task<OperationResult> CreateBackupAsync(bool includeWorkspaceStorage, IProgress<OperationProgress>? progress = null)
    {
        Directory.CreateDirectory(_backupRoot);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var finalPath = Path.Combine(_backupRoot, stamp);
        var tempPath = finalPath + ".incomplete";

        if (Directory.Exists(tempPath) || Directory.Exists(finalPath))
            return new(false, "같은 시각의 백업 폴더가 이미 존재합니다.");

        progress?.Report(new(5, "백업 폴더 준비 중..."));
        Directory.CreateDirectory(tempPath);

        if (!Directory.Exists(_roamingCursor))
            return FailIncomplete(tempPath, $"필수 Cursor Roaming 경로가 없습니다: {_roamingCursor}");

        var warnings = new List<string>();
        var infos = new List<string>();

        try
        {
            progress?.Report(new(15, includeWorkspaceStorage
                ? "Roaming 아카이브 생성 중 (두 WorkspaceStorage 포함)..."
                : "Roaming 아카이브 생성 중 (두 WorkspaceStorage 제외)..."));
            await ArchiveStorage.CreateAsync(
                _roamingCursor,
                Path.Combine(tempPath, RoamingArchiveName),
                RoamingExcludes(includeWorkspaceStorage));

            if (Directory.Exists(_localCursor))
            {
                progress?.Report(new(45, "Local 아카이브 생성 중..."));
                await ArchiveStorage.CreateAsync(
                    _localCursor,
                    Path.Combine(tempPath, LocalArchiveName),
                    LocalExcludes());
            }
            else
            {
                infos.Add("Local Cursor 경로가 없어 해당 범위를 건너뛰었습니다.");
            }

            if (Directory.Exists(_userCursor))
            {
                progress?.Report(new(65, ".cursor 아카이브 생성 중 (extensions 실파일 포함)..."));
                await ArchiveStorage.CreateAsync(
                    _userCursor,
                    Path.Combine(tempPath, UserArchiveName),
                    new[] { Path.Combine(_userCursor, "user-data") });
            }
            else
            {
                warnings.Add("사용자 .cursor 경로가 없어 확장/프로젝트 데이터를 백업하지 못했습니다.");
            }

            progress?.Report(new(82, "Cursor 버전 및 확장 목록 확인 중..."));
            var install = FindCursorInstall();
            string? cursorVersion = null;

            if (install.ExecutablePath is not null)
            {
                try
                {
                    var fileVersion = FileVersionInfo.GetVersionInfo(install.ExecutablePath);
                    cursorVersion = fileVersion.ProductVersion ?? fileVersion.FileVersion;
                    if (!string.IsNullOrWhiteSpace(cursorVersion))
                        await File.WriteAllTextAsync(Path.Combine(tempPath, "cursor_version.txt"), cursorVersion, new UTF8Encoding(false));
                    else
                        infos.Add("Cursor 실행 파일의 버전 정보가 비어 있습니다.");
                }
                catch
                {
                    infos.Add("Cursor 실행 파일에서 버전 정보를 읽지 못했습니다.");
                }
            }
            else
            {
                infos.Add("Cursor 실행 파일을 찾지 못해 버전 기록을 건너뛰었습니다.");
            }

            if (install.CliPath is not null)
            {
                var extensions = await RunCursorCliAsync(install.CliPath, "--list-extensions");
                if (extensions.Success)
                    await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions.txt"), extensions.Output, new UTF8Encoding(false));
                else
                    infos.Add("확장 ID 목록을 가져오지 못했습니다. 확장 실파일 백업은 유지됩니다.");

                var versions = await RunCursorCliAsync(install.CliPath, "--list-extensions", "--show-versions");
                if (versions.Success)
                    await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions_with_versions.txt"), versions.Output, new UTF8Encoding(false));
                else
                    infos.Add("확장 버전 목록을 가져오지 못했습니다. 확장 실파일 백업은 유지됩니다.");
            }
            else
            {
                infos.Add("Cursor CLI를 찾지 못해 확장 목록 생성을 건너뛰었습니다. 확장 실파일 백업은 유지됩니다.");
            }

            progress?.Report(new(92, "v3 백업 메타데이터 기록 중..."));
            var info = new BackupInfo
            {
                BackupVersion = 3,
                StorageFormat = "archive-v3",
                CreatedAt = DateTimeOffset.Now,
                CursorVersion = cursorVersion,
                Type = includeWorkspaceStorage ? "full-ai" : "normal",
                WorkspaceStorageIncluded = includeWorkspaceStorage,
                Status = warnings.Count == 0 ? "SUCCESS" : "WARNING"
            };

            await WriteInfoAsync(tempPath, info);
            info.TotalBytes = GetTopLevelFileBytes(tempPath);
            await WriteInfoAsync(tempPath, info);

            progress?.Report(new(98, "백업 완료 처리 중..."));
            Directory.Move(tempPath, finalPath);
            progress?.Report(new(100, "백업 완료"));

            var message = new StringBuilder(warnings.Count == 0
                ? $"백업 완료: {stamp}"
                : $"백업 완료(경고): {stamp}");
            message.Append("\r\n형식: v3 archive");
            foreach (var warning in warnings) message.Append("\r\n경고: ").Append(warning);
            foreach (var text in infos) message.Append("\r\n안내: ").Append(text);
            return new(true, message.ToString());
        }
        catch (Exception ex)
        {
            return FailIncomplete(tempPath, ex.Message);
        }
    }

    public async Task<OperationResult> RestoreAsync(BackupRecord record, IProgress<OperationProgress>? progress = null)
    {
        if (record.IsIncomplete)
            return new(false, "완료되지 않은 백업은 복원할 수 없습니다.");

        var source = record.FullPath;
        var archiveV3 = record.IsArchiveV3 || File.Exists(Path.Combine(source, RoamingArchiveName));
        var sourceRoamingArchive = Path.Combine(source, RoamingArchiveName);
        var sourceRoamingFolder = Path.Combine(source, "Roaming", "Cursor");

        if (archiveV3)
        {
            if (!File.Exists(sourceRoamingArchive))
                return new(false, $"v3 백업에 필수 {RoamingArchiveName} 파일이 없습니다.");
        }
        else if (!Directory.Exists(sourceRoamingFolder))
        {
            return new(false, "선택한 백업에 필수 Roaming\\Cursor 데이터가 없습니다.");
        }

        Directory.CreateDirectory(_backupRoot);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var safetyTemp = Path.Combine(_backupRoot, $"pre-restore_{stamp}.incomplete");
        var safetyFinal = Path.Combine(_backupRoot, $"pre-restore_{stamp}");
        if (Directory.Exists(safetyTemp) || Directory.Exists(safetyFinal))
            return new(false, "같은 시각의 복원 안전 백업 폴더가 이미 존재합니다.");
        Directory.CreateDirectory(safetyTemp);

        try
        {
            progress?.Report(new(10, "현재 상태 안전 백업 아카이브 생성 중..."));
            var safety = await BackupCurrentStateAsync(safetyTemp);
            if (!safety.Success)
                return new(false, $"현재 상태 안전 백업 실패. 복원을 시작하지 않았습니다.\r\n{safety.Message}");

            var safetyInfo = new BackupInfo
            {
                BackupVersion = 3,
                StorageFormat = "archive-v3",
                CreatedAt = DateTimeOffset.Now,
                Type = "pre-restore",
                WorkspaceStorageIncluded = true,
                Status = "SUCCESS"
            };
            await WriteInfoAsync(safetyTemp, safetyInfo);
            safetyInfo.TotalBytes = GetTopLevelFileBytes(safetyTemp);
            await WriteInfoAsync(safetyTemp, safetyInfo);
            Directory.Move(safetyTemp, safetyFinal);

            var sourceLocalArchive = Path.Combine(source, LocalArchiveName);
            var sourceUserArchive = Path.Combine(source, UserArchiveName);
            var sourceLocalFolder = Path.Combine(source, "Local", "Cursor");
            var sourceUserFolder = Path.Combine(source, "User", ".cursor");

            var restoreLocal = archiveV3 ? File.Exists(sourceLocalArchive) : Directory.Exists(sourceLocalFolder);
            var restoreUser = archiveV3 ? File.Exists(sourceUserArchive) : Directory.Exists(sourceUserFolder);

            var sourceHasRootWorkspace = archiveV3
                ? record.WorkspaceStorageIncluded
                : Directory.Exists(Path.Combine(sourceRoamingFolder, "WorkspaceStorage"));
            var sourceHasUserWorkspace = archiveV3
                ? record.WorkspaceStorageIncluded
                : Directory.Exists(Path.Combine(sourceRoamingFolder, "User", "workspaceStorage"));
            var sourceHasUserData = !archiveV3 && restoreUser && Directory.Exists(Path.Combine(sourceUserFolder, "user-data"));

            var oldRoaming = _roamingCursor + $".cursor-backup-old-{stamp}";
            var oldLocal = _localCursor + $".cursor-backup-old-{stamp}";
            var oldUser = _userCursor + $".cursor-backup-old-{stamp}";
            var hadRoaming = false;
            var hadLocal = false;
            var hadUser = false;

            progress?.Report(new(30, "현재 Cursor 데이터 분리 중..."));
            try
            {
                hadRoaming = MoveAsideIfExists(_roamingCursor, oldRoaming);
                hadLocal = restoreLocal && MoveAsideIfExists(_localCursor, oldLocal);
                hadUser = restoreUser && MoveAsideIfExists(_userCursor, oldUser);
            }
            catch
            {
                RestoreMovedAside(hadUser, oldUser, _userCursor);
                RestoreMovedAside(hadLocal, oldLocal, _localCursor);
                RestoreMovedAside(hadRoaming, oldRoaming, _roamingCursor);
                throw;
            }

            var preserveRootWorkspace = hadRoaming && !sourceHasRootWorkspace
                && Directory.Exists(Path.Combine(oldRoaming, "WorkspaceStorage"));
            var preserveUserWorkspace = hadRoaming && !sourceHasUserWorkspace
                && Directory.Exists(Path.Combine(oldRoaming, "User", "workspaceStorage"));
            var preserveUserData = hadUser && restoreUser && !sourceHasUserData
                && Directory.Exists(Path.Combine(oldUser, "user-data"));

            var movedRootWorkspace = false;
            var movedUserWorkspace = false;
            var movedUserData = false;

            try
            {
                progress?.Report(new(45, archiveV3 ? "Roaming 아카이브 복원 중..." : "Roaming 데이터 복원 중..."));
                if (archiveV3)
                    await ArchiveStorage.ExtractAsync(sourceRoamingArchive, _roamingCursor);
                else
                    EnsureRoboSuccess(await RunRobocopyAsync(sourceRoamingFolder, _roamingCursor, Array.Empty<string>()), "Roaming 복원");

                if (restoreLocal)
                {
                    progress?.Report(new(62, archiveV3 ? "Local 아카이브 복원 중..." : "Local 데이터 복원 중..."));
                    if (archiveV3)
                        await ArchiveStorage.ExtractAsync(sourceLocalArchive, _localCursor);
                    else
                        EnsureRoboSuccess(await RunRobocopyAsync(sourceLocalFolder, _localCursor, Array.Empty<string>()), "Local 복원");
                }

                if (restoreUser)
                {
                    progress?.Report(new(75, archiveV3 ? ".cursor 아카이브 복원 중..." : ".cursor 사용자 데이터 복원 중..."));
                    if (archiveV3)
                        await ArchiveStorage.ExtractAsync(sourceUserArchive, _userCursor);
                    else
                        EnsureRoboSuccess(await RunRobocopyAsync(sourceUserFolder, _userCursor, Array.Empty<string>()), ".cursor 복원");
                }

                progress?.Report(new(88, "백업 제외 데이터 제자리 이동 중..."));
                if (preserveRootWorkspace)
                {
                    MoveDirectory(Path.Combine(oldRoaming, "WorkspaceStorage"), Path.Combine(_roamingCursor, "WorkspaceStorage"));
                    movedRootWorkspace = true;
                }
                if (preserveUserWorkspace)
                {
                    MoveDirectory(Path.Combine(oldRoaming, "User", "workspaceStorage"), Path.Combine(_roamingCursor, "User", "workspaceStorage"));
                    movedUserWorkspace = true;
                }
                if (preserveUserData)
                {
                    MoveDirectory(Path.Combine(oldUser, "user-data"), Path.Combine(_userCursor, "user-data"));
                    movedUserData = true;
                }
            }
            catch
            {
                progress?.Report(new(90, "복원 실패 - 기존 데이터 롤백 중..."));

                if (movedUserData)
                    MoveDirectoryIfExists(Path.Combine(_userCursor, "user-data"), Path.Combine(oldUser, "user-data"));
                if (movedUserWorkspace)
                    MoveDirectoryIfExists(Path.Combine(_roamingCursor, "User", "workspaceStorage"), Path.Combine(oldRoaming, "User", "workspaceStorage"));
                if (movedRootWorkspace)
                    MoveDirectoryIfExists(Path.Combine(_roamingCursor, "WorkspaceStorage"), Path.Combine(oldRoaming, "WorkspaceStorage"));

                DeleteIfExists(_roamingCursor);
                if (restoreLocal) DeleteIfExists(_localCursor);
                if (restoreUser) DeleteIfExists(_userCursor);

                RestoreMovedAside(hadRoaming, oldRoaming, _roamingCursor);
                RestoreMovedAside(hadLocal, oldLocal, _localCursor);
                RestoreMovedAside(hadUser, oldUser, _userCursor);
                throw;
            }

            progress?.Report(new(96, "임시 데이터 정리 중..."));
            DeleteIfExists(oldRoaming);
            DeleteIfExists(oldLocal);
            DeleteIfExists(oldUser);
            progress?.Report(new(100, "복원 완료"));

            return new(true,
                $"복원 완료: {record.Name}\r\n형식: {(archiveV3 ? "v3 archive" : "legacy folder")}\r\n안전 백업: {Path.GetFileName(safetyFinal)}");
        }
        catch (Exception ex)
        {
            var safetyName = Directory.Exists(safetyFinal) ? Path.GetFileName(safetyFinal) : Path.GetFileName(safetyTemp);
            return new(false, $"복원 실패: {ex.Message}\r\n복원 안전 백업 확인: {safetyName}");
        }
    }

    public Task<IReadOnlyList<BackupRecord>> GetBackupsAsync() => Task.Run<IReadOnlyList<BackupRecord>>(() =>
    {
        Directory.CreateDirectory(_backupRoot);
        var result = new List<BackupRecord>();

        foreach (var directory in Directory.EnumerateDirectories(_backupRoot).OrderByDescending(Path.GetFileName))
        {
            var name = Path.GetFileName(directory);
            var incomplete = name.EndsWith(".incomplete", StringComparison.OrdinalIgnoreCase);
            BackupInfo? info = null;
            var infoPath = Path.Combine(directory, "backup-info.json");
            if (File.Exists(infoPath))
            {
                try { info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(infoPath)); }
                catch { }
            }

            var hasV3Archive = File.Exists(Path.Combine(directory, RoamingArchiveName));
            var isArchiveV3 = (info?.BackupVersion ?? 0) >= 3
                && string.Equals(info?.StorageFormat, "archive-v3", StringComparison.OrdinalIgnoreCase)
                || hasV3Archive;

            long bytes;
            if ((info?.TotalBytes ?? 0) > 0)
                bytes = info!.TotalBytes;
            else if (isArchiveV3)
                bytes = GetTopLevelFileBytes(directory);
            else
                bytes = GetDirectorySize(directory);

            result.Add(new BackupRecord
            {
                Name = name,
                FullPath = directory,
                Type = info?.Type ?? (incomplete ? "incomplete" : "legacy"),
                Status = incomplete ? "INCOMPLETE" : info?.Status ?? (isArchiveV3 ? "UNKNOWN" : "LEGACY"),
                SizeText = FormatBytes(bytes),
                CreatedAt = info?.CreatedAt.LocalDateTime ?? Directory.GetCreationTime(directory),
                IsIncomplete = incomplete,
                BackupVersion = info?.BackupVersion ?? (isArchiveV3 ? 3 : 1),
                StorageFormat = info?.StorageFormat ?? (isArchiveV3 ? "archive-v3" : null),
                WorkspaceStorageIncluded = info?.WorkspaceStorageIncluded ?? false
            });
        }

        return result;
    });

    public OperationResult DeleteBackup(BackupRecord record)
    {
        try
        {
            if (!IsUnderBackupRoot(record.FullPath))
                return new(false, "백업 루트 밖의 폴더는 삭제할 수 없습니다.");
            Directory.Delete(record.FullPath, recursive: true);
            return new(true, $"삭제 완료: {record.Name}");
        }
        catch (Exception ex)
        {
            return new(false, $"삭제 실패: {ex.Message}");
        }
    }

    private async Task<OperationResult> BackupCurrentStateAsync(string destination)
    {
        try
        {
            if (!Directory.Exists(_roamingCursor))
                return new(false, "현재 필수 Roaming Cursor 경로가 없습니다.");

            await ArchiveStorage.CreateAsync(
                _roamingCursor,
                Path.Combine(destination, RoamingArchiveName),
                RoamingExcludes(includeWorkspace: true));

            if (Directory.Exists(_localCursor))
            {
                await ArchiveStorage.CreateAsync(
                    _localCursor,
                    Path.Combine(destination, LocalArchiveName),
                    LocalExcludes());
            }

            if (Directory.Exists(_userCursor))
            {
                await ArchiveStorage.CreateAsync(
                    _userCursor,
                    Path.Combine(destination, UserArchiveName),
                    new[] { Path.Combine(_userCursor, "user-data") });
            }

            return new(true, "OK");
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }

    private IEnumerable<string> RoamingExcludes(bool includeWorkspace)
    {
        var list = new List<string>
        {
            Path.Combine(_roamingCursor, "User", "WebStorage"),
            Path.Combine(_roamingCursor, "User", "CachedData"),
            Path.Combine(_roamingCursor, "User", "History"),
            Path.Combine(_roamingCursor, "User", "logs"),
            Path.Combine(_roamingCursor, "logs"),
            Path.Combine(_roamingCursor, "Cache")
        };

        if (!includeWorkspace)
        {
            list.Add(Path.Combine(_roamingCursor, "WorkspaceStorage"));
            list.Add(Path.Combine(_roamingCursor, "User", "workspaceStorage"));
        }

        return list;
    }

    private IEnumerable<string> LocalExcludes() => new[]
    {
        Path.Combine(_localCursor, "Cache"),
        Path.Combine(_localCursor, "GPUCache"),
        Path.Combine(_localCursor, "Code Cache"),
        Path.Combine(_localCursor, "Service Worker"),
        Path.Combine(_localCursor, "Crashpad")
    };

    private static async Task<int> RunRobocopyAsync(string source, string destination, IEnumerable<string> excludedDirectories)
    {
        Directory.CreateDirectory(destination);
        var psi = new ProcessStartInfo("robocopy.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { source, destination, "/E", "/R:1", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS" })
            psi.ArgumentList.Add(arg);

        var excludes = excludedDirectories.ToArray();
        if (excludes.Length > 0)
        {
            psi.ArgumentList.Add("/XD");
            foreach (var exclude in excludes) psi.ArgumentList.Add(exclude);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Robocopy를 시작할 수 없습니다.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
        return process.ExitCode;
    }

    private static async Task<(bool Success, string Output)> RunCursorCliAsync(string command, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");

            var commandLine = new StringBuilder();
            commandLine.Append("call \"").Append(command).Append("\"");
            foreach (var arg in args) commandLine.Append(' ').Append(arg);
            psi.ArgumentList.Add(commandLine.ToString());

            using var process = Process.Start(psi);
            if (process is null) return (false, string.Empty);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
            return (process.ExitCode == 0, stdout.Result.Trim());
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    private static CursorInstall FindCursorInstall()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Cursor"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cursor"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Cursor")
        };

        string? executable = null;
        string? cli = null;
        foreach (var root in roots)
        {
            var exeCandidate = Path.Combine(root, "Cursor.exe");
            var cliCandidate = Path.Combine(root, "resources", "app", "bin", "cursor.cmd");
            if (executable is null && File.Exists(exeCandidate)) executable = exeCandidate;
            if (cli is null && File.Exists(cliCandidate)) cli = cliCandidate;
            if (executable is not null && cli is not null) break;
        }

        if (cli is null)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(part.Trim().Trim('"'), "cursor.cmd");
                    if (!File.Exists(candidate)) continue;
                    cli = candidate;
                    break;
                }
                catch { }
            }
        }

        return new CursorInstall(executable, cli);
    }

    private static Task WriteInfoAsync(string folder, BackupInfo info)
        => File.WriteAllTextAsync(
            Path.Combine(folder, "backup-info.json"),
            JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

    private static OperationResult FailIncomplete(string tempPath, string message)
        => new(false, $"백업 실패: {message}\r\n불완전 백업 유지: {Path.GetFileName(tempPath)}");

    private static bool MoveAsideIfExists(string source, string destination)
    {
        if (!Directory.Exists(source)) return false;
        Directory.Move(source, destination);
        return true;
    }

    private static void RestoreMovedAside(bool moved, string oldPath, string originalPath)
    {
        if (moved && Directory.Exists(oldPath) && !Directory.Exists(originalPath))
            Directory.Move(oldPath, originalPath);
    }

    private static void MoveDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
    }

    private static void MoveDirectoryIfExists(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(source, destination);
    }

    private static void EnsureRoboSuccess(int code, string operation)
    {
        if (code >= 8) throw new IOException($"{operation} 실패 (Robocopy {code}).");
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private bool IsUnderBackupRoot(string path)
    {
        var root = Path.GetFullPath(_backupRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static long GetTopLevelFileBytes(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).Sum(x => new FileInfo(x).Length); }
        catch { return 0; }
    }

    private static long GetDirectorySize(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length); }
        catch { return 0; }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}