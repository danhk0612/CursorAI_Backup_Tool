@echo off
setlocal EnableDelayedExpansion

:: Legacy utility: product.json (user install, then system install)
set "FILE="
if exist "%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" ( set "FILE=%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files (x86)\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files (x86)\Cursor\resources\app\product.json" )

echo.
echo "===== LEGACY: Cursor Marketplace -> VS Code ====="
echo "This utility directly modifies Cursor's installed product.json."
echo "Cursor updates may overwrite this change."
echo.

if not defined FILE (
 echo "Cursor product.json not found."
 pause
 exit /b 1
)

tasklist | find /I "cursor.exe" >nul
if !errorlevel! equ 0 (
 echo "Please close Cursor before changing Marketplace settings."
 pause
 exit /b 1
)

echo "File: %FILE%"
set /p "CONFIRM=Continue? (Y/N): "
if /i not "!CONFIRM!"=="Y" exit /b 0

:: Preserve the first original backup. Do not overwrite it on repeated switches.
if not exist "%FILE%.backup" (
 copy "%FILE%" "%FILE%.backup" >nul
 if errorlevel 1 (
  echo "[ERR] Could not create product.json.backup. Administrator permission may be required."
  pause
  exit /b 1
 )
 echo "Original product.json backed up."
) else (
 echo "Existing product.json.backup preserved."
)

set "PRODUCT_FILE=%FILE%"
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $path=$env:PRODUCT_FILE; $tmp=$path+'.cursor-backup-tool.tmp'; $j=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json; $j.extensionsGallery=[pscustomobject][ordered]@{ serviceUrl='https://marketplace.visualstudio.com/_apis/public/gallery'; cacheUrl='https://vscode.blob.core.windows.net/gallery/index'; itemUrl='https://marketplace.visualstudio.com/items' }; $j | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $tmp -Encoding UTF8; Move-Item -LiteralPath $tmp -Destination $path -Force" >nul 2>&1
if errorlevel 1 (
 echo "[ERR] Failed to update product.json. The original backup was not changed."
 pause
 exit /b 1
)

echo.
echo "Switched extensionsGallery to VS Code Marketplace."
echo "Restart Cursor."
echo.
pause
exit /b 0
