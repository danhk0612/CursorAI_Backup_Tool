# Cursor Backup Scripts

Windows 환경에서 Cursor 에디터의 사용자 데이터를
간편하게 백업·복원·확장 재설치·Marketplace 설정 변경을 할 수 있는
배치 스크립트 모음입니다.

## 구성 파일

- `cursor-backup.bat`  
  - 현재 로그인한 사용자의 Cursor 설정/데이터를
    날짜별 폴더(`yyyy-MM-dd_HHmm` 형식)로 백업합니다.
  - 백업 경로 예: `backups\2026-03-13_1009\Roaming\Cursor`, `...\Local\Cursor`, `...\User\.cursor`

- `cursor-restore.bat`  
  - `backups` 폴더 아래의 백업 폴더 중 하나를 선택해서
    현재 사용자 프로필로 복원합니다.

- `cursor_extensions_install.bat`  
  - 선택한 백업 폴더의 `extensions.txt`를 읽어
    Cursor 확장(extensions)을 일괄 재설치합니다.

- `cursor_market_cursor.bat`  
  - Cursor의 `product.json`을 백업본(`product.json.backup`)으로 되돌려
    원래 Cursor Marketplace 설정으로 복구합니다.

- `cursor_market_vscode.bat`  
  - `product.json`의 `extensionsGallery` 설정을
    VSCode Marketplace로 변경합니다.

## 전제 조건

- OS: Windows 10 이상
- Cursor 설치:
  - 사용자 설치(`%LOCALAPPDATA%\Programs\Cursor`) 또는
  - 시스템 설치(`C:\Program Files\Cursor` / `C:\Program Files (x86)\Cursor`)
- `sqlite3.exe`:
  - `cursor-backup.bat`와 같은 폴더에 `sqlite3.exe`가 있어야
    VACUUM 최적화가 동작합니다.
- PATH:
  - `cursor-backup.bat`의 정보 파일 생성(3/5 단계)은
    `cursor --version`, `cursor --list-extensions`를 호출하므로
    `cursor` 명령이 PATH에서 인식되는 환경이 가장 안전합니다.

## 사용 방법

### 1) 백업

1. Cursor를 종료합니다. (백업 스크립트가 자동으로 종료 여부를 물어보기도 합니다.)
2. `cursor-backup.bat`를 더블클릭 실행합니다.
3. `Cursor is running. Close it for a safe backup? (Y/N):` 가 나오면
   - `Y` 입력 시: 실행 중인 `cursor.exe` 프로세스를 강제 종료 후 백업 진행
   - `N` 입력 시: 종료하지 않고 계속 진행하지만,
     일부 파일이 사용 중일 수 있다는 경고가 출력됩니다.
4. 완료되면 `Created folder: yyyy-MM-dd_HHmm` 메시지와 함께
   `backups\yyyy-MM-dd_HHmm` 이름의 백업 폴더가 생성됩니다.

### 2) 복원

1. Cursor를 종료합니다.
2. `cursor-restore.bat`를 실행합니다.
3. `backups` 폴더 아래 백업 목록에서 번호를 선택하면,
   - `Roaming\Cursor` → `%APPDATA%\Cursor`
   - `Local\Cursor`   → `%LOCALAPPDATA%\Cursor`
   - `User\.cursor`   → `%USERPROFILE%\.cursor`
   로 복사됩니다.

### 3) 확장 재설치

1. `cursor_extensions_install.bat`를 실행합니다.
2. `backups` 폴더 아래에서 확장 목록을 사용할 백업 폴더 번호를 선택합니다.
3. 선택한 폴더의 `extensions.txt`를 읽어,
   각 줄을 확장 ID로 보고 `cursor --install-extension` 을 순차 실행합니다.

### 4) Marketplace 설정 관련

- `cursor_market_vscode.bat`  
  - VSCode Marketplace를 사용하도록 `product.json`의 `extensionsGallery`를 수정합니다.

- `cursor_market_cursor.bat`  
  - `product.json.backup`이 있을 경우 원래 설정으로 되돌립니다.

두 스크립트 모두 다음 위치 순서대로 `product.json`을 찾습니다.

1. `%LOCALAPPDATA%\Programs\Cursor\resources\app\product.json`
2. `C:\Program Files\Cursor\resources\app\product.json`
3. `C:\Program Files (x86)\Cursor\resources\app\product.json`

## 주의 사항

- **경고 메시지**
  - `Warning: One or more Cursor data paths not found. Backup may be incomplete.`  
    → 세 데이터 경로 중 하나라도 없으면 나오는 경고이며, 백업은 계속 진행됩니다.
- **여러 개의 Cursor.exe 종료**
  - Cursor는 여러 프로세스를 사용하는 구조라,
    `taskkill /IM cursor.exe /F` 실행 시 여러 PID가 한꺼번에 종료되는 것이 정상입니다.
