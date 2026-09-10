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

:: Backup folder name (date_time)
for /f "tokens=1-2 delims= " %%a in ('powershell -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd HHmm'"') do (
    set "DATE_STR=%%a"
    set "TIME_STR=%%b"
)
set "NAME=%DATE_STR%_%TIME_STR%"
set "DEST=%BACKUP_ROOT%\%NAME%"

echo.
echo "[1/5] Creating temporary folder: %NAME%"
if not exist "%DEST%" mkdir "%DEST%"
if not exist "%DEST%" (
    echo "[ERR] Failed to create backup folder: %DEST%"
    pause
    exit /b 1
)

echo.
echo "[2/5] Copying settings and extensions..."

if exist "%APPDATA%\Cursor" (
    robocopy "%APPDATA%\Cursor" "%DEST%\Roaming\Cursor" /E /R:1 /W:1 /XD WorkspaceStorage User\WebStorage User\CachedData User\History User\logs logs Cache >nul
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

:: Use call when invoking cursor so this script does not exit
echo.
echo "[3/5] Creating information files..."
call cursor --version > "%DEST%\cursor_version.txt" 2>nul
if errorlevel 1 (
    set "BACKUP_WARNING=1"
    echo "Warning: Could not create cursor_version.txt."
)
call cursor --list-extensions > "%DEST%\extensions.txt" 2>nul
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

echo.
echo "===== Backup Result ====="
echo "Roaming copy : !RC_ROAMING!"
echo "Local copy   : !RC_LOCAL!"
echo "User .cursor : !RC_USER!"

if "!BACKUP_FAILED!"=="1" (
    echo "Status       : FAILED"
    echo "One or more required data copy operations failed."
    echo "Backup folder: %NAME%"
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