@echo off
setlocal

:: product.json (user install, then system install)
set "FILE="
if exist "%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" ( set "FILE=%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files\Cursor\resources\app\product.json" )
if not defined FILE if exist "C:\Program Files (x86)\Cursor\resources\app\product.json" ( set "FILE=C:\Program Files (x86)\Cursor\resources\app\product.json" )

echo.
echo "===== Cursor Marketplace -> VSCode ====="
echo.

if not defined FILE (
 echo "Cursor product.json not found."
 pause
 exit /b 1
)

if not exist "%FILE%" (
 echo "product.json not found."
 pause
 exit /b 1
)

echo "Backing up existing file."
copy "%FILE%" "%FILE%.backup" >nul

powershell -Command ^
"(Get-Content '%FILE%' -Raw) -replace '\"extensionsGallery\":\s*\{[^}]+\}', '\"extensionsGallery\": { \"serviceUrl\": \"https://marketplace.visualstudio.com/_apis/public/gallery\", \"cacheUrl\": \"https://vscode.blob.core.windows.net/gallery/index\", \"itemUrl\": \"https://marketplace.visualstudio.com/items\" }' | Set-Content '%FILE%'"

echo.
echo "Switched to VSCode Marketplace."
echo "Restart Cursor."
echo.

pause
