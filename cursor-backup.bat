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

:: Warn if any data path is missing
if not exist "%APPDATA%\Cursor" set "MISSING=1"
if not exist "%LOCALAPPDATA%\Cursor" set "MISSING=1"
if not exist "%USERPROFILE%\.cursor" set "MISSING=1"
if defined MISSING echo "Warning: One or more Cursor data paths not found. Backup may be incomplete."

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

echo.
echo "[2/5] Copying settings and extensions..."
robocopy "%APPDATA%\Cursor" "%DEST%\Roaming\Cursor" /E /R:1 /W:1 /XD WorkspaceStorage User\WebStorage User\CachedData User\History User\logs logs Cache >nul
robocopy "%LOCALAPPDATA%\Cursor" "%DEST%\Local\Cursor" /E /R:1 /W:1 /XD Cache GPUCache "Code Cache" "Service Worker" Crashpad >nul
robocopy "%USERPROFILE%\.cursor" "%DEST%\User\.cursor" /E /R:1 /W:1 /XD user-data >nul
set "RC_ERR=%errorlevel%"

:: Use call when invoking cursor so this script does not exit
echo.
echo "[3/5] Creating information files..."
call cursor --version > "%DEST%\cursor_version.txt" 2>nul
call cursor --list-extensions > "%DEST%\extensions.txt" 2>nul

echo.
echo "[4/5] Optimizing database (VACUUM)..."
set "DBPATH=%DEST%\Roaming\Cursor\User\globalStorage"
if exist "%DBPATH%\state.vscdb" (
    "%SQLITE%" "%DBPATH%\state.vscdb" "VACUUM"
)

echo.
echo "[5/5] File cleanup"
if exist "%DBPATH%\state.vscdb.backup" (
    del /f /q "%DBPATH%\state.vscdb.backup" 
)

if %RC_ERR% gtr 7 echo "Warning: Some files may not have been copied (robocopy exit code %RC_ERR%)."

echo.
echo "===== All backup tasks completed! ====="
echo "Created folder: %NAME%"
echo.
pause