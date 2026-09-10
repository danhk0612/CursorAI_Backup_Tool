# Cursor Backup Scripts

Windows 환경에서 Cursor 에디터의 사용자 데이터를
간편하게 백업·복원·확장 재설치·Marketplace 설정 변경을 할 수 있는
배치 스크립트 모음입니다.

v2 개선 방향과 작업 순서는 `V2_PLAN.md`를 참고하세요.

## 구성 파일

- `cursor-backup.bat`  
  - 현재 로그인한 사용자의 Cursor 설정/데이터를
    날짜별 폴더(`yyyy-MM-dd_HHmmss` 형식)로 백업합니다.
  - 작업 중에는 `.incomplete` 접미사가 붙고 필수 데이터 복사가 완료된 뒤 정상 백업 폴더명으로 확정됩니다.
  - 기본 `Normal backup`은 용량 증가를 막기 위해 `workspaceStorage`를 제외합니다.
  - `Full AI backup`을 선택하면 `workspaceStorage`까지 포함합니다.

- `cursor-restore.bat`  
  - `backups` 폴더 아래의 완료된 백업 폴더 중 하나를 선택해서
    현재 사용자 프로필로 복원합니다.
  - `.incomplete` 백업은 복원 목록에서 제외됩니다.

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
  - 위 위치에서 찾지 못하면 PATH의 `cursor` 명령을 사용합니다.
- `sqlite3.exe`:
  - 현재 버전에서는 `cursor-backup.bat`와 같은 폴더에 `sqlite3.exe`가 있어야
    VACUUM 최적화가 동작합니다.
  - 이 의존성은 v2 후속 작업에서 제거할 예정입니다.

## 사용 방법

### 1) 백업

1. `cursor-backup.bat`를 실행합니다.
2. 백업 모드를 선택합니다.
   - `1. Normal backup` — 기본값. `workspaceStorage` 제외
   - `2. Full AI backup` — `workspaceStorage` 포함. 백업 용량이 크게 증가할 수 있음
3. Cursor가 실행 중이면 종료 여부를 묻습니다.
   - `Y` 입력 시: 실행 중인 `cursor.exe` 프로세스를 강제 종료 후 백업 진행
   - `N` 입력 시: 종료하지 않고 계속 진행하지만 일부 파일이 사용 중일 수 있음
4. 백업은 먼저 `backups\yyyy-MM-dd_HHmmss.incomplete`에 생성됩니다.
5. 필수 데이터 복사에 실패하면 `.incomplete` 상태로 남고 `FAILED`로 표시됩니다.
6. 필수 데이터 복사가 완료되면 `backups\yyyy-MM-dd_HHmmss`로 확정되고 결과가 `SUCCESS` 또는 `WARNING`으로 표시됩니다.

### 백업 범위

#### Normal backup

기본 백업입니다.

- `%APPDATA%\Cursor`
  - `workspaceStorage`, WebStorage, CachedData, History, 로그 및 Cache 제외
- `%LOCALAPPDATA%\Cursor`
  - Cache, GPUCache, Code Cache, Service Worker, Crashpad 제외
- `%USERPROFILE%\.cursor`
  - `user-data` 제외

#### Full AI backup

Normal backup과 동일하지만 `%APPDATA%\Cursor\User\workspaceStorage`도 포함합니다.
프로젝트별 Workspace 상태와 AI 작업 복구 가능성을 높이기 위한 선택 모드이며 저장 공간을 더 사용할 수 있습니다.

### 2) 복원

1. Cursor를 종료합니다.
2. `cursor-restore.bat`를 실행합니다.
3. `backups` 폴더 아래 완료된 백업 목록에서 번호를 선택하면,
   - `Roaming\Cursor` → `%APPDATA%\Cursor`
   - `Local\Cursor`   → `%LOCALAPPDATA%\Cursor`
   - `User\.cursor`   → `%USERPROFILE%\.cursor`
   로 복사됩니다.

현재 복원은 기존 데이터에 덮어쓰는 방식입니다. 안전한 교체형 복원 및 복원 전 자동 백업은 v2 후속 작업에서 개선할 예정입니다.

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

- **민감정보**
  - `.cursor` 및 Cursor 사용자 데이터에는 프로젝트 경로, AI transcript, MCP 설정, API key/token 등 민감정보가 포함될 수 있습니다.
  - `backups` 폴더는 안전한 위치에 보관하세요.
- **백업 결과**
  - `SUCCESS`: 필수 백업과 부가 정보 생성 완료
  - `WARNING`: 백업은 완료됐지만 일부 선택 경로 또는 부가 정보가 누락됨
  - `FAILED`: 필수 데이터 복사 실패 또는 백업 확정 실패
- **여러 개의 Cursor.exe 종료**
  - Cursor는 여러 프로세스를 사용하는 구조라,
    `taskkill /IM cursor.exe /F` 실행 시 여러 PID가 한꺼번에 종료되는 것이 정상입니다.
