# CursorAI Backup Tool

Windows 환경에서 Cursor 에디터의 사용자 데이터를 백업·복원하는 도구입니다.

v2에서는 기존 배치 스크립트의 백업/복원 정책을 정비하고, 같은 정책을 사용하는 C# WinForms GUI를 추가합니다. 기존 BAT 파일은 호환 및 수동 사용을 위해 유지합니다.

v2 정책과 작업 순서는 `V2_PLAN.md`를 참고하세요.

## WinForms GUI

프로젝트 위치:

`src\CursorAI.BackupTool\CursorAI.BackupTool.csproj`

GUI 범위는 다음 기능으로 제한합니다.

- 일반 백업
- 전체 AI 백업 (`workspaceStorage` 포함)
- 완료/불완전/Legacy/pre-restore 백업 목록
- 백업 종류, 상태, 크기, 생성 시각 표시
- 선택 백업 복원
- 선택 백업 삭제
- 복원 전 현재 상태 자동 안전 백업
- 복원 실패 시 기존 데이터 롤백 시도

### 빌드

.NET 8 SDK가 설치된 Windows에서 저장소 루트 기준으로 실행합니다.

```bat
dotnet publish src\CursorAI.BackupTool\CursorAI.BackupTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

출력 폴더의 `CursorAI.BackupTool.exe`를 단독 실행할 수 있습니다. 백업 데이터는 EXE와 같은 위치의 `backups` 폴더에 저장됩니다.

현재 저장소 작업 환경에서는 Windows/.NET SDK 실행 검증을 직접 수행하지 못했으므로, main 병합 전 실제 Windows에서 빌드와 백업/복원 검증이 필요합니다.

## 백업 정책

### Normal backup — 기본값

용량 증가를 줄이기 위한 기본 백업입니다.

- `%APPDATA%\Cursor`
  - `workspaceStorage`, WebStorage, CachedData, History, 로그 및 Cache 제외
- `%LOCALAPPDATA%\Cursor`
  - Cache, GPUCache, Code Cache, Service Worker, Crashpad 제외
- `%USERPROFILE%\.cursor`
  - `user-data` 제외
- Cursor 버전
- 확장 ID 목록
- 가능한 경우 확장 ID + 버전 목록
- `backup-info.json`

### Full AI backup

Normal backup과 동일하지만 `%APPDATA%\Cursor\User\workspaceStorage`도 포함합니다.

`workspaceStorage`의 크기에 따라 백업 용량이 크게 증가할 수 있으므로 GUI에서도 별도 경고 후 실행합니다.

## 백업 생성 방식

백업은 먼저 다음 이름으로 생성합니다.

`yyyy-MM-dd_HHmmss.incomplete`

필수 데이터 복사가 정상 완료된 경우에만 다음 정상 백업명으로 확정합니다.

`yyyy-MM-dd_HHmmss`

결과 상태:

- `SUCCESS`: 필수 백업과 부가 정보 생성 완료
- `WARNING`: 백업은 사용 가능하지만 일부 부가 정보 또는 선택 데이터 누락
- `FAILED`: 필수 데이터 복사 또는 백업 확정 실패
- `INCOMPLETE`: 작업 도중 실패하거나 중단된 폴더
- `LEGACY`: v2 메타데이터가 없는 기존 백업

## 복원 정책

복원은 기존 Cursor 폴더 위에 단순 덮어쓰지 않습니다.

1. Cursor 실행 여부 확인 및 종료
2. 현재 상태를 `pre-restore_yyyy-MM-dd_HHmmss`로 안전 백업
3. 현재 대상 폴더를 임시 위치로 이동
4. 선택 백업을 새로 복사
5. 일반 백업에 없는 현재 `workspaceStorage` 보존
6. 백업 제외 대상인 현재 `.cursor\user-data` 보존
7. 복원 실패 시 새로 생성된 데이터를 제거하고 기존 폴더 롤백 시도
8. 성공 시 임시 기존 폴더 정리

`.incomplete` 백업은 복원할 수 없습니다.

## 백업 메타데이터

v2 백업에는 `backup-info.json`이 생성됩니다.

주요 항목:

- `backupVersion`
- `createdAt`
- `cursorVersion`
- `type`: `normal`, `full-ai`, `pre-restore`
- `workspaceStorageIncluded`
- `status`

## 기존 배치 도구

- `cursor-backup.bat`
  - 일반/전체 AI 백업
  - `.incomplete` staging 및 상태 판정
- `cursor-restore.bat`
  - pre-restore 안전 백업 후 교체형 복원
- `cursor-backups-manage.bat`
  - 백업 상태/종류/크기 목록 및 삭제
- `cursor_extensions_install.bat`
  - `extensions_with_versions.txt`가 있으면 버전 목록 우선 사용
  - 설치 성공/실패 개별 집계

## Legacy Marketplace 도구

- `cursor_market_vscode.bat`
  - Cursor 설치 폴더의 `product.json`에서 `extensionsGallery`를 VS Code Marketplace 설정으로 변경
- `cursor_market_cursor.bat`
  - 저장된 원본에서 `extensionsGallery` 항목만 현재 `product.json`에 복원

Cursor 설치 파일을 직접 수정하는 고급/Legacy 기능이며 핵심 백업/복원 기능과 분리해서 사용합니다.

## 요구 환경

- Windows 10 이상
- GUI 빌드: .NET 8 SDK
- GUI 실행: win-x64 self-contained publish 사용 시 별도 .NET Runtime 불필요
- Robocopy가 제공되는 Windows 환경
- Cursor 설치 위치 탐색 순서:
  1. `%LOCALAPPDATA%\Programs\Cursor\Cursor.exe`
  2. `C:\Program Files\Cursor\Cursor.exe`
  3. `C:\Program Files (x86)\Cursor\Cursor.exe`
  4. PATH의 `cursor`

`sqlite3.exe`는 더 이상 필요하지 않고 백업 DB에 자동 `VACUUM`을 수행하지 않습니다.

## 주의 사항

Cursor 데이터 및 `.cursor`에는 프로젝트 경로, AI transcript, MCP 설정, API key/token 등 민감정보가 포함될 수 있습니다. `backups` 폴더는 안전한 위치에 보관해야 합니다.

복원은 파일 이동과 삭제를 포함하므로 GUI/BAT 모두 실제 사용 전에 정상 백업이 존재하는지 확인하는 것을 권장합니다.
