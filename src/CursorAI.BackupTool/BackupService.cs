using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CursorAI.BackupTool;

internal sealed class BackupService
{
    private readonly string _backupRoot = Path.Combine(AppContext.BaseDirectory, "backups");
    private readonly string _roamingCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor");
    private readonly string _localCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cursor");
    private readonly string _userCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor");

    public string BackupRoot => _backupRoot;
    public bool IsCursorRunning() => Process.GetProcessesByName("Cursor").Length > 0;

    public void StopCursor()
    {
        foreach (var process in Process.GetProcessesByName("Cursor"))
        {
            try { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
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
        if (Directory.Exists(tempPath) || Directory.Exists(finalPath)) return new(false, "같은 시각의 백업 폴더가 이미 존재합니다.");

        progress?.Report(new(5, "백업 폴더 준비 중..."));
        Directory.CreateDirectory(tempPath);
        var warnings = new List<string>();
        try
        {
            if (Directory.Exists(_roamingCursor))
            {
                progress?.Report(new(15, includeWorkspaceStorage ? "Roaming 데이터 복사 중 (workspaceStorage 포함)..." : "Roaming 데이터 복사 중..."));
                var rc = await RunRobocopyAsync(_roamingCursor, Path.Combine(tempPath, "Roaming", "Cursor"), RoamingExcludes(includeWorkspaceStorage));
                if (rc >= 8) return FailIncomplete(tempPath, $"Roaming 데이터 복사 실패 (Robocopy {rc}).");
            }
            else warnings.Add("Roaming Cursor 경로가 없습니다.");

            if (Directory.Exists(_localCursor))
            {
                progress?.Report(new(50, "Local 데이터 복사 중..."));
                var rc = await RunRobocopyAsync(_localCursor, Path.Combine(tempPath, "Local", "Cursor"), LocalExcludes());
                if (rc >= 8) return FailIncomplete(tempPath, $"Local 데이터 복사 실패 (Robocopy {rc}).");
            }
            else warnings.Add("Local Cursor 경로가 없습니다.");

            if (Directory.Exists(_userCursor))
            {
                progress?.Report(new(65, ".cursor 사용자 데이터 복사 중..."));
                var rc = await RunRobocopyAsync(_userCursor, Path.Combine(tempPath, "User", ".cursor"), new[] { Path.Combine(_userCursor, "user-data") });
                if (rc >= 8) return FailIncomplete(tempPath, $".cursor 데이터 복사 실패 (Robocopy {rc}).");
            }
            else warnings.Add("사용자 .cursor 경로가 없습니다.");

            progress?.Report(new(82, "Cursor 버전 및 확장 정보 확인 중..."));
            var cursorCli = FindCursorCliCommand();
            (bool Success, string Output) version = (false, string.Empty);
            (bool Success, string Output) extensions = (false, string.Empty);
            (bool Success, string Output) versions = (false, string.Empty);

            if (cursorCli is not null)
            {
                version = await RunCursorCliAsync(cursorCli, "--version");
                if (version.Success) await File.WriteAllTextAsync(Path.Combine(tempPath, "cursor_version.txt"), version.Output, Encoding.UTF8);
                else warnings.Add("Cursor 버전 정보를 가져오지 못했습니다.");

                extensions = await RunCursorCliAsync(cursorCli, "--list-extensions");
                if (extensions.Success) await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions.txt"), extensions.Output, Encoding.UTF8);
                else warnings.Add("확장 목록을 가져오지 못했습니다.");

                versions = await RunCursorCliAsync(cursorCli, "--list-extensions", "--show-versions");
                if (versions.Success) await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions_with_versions.txt"), versions.Output, Encoding.UTF8);
            }
            else
            {
                warnings.Add("Cursor CLI를 찾지 못해 버전/확장 목록 생성을 건너뛰었습니다.");
            }

            progress?.Report(new(92, "백업 메타데이터 기록 중..."));
            await WriteInfoAsync(tempPath, new BackupInfo
            {
                CreatedAt = DateTimeOffset.Now,
                CursorVersion = version.Success ? FirstLine(version.Output) : null,
                Type = includeWorkspaceStorage ? "full-ai" : "normal",
                WorkspaceStorageIncluded = includeWorkspaceStorage,
                Status = warnings.Count == 0 ? "SUCCESS" : "WARNING"
            });

            progress?.Report(new(98, "백업 완료 처리 중..."));
            Directory.Move(tempPath, finalPath);
            progress?.Report(new(100, "백업 완료"));
            return new(true, warnings.Count == 0 ? $"백업 완료: {stamp}" : $"백업 완료(경고): {stamp}\r\n- {string.Join("\r\n- ", warnings)}");
        }
        catch (Exception ex) { return FailIncomplete(tempPath, ex.Message); }
    }

    public async Task<OperationResult> RestoreAsync(BackupRecord record, IProgress<OperationProgress>? progress = null)
    {
        if (record.IsIncomplete) return new(false, "완료되지 않은 백업은 복원할 수 없습니다.");
        var source = record.FullPath;
        var sourceRoaming = Path.Combine(source, "Roaming", "Cursor");
        if (!Directory.Exists(sourceRoaming)) return new(false, "선택한 백업에 Roaming\\Cursor 데이터가 없습니다.");

        Directory.CreateDirectory(_backupRoot);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var safetyTemp = Path.Combine(_backupRoot, $"pre-restore_{stamp}.incomplete");
        var safetyFinal = Path.Combine(_backupRoot, $"pre-restore_{stamp}");
        Directory.CreateDirectory(safetyTemp);

        try
        {
            progress?.Report(new(10, "현재 상태 안전 백업 중..."));
            var safety = await BackupCurrentStateAsync(safetyTemp);
            if (!safety.Success) return new(false, $"현재 상태 안전 백업 실패. 복원을 시작하지 않았습니다.\r\n{safety.Message}");
            await WriteInfoAsync(safetyTemp, new BackupInfo { CreatedAt = DateTimeOffset.Now, Type = "pre-restore", WorkspaceStorageIncluded = true, Status = "SUCCESS" });
            Directory.Move(safetyTemp, safetyFinal);

            var restoreLocal = Directory.Exists(Path.Combine(source, "Local", "Cursor"));
            var restoreUser = Directory.Exists(Path.Combine(source, "User", ".cursor"));
            var oldRoaming = _roamingCursor + $".cursor-backup-old-{stamp}";
            var oldLocal = _localCursor + $".cursor-backup-old-{stamp}";
            var oldUser = _userCursor + $".cursor-backup-old-{stamp}";
            var hadRoaming = false;
            var hadLocal = false;
            var hadUser = false;

            progress?.Report(new(35, "현재 Cursor 데이터 분리 중..."));
            try
            {
                hadRoaming = MoveAsideIfExists(_roamingCursor, oldRoaming);
                hadLocal = restoreLocal && MoveAsideIfExists(_localCursor, oldLocal);
                hadUser = restoreUser && MoveAsideIfExists(_userCursor, oldUser);
            }
            catch
            {
                if (hadUser && Directory.Exists(oldUser) && !Directory.Exists(_userCursor)) Directory.Move(oldUser, _userCursor);
                if (hadLocal && Directory.Exists(oldLocal) && !Directory.Exists(_localCursor)) Directory.Move(oldLocal, _localCursor);
                if (hadRoaming && Directory.Exists(oldRoaming) && !Directory.Exists(_roamingCursor)) Directory.Move(oldRoaming, _roamingCursor);
                throw;
            }

            try
            {
                progress?.Report(new(50, "Roaming 데이터 복원 중..."));
                EnsureRoboSuccess(await RunRobocopyAsync(sourceRoaming, _roamingCursor, Array.Empty<string>()), "Roaming 복원");
                if (restoreLocal)
                {
                    progress?.Report(new(68, "Local 데이터 복원 중..."));
                    EnsureRoboSuccess(await RunRobocopyAsync(Path.Combine(source, "Local", "Cursor"), _localCursor, Array.Empty<string>()), "Local 복원");
                }
                if (restoreUser)
                {
                    progress?.Report(new(78, ".cursor 사용자 데이터 복원 중..."));
                    EnsureRoboSuccess(await RunRobocopyAsync(Path.Combine(source, "User", ".cursor"), _userCursor, Array.Empty<string>()), ".cursor 복원");
                }

                progress?.Report(new(88, "백업 제외 데이터 보존 중..."));
                if (!Directory.Exists(Path.Combine(sourceRoaming, "User", "workspaceStorage")) && hadRoaming)
                {
                    var workspace = Path.Combine(oldRoaming, "User", "workspaceStorage");
                    if (Directory.Exists(workspace)) EnsureRoboSuccess(await RunRobocopyAsync(workspace, Path.Combine(_roamingCursor, "User", "workspaceStorage"), Array.Empty<string>()), "workspaceStorage 보존");
                }
                if (restoreUser && hadUser)
                {
                    var userData = Path.Combine(oldUser, "user-data");
                    if (Directory.Exists(userData)) EnsureRoboSuccess(await RunRobocopyAsync(userData, Path.Combine(_userCursor, "user-data"), Array.Empty<string>()), ".cursor user-data 보존");
                }
            }
            catch
            {
                progress?.Report(new(90, "복원 실패 - 기존 데이터 롤백 중..."));
                DeleteIfExists(_roamingCursor);
                if (restoreLocal) DeleteIfExists(_localCursor);
                if (restoreUser) DeleteIfExists(_userCursor);
                if (hadRoaming) Directory.Move(oldRoaming, _roamingCursor);
                if (hadLocal) Directory.Move(oldLocal, _localCursor);
                if (hadUser) Directory.Move(oldUser, _userCursor);
                throw;
            }

            progress?.Report(new(96, "임시 데이터 정리 중..."));
            DeleteIfExists(oldRoaming);
            DeleteIfExists(oldLocal);
            DeleteIfExists(oldUser);
            progress?.Report(new(100, "복원 완료"));
            return new(true, $"복원 완료: {record.Name}\r\n안전 백업: {Path.GetFileName(safetyFinal)}");
        }
        catch (Exception ex) { return new(false, $"복원 실패: {ex.Message}\r\n가능한 경우 pre-restore 백업을 확인하십시오."); }
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
            if (File.Exists(infoPath)) try { info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(infoPath)); } catch { }
            result.Add(new BackupRecord
            {
                Name = name,
                FullPath = directory,
                Type = info?.Type ?? (incomplete ? "incomplete" : "legacy"),
                Status = incomplete ? "INCOMPLETE" : info?.Status ?? "LEGACY",
                SizeText = FormatBytes(GetDirectorySize(directory)),
                CreatedAt = info?.CreatedAt.LocalDateTime ?? Directory.GetCreationTime(directory),
                IsIncomplete = incomplete
            });
        }
        return result;
    });

    public OperationResult DeleteBackup(BackupRecord record)
    {
        try
        {
            if (!IsUnderBackupRoot(record.FullPath)) return new(false, "백업 루트 밖의 폴더는 삭제할 수 없습니다.");
            Directory.Delete(record.FullPath, true);
            return new(true, $"삭제 완료: {record.Name}");
        }
        catch (Exception ex) { return new(false, $"삭제 실패: {ex.Message}"); }
    }

    private async Task<OperationResult> BackupCurrentStateAsync(string destination)
    {
        if (Directory.Exists(_roamingCursor))
        {
            var rc = await RunRobocopyAsync(_roamingCursor, Path.Combine(destination, "Roaming", "Cursor"), RoamingExcludes(true));
            if (rc >= 8) return new(false, $"Roaming 안전 백업 실패 (Robocopy {rc}).");
        }
        if (Directory.Exists(_localCursor))
        {
            var rc = await RunRobocopyAsync(_localCursor, Path.Combine(destination, "Local", "Cursor"), LocalExcludes());
            if (rc >= 8) return new(false, $"Local 안전 백업 실패 (Robocopy {rc}).");
        }
        if (Directory.Exists(_userCursor))
        {
            var rc = await RunRobocopyAsync(_userCursor, Path.Combine(destination, "User", ".cursor"), new[] { Path.Combine(_userCursor, "user-data") });
            if (rc >= 8) return new(false, $".cursor 안전 백업 실패 (Robocopy {rc}).");
        }
        return new(true, "OK");
    }

    private IEnumerable<string> RoamingExcludes(bool includeWorkspace)
    {
        var list = new List<string>
        {
            Path.Combine(_roamingCursor, "User", "WebStorage"), Path.Combine(_roamingCursor, "User", "CachedData"),
            Path.Combine(_roamingCursor, "User", "History"), Path.Combine(_roamingCursor, "User", "logs"),
            Path.Combine(_roamingCursor, "logs"), Path.Combine(_roamingCursor, "Cache")
        };
        if (!includeWorkspace) list.Add(Path.Combine(_roamingCursor, "User", "workspaceStorage"));
        return list;
    }

    private IEnumerable<string> LocalExcludes() => new[]
    {
        Path.Combine(_localCursor, "Cache"), Path.Combine(_localCursor, "GPUCache"), Path.Combine(_localCursor, "Code Cache"),
        Path.Combine(_localCursor, "Service Worker"), Path.Combine(_localCursor, "Crashpad")
    };

    private static async Task<int> RunRobocopyAsync(string source, string destination, IEnumerable<string> excludedDirectories)
    {
        Directory.CreateDirectory(destination);
        var psi = new ProcessStartInfo("robocopy.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { source, destination, "/E", "/R:1", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS" }) psi.ArgumentList.Add(arg);
        var excludes = excludedDirectories.ToArray();
        if (excludes.Length > 0) { psi.ArgumentList.Add("/XD"); foreach (var exclude in excludes) psi.ArgumentList.Add(exclude); }
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Robocopy를 시작할 수 없습니다.");
        await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static async Task<(bool Success, string Output)> RunCursorCliAsync(string command, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");
            var commandLine = new StringBuilder();
            commandLine.Append("call \"").Append(command).Append("\"");
            foreach (var arg in args) commandLine.Append(' ').Append(arg);
            psi.ArgumentList.Add(commandLine.ToString());

            using var process = Process.Start(psi);
            if (process is null) return (false, string.Empty);
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode == 0, output.Trim());
        }
        catch { return (false, string.Empty); }
    }

    private static string? FindCursorCliCommand()
    {
        var installRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Cursor"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cursor"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Cursor")
        };

        foreach (var root in installRoots)
        {
            var candidate = Path.Combine(root, "resources", "app", "bin", "cursor.cmd");
            if (File.Exists(candidate)) return candidate;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(part.Trim().Trim('"'), "cursor.cmd");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private static Task WriteInfoAsync(string folder, BackupInfo info) => File.WriteAllTextAsync(Path.Combine(folder, "backup-info.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    private static OperationResult FailIncomplete(string tempPath, string message) => new(false, $"백업 실패: {message}\r\n불완전 백업 유지: {Path.GetFileName(tempPath)}");
    private static string? FirstLine(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    private static bool MoveAsideIfExists(string source, string destination) { if (!Directory.Exists(source)) return false; Directory.Move(source, destination); return true; }
    private static void EnsureRoboSuccess(int code, string operation) { if (code >= 8) throw new IOException($"{operation} 실패 (Robocopy {code})."); }
    private static void DeleteIfExists(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }

    private bool IsUnderBackupRoot(string path)
    {
        var root = Path.GetFullPath(_backupRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static long GetDirectorySize(string path) { try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length); } catch { return 0; } }
    private static string FormatBytes(long bytes) { string[] units = ["B", "KB", "MB", "GB", "TB"]; double value = bytes; var unit = 0; while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; } return $"{value:0.##} {units[unit]}"; }
}
