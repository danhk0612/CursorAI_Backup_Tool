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

echo.
echo "Restore Cursor data"

robocopy "%SOURCE%\Roaming\Cursor" "%APPDATA%\Cursor" /E /R:1 /W:1
set "RC_ERR=%errorlevel%"
robocopy "%SOURCE%\Local\Cursor" "%LOCALAPPDATA%\Cursor" /E /R:1 /W:1
if %errorlevel% gtr %RC_ERR% set "RC_ERR=%errorlevel%"
robocopy "%SOURCE%\User\.cursor" "%USERPROFILE%\.cursor" /E /R:1 /W:1
if %errorlevel% gtr %RC_ERR% set "RC_ERR=%errorlevel%"

if %RC_ERR% gtr 7 echo "Warning: Some files may not have been copied (robocopy exit code %RC_ERR%)."

echo.
echo "===== Restore Completed ====="

pause