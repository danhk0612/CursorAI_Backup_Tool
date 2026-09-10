# Cursor Backup Scripts

Windows 환경에서 Cursor 에디터의 사용자 데이터를 백업·복원하는 도구입니다.

v2 개선 방향과 작업 순서는 `V2_PLAN.md`, 새 아카이브 형식은 `V3_ARCHIVE_FORMAT.md`를 참고하세요.

## GUI

`src/CursorAI.BackupTool`에 .NET 8 WinForms GUI가 있습니다.

주요 기능:

- Normal backup / Full AI backup 선택
- v3 영역별 아카이브 백업
- 기존 v2/Legacy 폴더형 백업 복원 호환
- 백업 목록과 종류·상태·크기·생성 시각 표시
- 백업 / 복원 / 삭제 / 새로 고침
- 백업·복원 중 현재 단계와 경과 시간 표시
- Cursor가 실행 중인 상태에서 백업/복원 시작 시 알림 후 자동 종료 선택
- 복원 전 `pre-restore_...` 안전 백업 생성
- 실패 시 기존 데이터 롤백 시도
- 동시에 두 개의 GUI가 실행되지 않도록 단일 실행 제한
- 백업/복원 중 창 닫기 차단

### Cursor 설치 유형 대응

GUI는 기존 배치 도구와 동일하게 사용자 설치와 시스템 설치를 모두 탐색합니다.

- 사용자 설치: `%LOCALAPPDATA%\Programs\Cursor`
- 시스템 설치: `%ProgramFiles%\Cursor`
- 32비트 Program Files: `%ProgramFiles(x86)%\Cursor`

Cursor 버전은 발견한 `Cursor.exe`의 파일 버전 정보에서 읽습니다. 버전 확인을 위해 Cursor 본체를 실행하지 않습니다.

확장 목록을 만들 때는 각 설치 폴더의 `resources\app\bin\cursor.cmd`를 사용하고, 찾지 못하면 PATH의 `cursor.cmd`를 확인합니다. CLI를 찾지 못해도 확장 실파일 백업은 그대로 유지됩니다.

### Cursor 실행 중 처리

백업 또는 복원 버튼을 눌렀을 때 Cursor가 실행 중이면 확인창을 표시합니다.

`예`를 선택하면 GUI가 Cursor 프로세스를 종료하고, 완전히 종료된 것을 확인한 뒤 작업을 시작합니다. 종료하지 못하면 백업/복원은 시작하지 않습니다.

백업 과정에서는 `Cursor.exe`를 CLI 용도로 실행하지 않으므로 백업 도중 Cursor가 다시 실행되지 않도록 했습니다.

### 진행 상태

백업/복원 중에는 현재 처리 단계와 작업 경과 시간이 계속 표시되고, 진행 바는 작업이 계속 진행 중임을 보여주는 Marquee 방식으로 동작합니다.

백업 자체가 끝나면 진행 바와 경과 시간은 즉시 종료되고, 그 뒤 백업 목록을 별도로 새로 고칩니다.

## GUI 빌드

```powershell
cd src\CursorAI.BackupTool
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

출력 예:

```text
src\CursorAI.BackupTool\bin\Release\net8.0-windows\win-x64\publish\
```

## v3 아카이브 백업

새 GUI 백업은 Cursor의 수많은 파일을 백업 폴더에 그대로 복제하지 않고 다음과 같은 몇 개의 아카이브 파일로 저장합니다.

```text
backups\yyyy-MM-dd_HHmmss\
  backup-info.json
  roaming.zip
  local.zip                 # 해당 경로가 있을 때
  cursor-user.zip           # .cursor가 있을 때
  cursor_version.txt        # 조회 가능할 때
  extensions.txt            # CLI 조회 가능할 때
  extensions_with_versions.txt
