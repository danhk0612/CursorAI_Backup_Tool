# Cursor Backup Scripts

Windows 환경에서 Cursor 에디터의 사용자 데이터를 백업·복원하는 도구입니다.

v2 개선 방향과 작업 순서는 `V2_PLAN.md`를 참고하세요.

## GUI

`src/CursorAI.BackupTool`에 .NET 8 WinForms GUI가 있습니다.

주요 기능:

- Normal backup / Full AI backup 선택
- 백업 목록과 종류·상태·크기·생성 시각 표시
- 백업 / 복원 / 삭제 / 새로 고침
- 백업·복원 중 현재 단계, 진행률, 경과 시간 표시
- Cursor가 실행 중인 상태에서 백업/복원 시작 시 알림 후 자동 종료 선택
- 복원 전 `pre-restore_...` 안전 백업 생성
- 실패 시 기존 데이터 롤백 시도

### Cursor 실행 중 처리

백업 또는 복원 버튼을 눌렀을 때 Cursor가 실행 중이면 확인창을 표시합니다.

`예`를 선택하면 GUI가 Cursor 프로세스를 종료하고, 완전히 종료된 것을 확인한 뒤 작업을 시작합니다. 종료하지 못하면 백업/복원은 시작하지 않습니다.

백업 과정에서 Cursor 버전과 확장 목록을 읽을 때는 `Cursor.exe` 본체를 직접 실행하지 않고 Cursor 설치 폴더의 `resources\app\bin\cursor.cmd` CLI 런처를 사용합니다. CLI 런처를 찾지 못하면 Cursor를 다시 실행하지 않고 해당 부가 정보 생성을 경고로 건너뜁니다.

### 진행 상태

백업/복원 중에는 다음 정보가 표시됩니다.

- 현재 처리 단계
- 단계 기반 진행률
- 작업 경과 시간

진행률은 전체 파일 바이트를 실시간 계산하는 값이 아니라 백업/복원의 주요 단계 기준입니다. 큰 폴더를 복사하는 동안 같은 퍼센트에서 오래 머무를 수 있으나, 현재 단계와 경과 시간이 계속 표시됩니다.

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

## 백업 모드

### Normal backup

기본 백업입니다.

- `%APPDATA%\Cursor`
  - `workspaceStorage`, WebStorage, CachedData, History, 로그 및 Cache 제외
- `%LOCALAPPDATA%\Cursor`
  - Cache, GPUCache, Code Cache, Service Worker, Crashpad 제외
- `%USERPROFILE%\.cursor`
  - `user-data` 제외

`workspaceStorage`는 기본 제외하므로 일반적으로 Full AI backup보다 작고 빠릅니다.

### Full AI backup

Normal backup 범위에 더해 `%APPDATA%\Cursor\User\workspaceStorage`를 포함합니다.

프로젝트별 Workspace 상태와 AI 작업 복구 가능성을 높이는 대신 백업 용량과 시간이 크게 증가할 수 있습니다.

## 백업 처리

- 백업 폴더: `backups\yyyy-MM-dd_HHmmss`
- 작업 중: `backups\yyyy-MM-dd_HHmmss.incomplete`
- 완료된 백업에는 `backup-info.json` 생성
- Cursor 버전 및 확장 목록은 Cursor CLI가 사용 가능한 경우 기록
- `sqlite3.exe` 및 자동 `VACUUM`은 사용하지 않음

백업 결과는 다음 중 하나입니다.

- `SUCCESS`: 필수 백업 및 부가 정보 완료
- `WARNING`: 필수 백업은 완료됐지만 일부 선택 경로나 부가 정보 누락
- `FAILED`: 필수 데이터 복사 또는 백업 확정 실패

## 복원 처리

1. Cursor 종료 확인
2. 현재 상태를 `pre-restore_yyyy-MM-dd_HHmmss`로 안전 백업
3. 현재 Cursor 데이터를 임시 위치로 이동
4. 선택한 백업을 새로 복원
5. Normal backup에 없던 현재 `workspaceStorage` 보존
6. 백업 제외 대상인 `.cursor\user-data` 보존
7. 실패 시 기존 데이터 롤백 시도

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
