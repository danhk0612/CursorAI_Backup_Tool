@echo off
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "BACKUP_ROOT=%ROOT%backups"

:: Cursor executable (user install, then system install, else PATH)
set "CURSOR_CMD=cursor"
if exist "%LOCALAPPDATA%\Programs\Cursor\Cursor.exe" ( set "CURSOR_CMD=%LOCALAPPDATA%\Programs\Cursor\Cursor.exe" ) else if exist "C:\Program Files\Cursor\Cursor.exe" ( set "CURSOR_CMD=C:\Program Files\Cursor\Cursor.exe" ) else if exist "C:\Program Files (x86)\Cursor\Cursor.exe" ( set "CURSOR_CMD=C:\Program Files (x86)\Cursor\Cursor.exe" )

:menu
cls
echo.
echo "===== Cursor Extensions Install ====="
echo.
echo "1. Reinstall extensions from backup"
echo "2. Exit"
echo.
set /p "menu=Choice: "

if "%menu%"=="1" goto install
if "%menu%"=="2" exit
goto menu

:install
cd /d "%ROOT%"

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
for /f "delims=" %%f in ('dir /b /ad /o-d 2^>nul') do (
 echo %%f | findstr /I /E /C:".incomplete" >nul
 if errorlevel 1 (
  if exist "%%f\Roaming\Cursor" (
   set /a i+=1
   echo "!i!. %%f"
   set folder!i!=%%f
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
set /p "num=Select backup number to use for extension list: "

set "SEL=!folder%num%!"

if "!SEL!"=="" (
 echo "Invalid selection"
 pause
 goto menu
)

set "EXTFILE=%BACKUP_ROOT%\!SEL!\extensions_with_versions.txt"
set "EXTMODE=versioned"
if not exist "!EXTFILE!" (
 set "EXTFILE=%BACKUP_ROOT%\!SEL!\extensions.txt"
 set "EXTMODE=id-only"
)

if not exist "!EXTFILE!" (
 echo "No extension list found in the selected backup."
 pause
 goto menu
)

echo.
echo "===== Extension Install ====="
echo "Backup: !SEL!"
echo "List mode: !EXTMODE!"
echo.

set "EXT_TOTAL=0"
set "EXT_SUCCESS=0"
set "EXT_FAILED=0"

for /f "usebackq delims=" %%i in ("!EXTFILE!") do (
 if not "%%i"=="" (
  set /a EXT_TOTAL+=1
  echo "Installing %%i"
  call "%CURSOR_CMD%" --install-extension "%%i"
  if errorlevel 1 (
   set /a EXT_FAILED+=1
   echo "[FAIL] %%i"
  ) else (
   set /a EXT_SUCCESS+=1
   echo "[OK] %%i"
  )
 )
)

echo.
echo "===== Install Result ====="
echo "Total   : !EXT_TOTAL!"
echo "Success : !EXT_SUCCESS!"
echo "Failed  : !EXT_FAILED!"
echo.

pause
goto menu