```

압축은 별도 7-Zip 프로그램 없이 .NET 내장 ZIP과 빠른 압축 모드를 사용합니다. 목적은 최고 압축률보다 파일 개수를 크게 줄여 백업 삭제·전송·목록 조회를 개선하는 것입니다.

v3 `backup-info.json`에는 `backupVersion: 3`, `storageFormat: archive-v3`, `totalBytes`가 기록됩니다. `totalBytes`를 이용하므로 새 백업의 목록 크기 표시는 백업 내부 파일 전체를 다시 재귀 탐색하지 않습니다.

## 백업 모드

### Normal backup

기본 백업입니다.

- `%APPDATA%\Cursor`
  - `%APPDATA%\Cursor\WorkspaceStorage` 제외
  - `%APPDATA%\Cursor\User\workspaceStorage` 제외
  - 기존 WebStorage, CachedData, History, 로그 및 Cache 제외 정책 유지
- `%LOCALAPPDATA%\Cursor`
  - 존재하는 경우 백업
  - Cache, GPUCache, Code Cache, Service Worker, Crashpad 제외
- `%USERPROFILE%\.cursor`
  - `user-data` 제외
  - `extensions`는 실파일 그대로 포함
  - `projects`, `plugins`, `skills-cursor`, `plans` 등 나머지 데이터 포함

`.cursor\extensions`는 Marketplace 연결 상태, 삭제된 확장, 특정 버전, 수동 VSIX/오프라인 설치 확장을 복구할 수 있도록 Normal/Full AI 모두 실파일을 백업합니다.

### Full AI backup

Normal backup 범위에 더해 다음 두 위치를 모두 포함합니다.

- `%APPDATA%\Cursor\WorkspaceStorage`
- `%APPDATA%\Cursor\User\workspaceStorage`

프로젝트별 Workspace 상태와 AI 작업 복구 가능성을 높이는 대신 백업 용량과 시간이 크게 증가할 수 있습니다.

## 백업 처리

- 백업 폴더: `backups\yyyy-MM-dd_HHmmss`
- 작업 중: `backups\yyyy-MM-dd_HHmmss.incomplete`
- 새 GUI 백업 형식: v3 `archive-v3`
- 완료된 백업에는 `backup-info.json` 생성
- `%APPDATA%\Cursor`는 복원에 필요한 필수 범위로 취급하며 없으면 백업 실패
- `%LOCALAPPDATA%\Cursor`가 없는 환경은 정상적으로 허용하고 안내만 표시
- Cursor 버전/확장 목록 같은 부가 메타데이터 조회 실패는 백업 데이터 자체가 정상이라면 안내로 표시
- `.cursor` 아카이브 생성 실패는 백업 실패
- `sqlite3.exe` 및 자동 `VACUUM`은 사용하지 않음

백업 결과는 다음 중 하나입니다.

- `SUCCESS`: 필수 백업 완료
- `WARNING`: 필수 백업은 완료됐지만 `.cursor` 같은 주요 선택 범위가 존재하지 않는 등 복구 범위가 줄어든 경우
- `FAILED`: 필수 Roaming 데이터, `.cursor` 아카이브 생성 또는 백업 확정 실패

## 복원 처리

1. Cursor 종료 확인
2. 현재 상태를 v3 `pre-restore_yyyy-MM-dd_HHmmss` 아카이브로 안전 백업
3. 현재 Cursor 데이터를 임시 위치로 이동
4. 선택한 백업을 새로 복원
5. v3 백업이면 `roaming.zip`, `local.zip`, `cursor-user.zip`을 해제
6. 기존 v2/Legacy 백업이면 기존 폴더형 구조를 Robocopy로 복원
7. 선택한 백업에 없는 현재 `%APPDATA%\Cursor\WorkspaceStorage` 보존
8. 선택한 백업에 없는 현재 `%APPDATA%\Cursor\User\workspaceStorage` 보존
9. 백업 제외 대상인 `.cursor\user-data` 보존
10. 위 제외 데이터는 같은 볼륨에서 대용량 재복사하지 않고 디렉터리 이동으로 제자리 복원
11. 어느 단계든 실패하면 이동된 제외 데이터를 원래 임시 폴더로 되돌린 뒤 기존 Cursor 데이터 전체 롤백 시도

`.incomplete` 백업은 복원할 수 없습니다.

## Legacy 배치 도구

기존 배치 파일도 저장소에 남아 있습니다.

- `cursor-backup.bat`
- `cursor-restore.bat`
- `cursor_extensions_install.bat`
- `cursor-backups-manage.bat`
- `cursor_market_vscode.bat`
- `cursor_market_cursor.bat`

Marketplace 스크립트는 Legacy/고급 기능이며 Cursor 설치의 `product.json`을 직접 수정하므로 일반 백업/복원 기능과 분리해서 사용해야 합니다.

## 주의 사항

`.cursor` 및 Cursor 사용자 데이터에는 프로젝트 경로, AI transcript, MCP 설정, API key/token 등 민감정보가 포함될 수 있습니다. `backups` 폴더는 안전한 위치에 보관하세요.

ZIP 아카이브 방식은 백업 폴더의 파일 개수와 삭제/전송 비용을 크게 줄이지만, 복원 시 실제 Cursor 데이터 파일은 다시 생성해야 하므로 복원 시간 자체를 완전히 제거하지는 못합니다.