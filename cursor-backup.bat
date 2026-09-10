@echo off
setlocal enabledelayedexpansion
:: Data paths are per-user (APPDATA, LOCALAPPDATA, USERPROFILE). Works for both user-installed and system-installed Cursor.

echo "===== Cursor Backup Start ====="
echo.

set "ROOT=%~dp0"
set "SQLITE=%ROOT%sqlite3.exe"
set "BACKUP_ROOT=%ROOT%backups"
if not exist "%BACKUP_ROOT%" mkdir "%BACKUP_ROOT%"

:: Check required file exists
if not exist "%SQLITE%" ( echo "[ERR] Can not find sqlite3.exe" & pause & exit )

:: Cursor executable (user install, then system install, else PATH)
set "CURSOR_CMD=cursor"
if exist "%LOCALAPPDATA%\Programs\Cursor\Cursor.exe" ( set "CURSOR_CMD=%LOCALAPPDATA%\Programs\Cursor\Cursor.exe" ) else if exist "C:\Program Files\Cursor\Cursor.exe" ( set "CURSOR_CMD=C:\Program Files\Cursor\Cursor.exe" ) else if exist "C:\Program Files (x86)\Cursor\Cursor.exe" ( set "CURSOR_CMD=C:\Program Files (x86)\Cursor\Cursor.exe" )

:: Backup mode. Normal is the default to avoid large workspaceStorage backups.
echo "Backup mode:"
echo "  1. Normal backup (default, excludes workspaceStorage)"
echo "  2. Full AI backup (includes workspaceStorage; may use much more space)"
set /p "BACKUP_MODE=Select mode [1]: "
if "!BACKUP_MODE!"=="" set "BACKUP_MODE=1"
if not "!BACKUP_MODE!"=="1" if not "!BACKUP_MODE!"=="2" (
    echo "[ERR] Invalid backup mode."
    pause
    exit /b 1
)

set "WORKSPACE_INCLUDED=0"
set "BACKUP_TYPE=normal"
if "!BACKUP_MODE!"=="2" (
    set "WORKSPACE_INCLUDED=1"
    set "BACKUP_TYPE=full-ai"
)

echo.
if "!WORKSPACE_INCLUDED!"=="1" (
    echo "Selected: Full AI backup - workspaceStorage will be included."
) else (
    echo "Selected: Normal backup - workspaceStorage will be excluded."
)
echo "Notice: Cursor backup data may contain project paths, AI transcripts, MCP settings,"
echo "        API keys, tokens, or other sensitive information. Store backups securely."
echo.

:: Cursor process: prompt before closing
tasklist | find /I "cursor.exe" >nul
if %errorlevel%==0 (
    set /p "CHOICE=Cursor is running. Close it for a safe backup? (Y/N): "
    if /i "!CHOICE!"=="Y" (
        taskkill /IM cursor.exe /F
        timeout /t 2 /nobreak >nul
    ) else (
        echo "Continuing without closing. Some files may be in use."
    )
)

:: Backup result state
set "BACKUP_FAILED=0"
set "BACKUP_WARNING=0"
set "RC_ROAMING=-"
set "RC_LOCAL=-"
set "RC_USER=-"

:: Warn if any data path is missing. Missing paths are warnings, not copy failures.
if not exist "%APPDATA%\Cursor" (
    set "BACKUP_WARNING=1"
    echo "Warning: %APPDATA%\Cursor not found. Roaming data will be skipped."
)
if not exist "%LOCALAPPDATA%\Cursor" (
    set "BACKUP_WARNING=1"
    echo "Warning: %LOCALAPPDATA%\Cursor not found. Local data will be skipped."
)
if not exist "%USERPROFILE%\.cursor" (
    set "BACKUP_WARNING=1"
    echo "Warning: %USERPROFILE%\.cursor not found. User .cursor data will be skipped."
)

