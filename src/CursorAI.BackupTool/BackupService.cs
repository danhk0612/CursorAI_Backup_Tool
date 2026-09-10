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
                ? "Roaming 데이터 복사 중 (두 WorkspaceStorage 포함)..."
                : "Roaming 데이터 복사 중 (두 WorkspaceStorage 제외)..."));
            var roamingRc = await RunRobocopyAsync(_roamingCursor, Path.Combine(tempPath, "Roaming", "Cursor"), RoamingExcludes(includeWorkspaceStorage));
            if (roamingRc >= 8)
                return FailIncomplete(tempPath, $"Roaming 데이터 복사 실패 (Robocopy {roamingRc}).");

            if (Directory.Exists(_localCursor))
            {
                progress?.Report(new(45, "Local 데이터 복사 중..."));
                var localRc = await RunRobocopyAsync(_localCursor, Path.Combine(tempPath, "Local", "Cursor"), LocalExcludes());
                if (localRc >= 8)
                    return FailIncomplete(tempPath, $"Local 데이터 복사 실패 (Robocopy {localRc}).");
            }
            else
            {
                infos.Add("Local Cursor 경로가 없어 해당 범위를 건너뛰었습니다.");
            }

            if (Directory.Exists(_userCursor))
            {
                progress?.Report(new(65, ".cursor 사용자 데이터 복사 중 (extensions 포함)..."));
                var userRc = await RunRobocopyAsync(_userCursor, Path.Combine(tempPath, "User", ".cursor"),
                    new[] { Path.Combine(_userCursor, "user-data") });
                if (userRc >= 8)
                    return FailIncomplete(tempPath, $".cursor 데이터 복사 실패 (Robocopy {userRc}).");
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
                        await File.WriteAllTextAsync(Path.Combine(tempPath, "cursor_version.txt"), cursorVersion, Encoding.UTF8);
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
                    await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions.txt"), extensions.Output, Encoding.UTF8);
                else
                    infos.Add("확장 ID 목록을 가져오지 못했습니다. 확장 실파일 백업은 유지됩니다.");

                var versions = await RunCursorCliAsync(install.CliPath, "--list-extensions", "--show-versions");
                if (versions.Success)
                    await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions_with_versions.txt"), versions.Output, Encoding.UTF8);
                else
                    infos.Add("확장 버전 목록을 가져오지 못했습니다. 확장 실파일 백업은 유지됩니다.");
            }
            else
            {
                infos.Add("Cursor CLI를 찾지 못해 확장 목록 생성을 건너뛰었습니다. 확장 실파일 백업은 유지됩니다.");
            }

            progress?.Report(new(92, "백업 메타데이터 기록 중..."));
            await WriteInfoAsync(tempPath, new BackupInfo
            {
                CreatedAt = DateTimeOffset.Now,
                CursorVersion = cursorVersion,
                Type = includeWorkspaceStorage ? "full-ai" : "normal",
                WorkspaceStorageIncluded = includeWorkspaceStorage,
                Status = warnings.Count == 0 ? "SUCCESS" : "WARNING"
            });

            progress?.Report(new(98, "백업 완료 처리 중..."));
            Directory.Move(tempPath, finalPath);
            progress?.Report(new(100, "백업 완료"));

            var message = new StringBuilder(warnings.Count == 0 ? $"백업 완료: {stamp}" : $"백업 완료(경고): {stamp}");
            foreach (var warning in warnings) message.Append("\r\n경고: ").Append(warning);
            foreach (var info in infos) message.Append("\r\n안내: ").Append(info);
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
        var sourceRoaming = Path.Combine(source, "Roaming", "Cursor");
        if (!Directory.Exists(sourceRoaming))
            return new(false, "선택한 백업에 필수 Roaming\\Cursor 데이터가 없습니다.");

        Directory.CreateDirectory(_backupRoot);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var safetyTemp = Path.Combine(_backupRoot, $"pre-restore_{stamp}.incomplete");
        var safetyFinal = Path.Combine(_backupRoot, $"pre-restore_{stamp}");
        if (Directory.Exists(safetyTemp) || Directory.Exists(safetyFinal))
            return new(false, "같은 시각의 복원 안전 백업 폴더가 이미 존재합니다.");
        Directory.CreateDirectory(safetyTemp);

        string? oldRoaming = null;
        string? oldLocal = null;
        string? oldUser = null;
        string? preservedRootWorkspace = null;
        string? preservedUserWorkspace = null;
        string? preservedUserData = null;

        try
        {
            progress?.Report(new(10, "현재 상태 안전 백업 중..."));
            var safety = await BackupCurrentStateAsync(safetyTemp);
            if (!safety.Success)
                return new(false, $"현재 상태 안전 백업 실패. 복원을 시작하지 않았습니다.\r\n{safety.Message}");

            await WriteInfoAsync(safetyTemp, new BackupInfo
            {
                CreatedAt = DateTimeOffset.Now,
                Type = "pre-restore",
                WorkspaceStorageIncluded = true,
                Status = "SUCCESS"
            });
            Directory.Move(safetyTemp, safetyFinal);

            var sourceLocal = Path.Combine(source, "Local", "Cursor");
            var sourceUser = Path.Combine(source, "User", ".cursor");
            var restoreLocal = Directory.Exists(sourceLocal);
            var restoreUser = Directory.Exists(sourceUser);

            oldRoaming = _roamingCursor + $".cursor-backup-old-{stamp}";
            oldLocal = _localCursor + $".cursor-backup-old-{stamp}";
            oldUser = _userCursor + $".cursor-backup-old-{stamp}";

            progress?.Report(new(30, "현재 Cursor 데이터 분리 중..."));
            var hadRoaming = MoveAsideIfExists(_roamingCursor, oldRoaming);
            var hadLocal = restoreLocal && MoveAsideIfExists(_localCursor, oldLocal);
            var hadUser = restoreUser && MoveAsideIfExists(_userCursor, oldUser);

            try
            {
                if (hadRoaming)
                {
                    if (!Directory.Exists(Path.Combine(sourceRoaming, "WorkspaceStorage")))
                        preservedRootWorkspace = MoveToPreserveIfExists(Path.Combine(oldRoaming, "WorkspaceStorage"), _roamingCursor + $".preserve-root-workspace-{stamp}");
                    if (!Directory.Exists(Path.Combine(sourceRoaming, "User", "workspaceStorage")))
                        preservedUserWorkspace = MoveToPreserveIfExists(Path.Combine(oldRoaming, "User", "workspaceStorage"), _roamingCursor + $".preserve-user-workspace-{stamp}");
                }

                if (hadUser && !Directory.Exists(Path.Combine(sourceUser, "user-data")))
                    preservedUserData = MoveToPreserveIfExists(Path.Combine(oldUser, "user-data"), _userCursor + $".preserve-user-data-{stamp}");
            }
            catch
            {
                RestorePreservedToOld(preservedRootWorkspace, oldRoaming, "WorkspaceStorage");
                RestorePreservedToOld(preservedUserWorkspace, oldRoaming, Path.Combine("User", "workspaceStorage"));
                RestorePreservedToOld(preservedUserData, oldUser, "user-data");
                if (hadUser && Directory.Exists(oldUser) && !Directory.Exists(_userCursor)) Directory.Move(oldUser, _userCursor);
                if (hadLocal && Directory.Exists(oldLocal) && !Directory.Exists(_localCursor)) Directory.Move(oldLocal, _localCursor);
                if (hadRoaming && Directory.Exists(oldRoaming) && !Directory.Exists(_roamingCursor)) Directory.Move(oldRoaming, _roamingCursor);
                throw;
            }

            try
            {
                progress?.Report(new(45, "Roaming 데이터 복원 중..."));
                EnsureRoboSuccess(await RunRobocopyAsync(sourceRoaming, _roamingCursor, Array.Empty<string>()), "Roaming 복원");

                if (restoreLocal)
                {
                    progress?.Report(new(62, "Local 데이터 복원 중..."));
                    EnsureRoboSuccess(await RunRobocopyAsync(sourceLocal, _localCursor, Array.Empty<string>()), "Local 복원");
                }

                if (restoreUser)
                {
                    progress?.Report(new(75, ".cursor 사용자 데이터 복원 중..."));
                    EnsureRoboSuccess(await RunRobocopyAsync(sourceUser, _userCursor, Array.Empty<string>()), ".cursor 복원");
                }

                progress?.Report(new(88, "백업 제외 데이터 제자리 이동 중..."));
                MovePreservedIntoTarget(preservedRootWorkspace, Path.Combine(_roamingCursor, "WorkspaceStorage"));
                preservedRootWorkspace = null;
                MovePreservedIntoTarget(preservedUserWorkspace, Path.Combine(_roamingCursor, "User", "workspaceStorage"));
                preservedUserWorkspace = null;
                MovePreservedIntoTarget(preservedUserData, Path.Combine(_userCursor, "user-data"));
                preservedUserData = null;
            }
            catch
            {
                progress?.Report(new(90, "복원 실패 - 기존 데이터 롤백 중..."));
                DeleteIfExists(_roamingCursor);
                if (restoreLocal) DeleteIfExists(_localCursor);
                if (restoreUser) DeleteIfExists(_userCursor);

                RestorePreservedToOld(preservedRootWorkspace, oldRoaming, "WorkspaceStorage");
                RestorePreservedToOld(preservedUserWorkspace, oldRoaming, Path.Combine("User", "workspaceStorage"));
                RestorePreservedToOld(preservedUserData, oldUser, "user-data");

                if (hadRoaming && Directory.Exists(oldRoaming)) Directory.Move(oldRoaming, _roamingCursor);
                if (hadLocal && Directory.Exists(oldLocal)) Directory.Move(oldLocal, _localCursor);
                if (hadUser && Directory.Exists(oldUser)) Directory.Move(oldUser, _userCursor);
                throw;
            }

            progress?.Report(new(96, "임시 데이터 정리 중..."));
            DeleteIfExists(oldRoaming);
            DeleteIfExists(oldLocal);
            DeleteIfExists(oldUser);
            progress?.Report(new(100, "복원 완료"));
            return new(true, $"복원 완료: {record.Name}\r\n안전 백업: {Path.GetFileName(safetyFinal)}");
        }
        catch (Exception ex)
        {
            return new(false, $"복원 실패: {ex.Message}\r\npre-restore 백업: {Path.GetFileName(safetyFinal)}");
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
            if (!IsUnderBackupRoot(record.FullPath))
                return new(false, "백업 루트 밖의 폴더는 삭제할 수 없습니다.");
            Directory.Delete(record.FullPath, true);
            return new(true, $"삭제 완료: {record.Name}");
        }
        catch (Exception ex)
        {
            return new(false, $"삭제 실패: {ex.Message}");
        }
    }

    private async Task<OperationResult> BackupCurrentStateAsync(string destination)
    {
        if (!Directory.Exists(_roamingCursor))
            return new(false, $"필수 Cursor Roaming 경로가 없습니다: {_roamingCursor}");

        var roamingRc = await RunRobocopyAsync(_roamingCursor, Path.Combine(destination, "Roaming", "Cursor"), RoamingExcludes(true));
        if (roamingRc >= 8) return new(false, $"Roaming 안전 백업 실패 (Robocopy {roamingRc}).");

        if (Directory.Exists(_localCursor))
        {
            var localRc = await RunRobocopyAsync(_localCursor, Path.Combine(destination, "Local", "Cursor"), LocalExcludes());
            if (localRc >= 8) return new(false, $"Local 안전 백업 실패 (Robocopy {localRc}).");
        }

        if (Directory.Exists(_userCursor))
        {
            var userRc = await RunRobocopyAsync(_userCursor, Path.Combine(destination, "User", ".cursor"), new[] { Path.Combine(_userCursor, "user-data") });
            if (userRc >= 8) return new(false, $".cursor 안전 백업 실패 (Robocopy {userRc}).");
        }
        return new(true, "OK");
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
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask, waitTask);
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
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var waitTask = process.WaitForExitAsync();
            await Task.WhenAll(stdoutTask, stderrTask, waitTask);
            return (process.ExitCode == 0, stdoutTask.Result.Trim());
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
            if (executable is null && File.Exists(exeCandidate)) executable = exeCandidate;

            var cliCandidate = Path.Combine(root, "resources", "app", "bin", "cursor.cmd");
            if (cli is null && File.Exists(cliCandidate)) cli = cliCandidate;
        }

        if (cli is null)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(part.Trim().Trim('"'), "cursor.cmd");
                    if (File.Exists(candidate))
                    {
                        cli = candidate;
                        break;
                    }
                }
                catch { }
            }
        }

        return new(executable, cli);
    }

    private static string? MoveToPreserveIfExists(string source, string destination)
    {
        if (!Directory.Exists(source)) return null;
        if (Directory.Exists(destination)) throw new IOException($"보존용 임시 폴더가 이미 존재합니다: {destination}");
        Directory.Move(source, destination);
        return destination;
    }

    private static void MovePreservedIntoTarget(string? preserved, string target)
    {
        if (preserved is null || !Directory.Exists(preserved)) return;
        if (Directory.Exists(target)) throw new IOException($"보존 데이터 대상 경로가 이미 존재합니다: {target}");
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        Directory.Move(preserved, target);
    }

    private static void RestorePreservedToOld(string? preserved, string? oldRoot, string relativeTarget)
    {
        if (preserved is null || oldRoot is null || !Directory.Exists(preserved)) return;
        var target = Path.Combine(oldRoot, relativeTarget);
        if (Directory.Exists(target)) return;
        var parent = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        Directory.Move(preserved, target);
    }

    private static Task WriteInfoAsync(string folder, BackupInfo info) =>
        File.WriteAllTextAsync(Path.Combine(folder, "backup-info.json"),
            JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

    private static OperationResult FailIncomplete(string tempPath, string message) =>
        new(false, $"백업 실패: {message}\r\n불완전 백업 유지: {Path.GetFileName(tempPath)}");

    private static bool MoveAsideIfExists(string source, string destination)
    {
        if (!Directory.Exists(source)) return false;
        if (Directory.Exists(destination)) throw new IOException($"임시 폴더가 이미 존재합니다: {destination}");
        Directory.Move(source, destination);
        return true;
    }

    private static void EnsureRoboSuccess(int code, string operation)
    {
        if (code >= 8) throw new IOException($"{operation} 실패 (Robocopy {code}).");
    }

    private static void DeleteIfExists(string? path)
    {
        if (path is not null && Directory.Exists(path)) Directory.Delete(path, true);
    }

    private bool IsUnderBackupRoot(string path)
    {
        var root = Path.GetFullPath(_backupRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
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
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private sealed record CursorInstall(string? ExecutablePath, string? CliPath);
}
