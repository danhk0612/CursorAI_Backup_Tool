@echo off
setlocal EnableDelayedExpansion
:: Data paths are per-user. Works for both user-installed and system-installed Cursor.

echo "===== Cursor Restore ====="
echo.

set "ROOT=%~dp0"
set "BACKUP_ROOT=%ROOT%backups"

tasklist | find /I "cursor.exe" >nul
if %errorlevel%==0 (
    set /p "CHOICE=Cursor is running. Close it for a safe restore? (Y/N): "
    if /i "!CHOICE!"=="Y" (
        taskkill /IM cursor.exe /F
        timeout /t 2 /nobreak >nul
    ) else (
        echo "Please close Cursor and run this script again."
        pause
        exit /b 1
    )
)

:menu

echo.
echo "===== Backup List ====="
echo.

set i=0

if not exist "%BACKUP_ROOT%" (
    echo "No backup folder found."
    pause
    goto menu
)

pushd "%BACKUP_ROOT%"
for /f "delims=" %%d in ('dir /b /ad /o-d 2^>nul') do (
 echo %%d | findstr /I /E /C:".incomplete" >nul
 if errorlevel 1 (
  if exist "%%d\Roaming\Cursor" (
   set /a i+=1
   echo "!i!. %%d"
   set file!i!=%%d
  )
 )
)
popd

if !i! equ 0 (
    echo "No completed backup folder found."
    pause
    goto menu
)

echo.
set /p "num=Select the number to restore: "

set "SEL=!file%num%!"

if "!SEL!"=="" (
 echo "Invalid selection"
 pause
 goto menu
)

set "SOURCE=%BACKUP_ROOT%\!SEL!"
if not exist "!SOURCE!\Roaming\Cursor" (
    echo "[ERR] Selected backup does not contain Roaming\Cursor."
    pause
    goto menu
)

echo.
echo "Selected backup: !SEL!"
echo "Restore replaces the backed-up Cursor data scope instead of merging it."
echo "A pre-restore safety backup will be created first."
set /p "CONFIRM=Continue restore? (Y/N): "
if /i not "!CONFIRM!"=="Y" goto menu

:: Timestamp shared by safety backup and temporary old folders.
for /f %%a in ('powershell -NoProfile -Command "Get-Date -Format 'yyyy-MM-dd_HHmmss'"') do set "STAMP=%%a"
set "SAFETY_NAME=pre-restore_!STAMP!"
set "SAFETY_TEMP=%BACKUP_ROOT%\!SAFETY_NAME!.incomplete"
set "SAFETY_FINAL=%BACKUP_ROOT%\!SAFETY_NAME!"

if exist "!SAFETY_TEMP!" (
    echo "[ERR] Pre-restore temporary folder already exists."
    pause
    exit /b 1
)
if exist "!SAFETY_FINAL!" (
    echo "[ERR] Pre-restore backup folder already exists."
    pause
    exit /b 1
)

mkdir "!SAFETY_TEMP!"
if not exist "!SAFETY_TEMP!" (
    echo "[ERR] Could not create pre-restore safety backup folder."
    pause
    exit /b 1
)

echo.
echo "[1/4] Creating pre-restore safety backup..."
set "SAFETY_FAILED=0"

if exist "%APPDATA%\Cursor" (
    robocopy "%APPDATA%\Cursor" "!SAFETY_TEMP!\Roaming\Cursor" /E /R:1 /W:1 /XD User\WebStorage User\CachedData User\History User\logs logs Cache >nul
    set "RC=!errorlevel!"
    if !RC! geq 8 (
        set "SAFETY_FAILED=1"
        echo "[ERR] Failed to back up current Roaming Cursor data (robocopy exit code !RC!)."
    )
)