:: Backup folder name (date_time with seconds)
for /f %%a in ('powershell -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd_HHmmss'"') do set "NAME=%%a"
set "FINAL_DEST=%BACKUP_ROOT%\%NAME%"
set "DEST=%FINAL_DEST%.incomplete"

echo.
echo "[1/5] Creating temporary folder: %NAME%.incomplete"
if exist "%DEST%" (
    echo "[ERR] Temporary backup folder already exists: %DEST%"
    pause
    exit /b 1
)
if exist "%FINAL_DEST%" (
    echo "[ERR] Backup folder already exists: %FINAL_DEST%"
    pause
    exit /b 1
)
mkdir "%DEST%"
if not exist "%DEST%" (
    echo "[ERR] Failed to create backup folder: %DEST%"
    pause
    exit /b 1
)

echo.
echo "[2/5] Copying settings and extensions..."

if exist "%APPDATA%\Cursor" (
    if "!WORKSPACE_INCLUDED!"=="1" (
        robocopy "%APPDATA%\Cursor" "%DEST%\Roaming\Cursor" /E /R:1 /W:1 /XD User\WebStorage User\CachedData User\History User\logs logs Cache >nul
    ) else (
        robocopy "%APPDATA%\Cursor" "%DEST%\Roaming\Cursor" /E /R:1 /W:1 /XD "%APPDATA%\Cursor\User\workspaceStorage" User\WebStorage User\CachedData User\History User\logs logs Cache >nul
    )
    set "RC_ROAMING=!errorlevel!"
    if !RC_ROAMING! geq 8 (
        set "BACKUP_FAILED=1"
        echo "[ERR] Roaming Cursor copy failed (robocopy exit code !RC_ROAMING!)."
    )
)

if exist "%LOCALAPPDATA%\Cursor" (
    robocopy "%LOCALAPPDATA%\Cursor" "%DEST%\Local\Cursor" /E /R:1 /W:1 /XD Cache GPUCache "Code Cache" "Service Worker" Crashpad >nul
    set "RC_LOCAL=!errorlevel!"
    if !RC_LOCAL! geq 8 (
        set "BACKUP_FAILED=1"
        echo "[ERR] Local Cursor copy failed (robocopy exit code !RC_LOCAL!)."
    )
)

if exist "%USERPROFILE%\.cursor" (
    robocopy "%USERPROFILE%\.cursor" "%DEST%\User\.cursor" /E /R:1 /W:1 /XD user-data >nul
    set "RC_USER=!errorlevel!"
    if !RC_USER! geq 8 (
        set "BACKUP_FAILED=1"
        echo "[ERR] User .cursor copy failed (robocopy exit code !RC_USER!)."
    )
)

:: Use call when invoking Cursor so PATH-resolved batch launchers return to this script.
echo.
echo "[3/5] Creating information files..."
call "%CURSOR_CMD%" --version > "%DEST%\cursor_version.txt" 2>nul
if errorlevel 1 (
    set "BACKUP_WARNING=1"
    echo "Warning: Could not create cursor_version.txt."
)
call "%CURSOR_CMD%" --list-extensions > "%DEST%\extensions.txt" 2>nul
if errorlevel 1 (
    set "BACKUP_WARNING=1"
    echo "Warning: Could not create extensions.txt."
)

echo.
echo "[4/5] Optimizing database (VACUUM)..."
set "DBPATH=%DEST%\Roaming\Cursor\User\globalStorage"
if exist "%DBPATH%\state.vscdb" (
    "%SQLITE%" "%DBPATH%\state.vscdb" "VACUUM"
    if errorlevel 1 (
        set "BACKUP_WARNING=1"
        echo "Warning: Database VACUUM failed. The copied database is still kept."
    )
)

echo.
echo "[5/5] File cleanup"
if exist "%DBPATH%\state.vscdb.backup" (
    del /f /q "%DBPATH%\state.vscdb.backup"
    if errorlevel 1 (
        set "BACKUP_WARNING=1"
        echo "Warning: Could not remove state.vscdb.backup from the backup folder."
    )
)

set "BACKUP_STATUS=SUCCESS"
if "!BACKUP_WARNING!"=="1" set "BACKUP_STATUS=WARNING"
if "!BACKUP_FAILED!"=="1" set "BACKUP_STATUS=FAILED"

:: Write a machine-readable manifest for completed-backup validation and future GUI use.
powershell -NoProfile -Command "$versionFile = Join-Path $env:DEST 'cursor_version.txt'; $version = $null; if (Test-Path -LiteralPath $versionFile) { $version = Get-Content -LiteralPath $versionFile -ErrorAction SilentlyContinue | Select-Object -First 1 }; $obj = [ordered]@{ backupVersion = 2; createdAt = (Get-Date).ToString('o'); cursorVersion = $version; type = $env:BACKUP_TYPE; workspaceStorageIncluded = ($env:WORKSPACE_INCLUDED -eq '1'); status = $env:BACKUP_STATUS }; $obj | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $env:DEST 'backup-info.json') -Encoding UTF8" >nul 2>&1
if errorlevel 1 (
    if "!BACKUP_FAILED!"=="0" (
        set "BACKUP_WARNING=1"
        set "BACKUP_STATUS=WARNING"
    )
    echo "Warning: Could not create backup-info.json."
)

echo.
echo "===== Backup Result ====="
echo "Backup type  : !BACKUP_TYPE!"
echo "workspaceStorage: !WORKSPACE_INCLUDED!"
echo "Roaming copy : !RC_ROAMING!"
echo "Local copy   : !RC_LOCAL!"
echo "User .cursor : !RC_USER!"

if "!BACKUP_FAILED!"=="1" (
    echo "Status       : FAILED"
    echo "One or more required data copy operations failed."
    echo "Incomplete backup kept at: %NAME%.incomplete"
    echo.
    pause
    exit /b 1
)

move "%DEST%" "%FINAL_DEST%" >nul
if errorlevel 1 (
    echo "Status       : FAILED"
    echo "[ERR] Backup data was copied, but the temporary folder could not be finalized."
    echo "Incomplete backup kept at: %NAME%.incomplete"
    echo.
    pause
    exit /b 1
)

if "!BACKUP_WARNING!"=="1" (
    echo "Status       : WARNING"
    echo "Backup completed, but one or more optional items were skipped or failed."
) else (
    echo "Status       : SUCCESS"
    echo "All backup tasks completed successfully."
)

echo "Created folder: %NAME%"
echo.
pause
exit /b 0