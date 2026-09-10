using System.Diagnostics;

namespace CursorAI.BackupTool;

internal sealed class OptimizedRestoreService
{
    private const string RoamingArchiveName = "roaming.zip";
    private const string LocalArchiveName = "local.zip";
    private const string UserArchiveName = "cursor-user.zip";

    private readonly BackupService _backupService;
    private readonly string _roamingCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor");
    private readonly string _localCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cursor");
    private readonly string _userCursor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor");

    public OptimizedRestoreService(BackupService backupService) => _backupService = backupService;

    public Task<OperationResult> RestoreAsync(
        BackupRecord record,
        bool createPersistentSafetyBackup,
        IProgress<OperationProgress>? progress = null)
    {
        if (createPersistentSafetyBackup)
            return _backupService.RestoreAsync(record, progress);

        return RestoreWithMoveAsideAsync(record, progress);
    }

    private async Task<OperationResult> RestoreWithMoveAsideAsync(
        BackupRecord record,
        IProgress<OperationProgress>? progress)
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

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
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

        try
        {
            progress?.Report(new(10, "현재 Cursor 데이터 롤백용 분리 중..."));
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
                progress?.Report(new(30, archiveV3 ? "Roaming 아카이브 복원 중..." : "Roaming 데이터 복원 중..."));
                if (archiveV3)
                    await ArchiveStorage.ExtractAsync(sourceRoamingArchive, _roamingCursor);
                else
                    EnsureRoboSuccess(await RunRobocopyAsync(sourceRoamingFolder, _roamingCursor), "Roaming 복원");

                if (restoreLocal)
                {
                    progress?.Report(new(55, archiveV3 ? "Local 아카이브 복원 중..." : "Local 데이터 복원 중..."));
                    if (archiveV3)
                        await ArchiveStorage.ExtractAsync(sourceLocalArchive, _localCursor);
                    else
                        EnsureRoboSuccess(await RunRobocopyAsync(sourceLocalFolder, _localCursor), "Local 복원");
                }

                if (restoreUser)
                {
                    progress?.Report(new(70, archiveV3 ? ".cursor 아카이브 복원 중..." : ".cursor 사용자 데이터 복원 중..."));
                    if (archiveV3)
                        await ArchiveStorage.ExtractAsync(sourceUserArchive, _userCursor);
                    else
                        EnsureRoboSuccess(await RunRobocopyAsync(sourceUserFolder, _userCursor), ".cursor 복원");
                }

                progress?.Report(new(85, "백업 제외 데이터 제자리 이동 중..."));
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

            progress?.Report(new(95, "롤백용 임시 데이터 정리 중..."));
            DeleteIfExists(oldRoaming);
            DeleteIfExists(oldLocal);
            DeleteIfExists(oldUser);
            progress?.Report(new(100, "복원 완료"));

            return new(true,
                $"복원 완료: {record.Name}\r\n형식: {(archiveV3 ? "v3 archive" : "legacy folder")}\r\n복원 전 영구 안전 백업: 생성 안 함 (move-aside 롤백 사용)");
        }
        catch (Exception ex)
        {
            return new(false, $"복원 실패: {ex.Message}\r\n기존 데이터 move-aside 롤백을 시도했습니다.");
        }
    }

    private static async Task<int> RunRobocopyAsync(string source, string destination)
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

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Robocopy를 시작할 수 없습니다.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync());
        return process.ExitCode;
    }

    private static void EnsureRoboSuccess(int code, string operation)
    {
        if (code >= 8) throw new IOException($"{operation} 실패 (Robocopy {code}).");
    }

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

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