if exist "%LOCALAPPDATA%\Cursor" (
    robocopy "%LOCALAPPDATA%\Cursor" "!SAFETY_TEMP!\Local\Cursor" /E /R:1 /W:1 /XD Cache GPUCache "Code Cache" "Service Worker" Crashpad >nul
    set "RC=!errorlevel!"
    if !RC! geq 8 (
        set "SAFETY_FAILED=1"
        echo "[ERR] Failed to back up current Local Cursor data (robocopy exit code !RC!)."
    )
)

if exist "%USERPROFILE%\.cursor" (
    robocopy "%USERPROFILE%\.cursor" "!SAFETY_TEMP!\User\.cursor" /E /R:1 /W:1 /XD user-data >nul
    set "RC=!errorlevel!"
    if !RC! geq 8 (
        set "SAFETY_FAILED=1"
        echo "[ERR] Failed to back up current .cursor data (robocopy exit code !RC!)."
    )
)

if "!SAFETY_FAILED!"=="1" (
    echo "Restore aborted because the current state could not be backed up safely."
    echo "Incomplete safety backup kept at: !SAFETY_NAME!.incomplete"
    pause
    exit /b 1
)

move "!SAFETY_TEMP!" "!SAFETY_FINAL!" >nul
if errorlevel 1 (
    echo "[ERR] Could not finalize pre-restore safety backup."
    echo "Restore was not started."
    pause
    exit /b 1
)

echo "Safety backup: !SAFETY_NAME!"

:: Replace only scopes that exist in the selected backup.
set "RESTORE_LOCAL=0"
set "RESTORE_USER=0"
if exist "!SOURCE!\Local\Cursor" set "RESTORE_LOCAL=1"
if exist "!SOURCE!\User\.cursor" set "RESTORE_USER=1"

set "OLD_ROAMING=%APPDATA%\Cursor.cursor-backup-old-!STAMP!"
set "OLD_LOCAL=%LOCALAPPDATA%\Cursor.cursor-backup-old-!STAMP!"
set "OLD_USER=%USERPROFILE%\.cursor.cursor-backup-old-!STAMP!"
set "HAD_ROAMING=0"
set "HAD_LOCAL=0"
set "HAD_USER=0"

echo.
echo "[2/4] Moving current data aside..."

if exist "%APPDATA%\Cursor" (
    move "%APPDATA%\Cursor" "!OLD_ROAMING!" >nul
    if errorlevel 1 (
        echo "[ERR] Could not move current Roaming Cursor data aside."
        pause
        exit /b 1
    )
    set "HAD_ROAMING=1"
)

if "!RESTORE_LOCAL!"=="1" if exist "%LOCALAPPDATA%\Cursor" (
    move "%LOCALAPPDATA%\Cursor" "!OLD_LOCAL!" >nul
    if errorlevel 1 (
        if "!HAD_ROAMING!"=="1" move "!OLD_ROAMING!" "%APPDATA%\Cursor" >nul
        echo "[ERR] Could not move current Local Cursor data aside. Current data was restored."
        pause
        exit /b 1
    )
    set "HAD_LOCAL=1"
)

if "!RESTORE_USER!"=="1" if exist "%USERPROFILE%\.cursor" (
    move "%USERPROFILE%\.cursor" "!OLD_USER!" >nul
    if errorlevel 1 (
        if "!HAD_LOCAL!"=="1" move "!OLD_LOCAL!" "%LOCALAPPDATA%\Cursor" >nul
        if "!HAD_ROAMING!"=="1" move "!OLD_ROAMING!" "%APPDATA%\Cursor" >nul
        echo "[ERR] Could not move current .cursor data aside. Current data was restored."
        pause
        exit /b 1
    )
    set "HAD_USER=1"
)

echo.
echo "[3/4] Restoring selected backup..."
set "RESTORE_FAILED=0"

robocopy "!SOURCE!\Roaming\Cursor" "%APPDATA%\Cursor" /E /R:1 /W:1 >nul
set "RC=!errorlevel!"
if !RC! geq 8 (
    set "RESTORE_FAILED=1"
    echo "[ERR] Roaming restore failed (robocopy exit code !RC!)."
)

