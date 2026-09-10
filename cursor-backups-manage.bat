@echo off
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "BACKUP_ROOT=%ROOT%backups"

:menu
cls
echo.
echo "===== Cursor Backup Manager ====="
echo.

if not exist "%BACKUP_ROOT%" mkdir "%BACKUP_ROOT%"

set "i=0"
echo "No.  Type          Status      Size(MB)  Folder"
echo "----  ------------  ----------  --------  ------------------------------"

for /f "usebackq tokens=1-5 delims=|" %%a in (`powershell -NoProfile -Command "$root=$env:BACKUP_ROOT; if(Test-Path -LiteralPath $root){ Get-ChildItem -LiteralPath $root -Directory | Sort-Object LastWriteTime -Descending | ForEach-Object { $d=$_; $type='legacy'; $status='UNKNOWN'; $manifest=Join-Path $d.FullName 'backup-info.json'; if($d.Name.EndsWith('.incomplete',[System.StringComparison]::OrdinalIgnoreCase)){ $status='INCOMPLETE' } elseif(Test-Path -LiteralPath $manifest){ try{ $m=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json; if($m.type){$type=[string]$m.type}; if($m.status){$status=[string]$m.status}else{$status='COMPLETE'} } catch { $status='INVALID' } } elseif(Test-Path -LiteralPath (Join-Path $d.FullName 'Roaming\Cursor')){ $status='COMPLETE' }; $sum=(Get-ChildItem -LiteralPath $d.FullName -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum; if($null -eq $sum){$sum=0}; $mb=[math]::Round($sum/1MB,1); '{0}|{1}|{2}|{3}|{4}' -f $d.Name,$type,$status,$mb,$d.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') } }"`) do (
 set /a i+=1
 set "folder!i!=%%a"
 set "type!i!=%%b"
 set "status!i!=%%c"
 set "size!i!=%%d"
 echo "!i!.   %%b  %%c  %%d  %%a"
)

if !i! equ 0 echo "No backup folders found."

echo.
echo "1. Refresh"
echo "2. Delete backup"
echo "3. Exit"
echo.
set /p "CHOICE=Choice: "

if "!CHOICE!"=="1" goto menu
if "!CHOICE!"=="2" goto delete_backup
if "!CHOICE!"=="3" exit /b 0
goto menu

:delete_backup
if !i! equ 0 (
 echo "No backup to delete."
 pause
 goto menu
)

echo.
set /p "NUM=Backup number to delete: "
set "TARGET=!folder%NUM%!"
if "!TARGET!"=="" (
 echo "Invalid selection."
 pause
 goto menu
)

set "TARGET_PATH=%BACKUP_ROOT%\!TARGET!"
echo.
echo "Delete: !TARGET!"
echo "This permanently deletes the selected backup folder."
set /p "CONFIRM=Type Y to delete: "
if /i not "!CONFIRM!"=="Y" goto menu

rmdir /s /q "!TARGET_PATH!"
if exist "!TARGET_PATH!" (
 echo "[ERR] Failed to delete the backup folder."
 pause
 goto menu
)

echo "Deleted: !TARGET!"
pause
goto menu
