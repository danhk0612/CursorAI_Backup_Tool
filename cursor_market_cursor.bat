@echo off
setlocal

:: product.json (user install, then system install)
set "FILE="
if exist "%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" ( set "FILE=%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files (x86)\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files (x86)\Cursor\resources\app\product.json" )

echo.
echo "===== Cursor Marketplace Restore ====="
echo.

if not defined FILE (
 echo "Cursor product.json not found."
 pause
 exit /b 1
)

if exist "%FILE%.backup" (
 copy "%FILE%.backup" "%FILE%" /Y >nul
 echo "Restored from backup file."
) else (
 echo "No backup file found."
)

echo.
echo "Restart Cursor."
echo.

pause