if "!RESTORE_LOCAL!"=="1" (
    robocopy "!SOURCE!\Local\Cursor" "%LOCALAPPDATA%\Cursor" /E /R:1 /W:1 >nul
    set "RC=!errorlevel!"
    if !RC! geq 8 (
        set "RESTORE_FAILED=1"
        echo "[ERR] Local restore failed (robocopy exit code !RC!)."
    )
)

if "!RESTORE_USER!"=="1" (
    robocopy "!SOURCE!\User\.cursor" "%USERPROFILE%\.cursor" /E /R:1 /W:1 >nul
    set "RC=!errorlevel!"
    if !RC! geq 8 (
        set "RESTORE_FAILED=1"
        echo "[ERR] .cursor restore failed (robocopy exit code !RC!)."
    )
)

:: Normal backups intentionally exclude workspaceStorage. Preserve the current one in that case.
if not exist "!SOURCE!\Roaming\Cursor\User\workspaceStorage" if "!HAD_ROAMING!"=="1" if exist "!OLD_ROAMING!\User\workspaceStorage" (
    robocopy "!OLD_ROAMING!\User\workspaceStorage" "%APPDATA%\Cursor\User\workspaceStorage" /E /R:1 /W:1 >nul
    set "RC=!errorlevel!"
    if !RC! geq 8 (
        set "RESTORE_FAILED=1"
        echo "[ERR] Failed to preserve current workspaceStorage (robocopy exit code !RC!)."
    )
)

:: user-data is intentionally excluded from .cursor backups, so preserve the current copy.
if "!RESTORE_USER!"=="1" if "!HAD_USER!"=="1" if exist "!OLD_USER!\user-data" (
    robocopy "!OLD_USER!\user-data" "%USERPROFILE%\.cursor\user-data" /E /R:1 /W:1 >nul
    set "RC=!errorlevel!"
    if !RC! geq 8 (
        set "RESTORE_FAILED=1"
        echo "[ERR] Failed to preserve current .cursor\user-data (robocopy exit code !RC!)."
    )
)

if "!RESTORE_FAILED!"=="1" (
    echo.
    echo "Restore failed. Rolling back current data..."
    if exist "%APPDATA%\Cursor" rmdir /s /q "%APPDATA%\Cursor"
    if "!RESTORE_LOCAL!"=="1" if exist "%LOCALAPPDATA%\Cursor" rmdir /s /q "%LOCALAPPDATA%\Cursor"
    if "!RESTORE_USER!"=="1" if exist "%USERPROFILE%\.cursor" rmdir /s /q "%USERPROFILE%\.cursor"

    if "!HAD_ROAMING!"=="1" move "!OLD_ROAMING!" "%APPDATA%\Cursor" >nul
    if "!HAD_LOCAL!"=="1" move "!OLD_LOCAL!" "%LOCALAPPDATA%\Cursor" >nul
    if "!HAD_USER!"=="1" move "!OLD_USER!" "%USERPROFILE%\.cursor" >nul

    echo "Status: FAILED - previous data rollback attempted."
    echo "Safety backup remains at: !SAFETY_NAME!"
    pause
    exit /b 1
)

echo.
echo "[4/4] Cleaning temporary old data..."
if "!HAD_ROAMING!"=="1" if exist "!OLD_ROAMING!" rmdir /s /q "!OLD_ROAMING!"
if "!HAD_LOCAL!"=="1" if exist "!OLD_LOCAL!" rmdir /s /q "!OLD_LOCAL!"
if "!HAD_USER!"=="1" if exist "!OLD_USER!" rmdir /s /q "!OLD_USER!"

echo.
echo "===== Restore Completed ====="
echo "Status        : SUCCESS"
echo "Restored from : !SEL!"
echo "Safety backup : !SAFETY_NAME!"
echo.
pause
exit /b 0