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
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
                // A process may exit between enumeration and Kill(). Remaining processes are checked by the caller.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public async Task<OperationResult> CreateBackupAsync(bool includeWorkspaceStorage)
    {
        Directory.CreateDirectory(_backupRoot);

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var finalPath = Path.Combine(_backupRoot, stamp);
        var tempPath = finalPath + ".incomplete";

        if (Directory.Exists(tempPath) || Directory.Exists(finalPath))
            return new(false, "같은 시각의 백업 폴더가 이미 존재합니다.");

        Directory.CreateDirectory(tempPath);
        var warnings = new List<string>();

        try
        {
            if (Directory.Exists(_roamingCursor))
            {
                var excludes = new List<string>
                {
                    Path.Combine(_roamingCursor, "User", "WebStorage"),
                    Path.Combine(_roamingCursor, "User", "CachedData"),
                    Path.Combine(_roamingCursor, "User", "History"),
                    Path.Combine(_roamingCursor, "User", "logs"),
                    Path.Combine(_roamingCursor, "logs"),
                    Path.Combine(_roamingCursor, "Cache")
                };
                if (!includeWorkspaceStorage)
                    excludes.Add(Path.Combine(_roamingCursor, "User", "workspaceStorage"));

                var rc = await RunRobocopyAsync(_roamingCursor, Path.Combine(tempPath, "Roaming", "Cursor"), excludes);
                if (rc >= 8) return FailIncomplete(tempPath, $"Roaming 데이터 복사 실패 (Robocopy {rc}).");
            }
            else warnings.Add("Roaming Cursor 경로가 없습니다.");

            if (Directory.Exists(_localCursor))
            {
                var excludes = new[]
                {
                    Path.Combine(_localCursor, "Cache"), Path.Combine(_localCursor, "GPUCache"),
                    Path.Combine(_localCursor, "Code Cache"), Path.Combine(_localCursor, "Service Worker"),
                    Path.Combine(_localCursor, "Crashpad")
                };
                var rc = await RunRobocopyAsync(_localCursor, Path.Combine(tempPath, "Local", "Cursor"), excludes);
                if (rc >= 8) return FailIncomplete(tempPath, $"Local 데이터 복사 실패 (Robocopy {rc}).");
            }
            else warnings.Add("Local Cursor 경로가 없습니다.");

            if (Directory.Exists(_userCursor))
            {
                var rc = await RunRobocopyAsync(_userCursor, Path.Combine(tempPath, "User", ".cursor"),
                    new[] { Path.Combine(_userCursor, "user-data") });
                if (rc >= 8) return FailIncomplete(tempPath, $".cursor 데이터 복사 실패 (Robocopy {rc}).");
            }
            else warnings.Add("사용자 .cursor 경로가 없습니다.");

            var cursorCommand = FindCursorCommand();
            var version = await RunCursorAsync(cursorCommand, "--version");
            if (version.Success)
                await File.WriteAllTextAsync(Path.Combine(tempPath, "cursor_version.txt"), version.Output, Encoding.UTF8);
            else
                warnings.Add("Cursor 버전 정보를 가져오지 못했습니다.");

            var extensions = await RunCursorAsync(cursorCommand, "--list-extensions");
            if (extensions.Success)
                await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions.txt"), extensions.Output, Encoding.UTF8);
            else
                warnings.Add("확장 목록을 가져오지 못했습니다.");

            var extensionVersions = await RunCursorAsync(cursorCommand, "--list-extensions", "--show-versions");
            if (extensionVersions.Success)
                await File.WriteAllTextAsync(Path.Combine(tempPath, "extensions_with_versions.txt"), extensionVersions.Output, Encoding.UTF8);

            var info = new BackupInfo
            {
                CreatedAt = DateTimeOffset.Now,
                CursorVersion = version.Success ? FirstLine(version.Output) : null,
                Type = includeWorkspaceStorage ? "full-ai" : "normal",
                WorkspaceStorageIncluded = includeWorkspaceStorage,
                Status = warnings.Count == 0 ? "SUCCESS" : "WARNING"
            };
            await WriteInfoAsync(tempPath, info);

            Directory.Move(tempPath, finalPath);
            return new(true, warnings.Count == 0
                ? $"백업 완료: {stamp}"
                : $"백업 완료(경고): {stamp}\r\n- {string.Join("\r\n- ", warnings)}");
        }
        catch (Exception ex)
        {
            return FailIncomplete(tempPath, ex.Message);
        }
    }

    public async Task<OperationResult> RestoreAsync(BackupRecord record)
    {
        if (record.IsIncomplete)
            return new(false, "완료되지 않은 백업은 복원할 수 없습니다.");

        var source = record.FullPath;
        var sourceRoaming = Path.Combine(source, "Roaming", "Cursor");
        if (!Directory.Exists(sourceRoaming))
            return new(false, "선택한 백업에 Roaming\\Cursor 데이터가 없습니다.");

        Directory.CreateDirectory(_backupRoot);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var safetyTemp = Path.Combine(_backupRoot, $"pre-restore_{stamp}.incomplete");
        var safetyFinal = Path.Combine(_backupRoot, $"pre-restore_{stamp}");
        Directory.CreateDirectory(safetyTemp);

        try
        {
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

            var restoreLocal = Directory.Exists(Path.Combine(source, "Local", "Cursor"));
            var restoreUser = Directory.Exists(Path.Combine(source, "User", ".cursor"));
            var oldRoaming = _roamingCursor + $".cursor-backup-old-{stamp}";
            var oldLocal = _localCursor + $".cursor-backup-old-{stamp}";
            var oldUser = _userCursor + $".cursor-backup-old-{stamp}";

            var hadRoaming = MoveAsideIfExists(_roamingCursor, oldRoaming);
            var hadLocal = restoreLocal && MoveAsideIfExists(_localCursor, oldLocal);
            var hadUser = restoreUser && MoveAsideIfExists(_userCursor, oldUser);

            try
            {
                EnsureRoboSuccess(await RunRobocopyAsync(sourceRoaming, _roamingCursor, Array.Empty<string>()), "Roaming 복원");
                if (restoreLocal)
                    EnsureRoboSuccess(await RunRobocopyAsync(Path.Combine(source, "Local", "Cursor"), _localCursor, Array.Empty<string>()), "Local 복원");
                if (restoreUser)
                    EnsureRoboSuccess(await RunRobocopyAsync(Path.Combine(source, "User", ".cursor"), _userCursor, Array.Empty<string>()), ".cursor 복원");

                if (!Directory.Exists(Path.Combine(sourceRoaming, "User", "workspaceStorage")) && hadRoaming)
                {
                    var currentWorkspace = Path.Combine(oldRoaming, "User", "workspaceStorage");
                    if (Directory.Exists(currentWorkspace))
                        EnsureRoboSuccess(await RunRobocopyAsync(currentWorkspace, Path.Combine(_roamingCursor, "User", "workspaceStorage"), Array.Empty<string>()), "workspaceStorage 보존");
                }

                if (restoreUser && hadUser)
                {
                    var currentUserData = Path.Combine(oldUser, "user-data");
                    if (Directory.Exists(currentUserData))
                        EnsureRoboSuccess(await RunRobocopyAsync(currentUserData, Path.Combine(_userCursor, "user-data"), Array.Empty<string>()), ".cursor user-data 보존");
                }
            }
            catch
            {
                DeleteIfExists(_roamingCursor);
                if (restoreLocal) DeleteIfExists(_localCursor);
                if (restoreUser) DeleteIfExists(_userCursor);
                if (hadRoaming) Directory.Move(oldRoaming, _roamingCursor);
                if (hadLocal) Directory.Move(oldLocal, _localCursor);
                if (hadUser) Directory.Move(oldUser, _userCursor);
                throw;
            }

            DeleteIfExists(oldRoaming);
            DeleteIfExists(oldLocal);
            DeleteIfExists(oldUser);
            return new(true, $"복원 완료: {record.Name}\r\n안전 백업: {Path.GetFileName(safetyFinal)}");
        }
        catch (Exception ex)
        {
            return new(false, $"복원 실패: {ex.Message}\r\n가능한 경우 pre-restore 백업을 확인하십시오.");
        }
    }

    public async Task<IReadOnlyList<BackupRecord>> GetBackupsAsync()
    {
        return await Task.Run(() =>
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
                    try { info = JsonSerializer.Deserialize<BackupInfo>(File.ReadAllText(infoPath)); } catch { }
                }

                var type = info?.Type ?? (incomplete ? "incomplete" : "legacy");
                var status = incomplete ? "INCOMPLETE" : info?.Status ?? "LEGACY";
                var created = info?.CreatedAt.LocalDateTime ?? Directory.GetCreationTime(directory);
                result.Add(new BackupRecord
                {
                    Name = name,
                    FullPath = directory,
                    Type = type,
                    Status = status,
                    SizeText = FormatBytes(GetDirectorySize(directory)),
                    CreatedAt = created,
                    IsIncomplete = incomplete
                });
            }
            return (IReadOnlyList<BackupRecord>)result;
        });
    }

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
        if (Directory.Exists(_roamingCursor))
        {
            var rc = await RunRobocopyAsync(_roamingCursor, Path.Combine(destination, "Roaming", "Cursor"),
                new[] { Path.Combine(_roamingCursor, "User", "WebStorage"), Path.Combine(_roamingCursor, "User", "CachedData"), Path.Combine(_roamingCursor, "User", "History"), Path.Combine(_roamingCursor, "User", "logs"), Path.Combine(_roamingCursor, "logs"), Path.Combine(_roamingCursor, "Cache") });
            if (rc >= 8) return new(false, $"Roaming 안전 백업 실패 (Robocopy {rc}).");
        }
        if (Directory.Exists(_localCursor))
        {
            var rc = await RunRobocopyAsync(_localCursor, Path.Combine(destination, "Local", "Cursor"),
                new[] { Path.Combine(_localCursor, "Cache"), Path.Combine(_localCursor, "GPUCache"), Path.Combine(_localCursor, "Code Cache"), Path.Combine(_localCursor, "Service Worker"), Path.Combine(_localCursor, "Crashpad") });
            if (rc >= 8) return new(false, $"Local 안전 백업 실패 (Robocopy {rc}).");
        }
        if (Directory.Exists(_userCursor))
        {
            var rc = await RunRobocopyAsync(_userCursor, Path.Combine(destination, "User", ".cursor"), new[] { Path.Combine(_userCursor, "user-data") });
            if (rc >= 8) return new(false, $".cursor 안전 백업 실패 (Robocopy {rc}).");
        }
        return new(true, "OK");
    }

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
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(destination);
        psi.ArgumentList.Add("/E");
        psi.ArgumentList.Add("/R:1");
        psi.ArgumentList.Add("/W:1");
        psi.ArgumentList.Add("/NFL");
        psi.ArgumentList.Add("/NDL");
        psi.ArgumentList.Add("/NJH");
        psi.ArgumentList.Add("/NJS");
        var excludes = excludedDirectories.ToArray();
        if (excludes.Length > 0)
        {
            psi.ArgumentList.Add("/XD");
            foreach (var exclude in excludes) psi.ArgumentList.Add(exclude);
        }
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Robocopy를 시작할 수 없습니다.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static async Task<(bool Success, string Output)> RunCursorAsync(string command, params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi);
            if (process is null) return (false, string.Empty);
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode == 0, output.Trim());
        }
        catch { return (false, string.Empty); }
    }

    private static string FindCursorCommand()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Cursor", "Cursor.exe");
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cursor", "Cursor.exe");
        var pf86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Cursor", "Cursor.exe");
        if (File.Exists(local)) return local;
        if (File.Exists(pf)) return pf;
        if (File.Exists(pf86)) return pf86;
        return "cursor";
    }

    private static async Task WriteInfoAsync(string folder, BackupInfo info)
    {
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(folder, "backup-info.json"), json, new UTF8Encoding(false));
    }

    private static OperationResult FailIncomplete(string tempPath, string message) =>
        new(false, $"백업 실패: {message}\r\n불완전 백업 유지: {Path.GetFileName(tempPath)}");

    private static string? FirstLine(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    private static bool MoveAsideIfExists(string source, string destination)
    {
        if (!Directory.Exists(source)) return false;
        Directory.Move(source, destination);
        return true;
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

    private static long GetDirectorySize(string path)
    {
        try { return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length); }
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
}
