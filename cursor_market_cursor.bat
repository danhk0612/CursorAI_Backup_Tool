@echo off
setlocal EnableDelayedExpansion

:: Legacy utility: product.json (user install, then system install)
set "FILE="
if exist "%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" ( set "FILE=%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files (x86)\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files (x86)\Cursor\resources\app\product.json" )

echo.
echo "===== LEGACY: Cursor Marketplace Restore ====="
echo "Only the extensionsGallery section will be restored from product.json.backup."
echo.

if not defined FILE (
 echo "Cursor product.json not found."
 pause
 exit /b 1
)

if not exist "%FILE%.backup" (
 echo "No product.json.backup file found."
 pause
 exit /b 1
)

tasklist | find /I "cursor.exe" >nul
if !errorlevel! equ 0 (
 echo "Please close Cursor before restoring Marketplace settings."
 pause
 exit /b 1
)

echo "Current file: %FILE%"
echo "Backup file : %FILE%.backup"
set /p "CONFIRM=Restore extensionsGallery only? (Y/N): "
if /i not "!CONFIRM!"=="Y" exit /b 0

set "PRODUCT_FILE=%FILE%"
set "PRODUCT_BACKUP=%FILE%.backup"
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $path=$env:PRODUCT_FILE; $backupPath=$env:PRODUCT_BACKUP; $tmp=$path+'.cursor-backup-tool.tmp'; $current=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json; $original=Get-Content -LiteralPath $backupPath -Raw | ConvertFrom-Json; if ($null -eq $original.extensionsGallery) { throw 'extensionsGallery not found in backup' }; $current.extensionsGallery=$original.extensionsGallery; $current | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $tmp -Encoding UTF8; Move-Item -LiteralPath $tmp -Destination $path -Force" >nul 2>&1
if errorlevel 1 (
 echo "[ERR] Failed to restore extensionsGallery. Current product.json was left unchanged when possible."
 pause
 exit /b 1
)

echo.
echo "Restored extensionsGallery from product.json.backup."
echo "Other current product.json fields were preserved."
echo "Restart Cursor."
echo.
pause
exit /b 0
