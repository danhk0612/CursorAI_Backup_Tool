# CursorAI Backup Tool

Windows에서 Cursor 에디터의 설정, 확장 프로그램, 프로젝트 관련 사용자 데이터를 간편하게 백업하고 복원하는 도구입니다.

## 주요 기능

- GUI에서 클릭으로 백업 / 복원 / 삭제
- 일반 백업과 전체 AI 백업 지원
- Cursor 사용자 설치 / 시스템 설치 자동 대응
- 백업 전에 Cursor가 실행 중이면 자동 종료 여부 확인
- `.cursor\extensions` 실제 확장 파일까지 백업
- 기존 백업 목록에서 생성 시각, 종류, 상태, 크기 확인
- 복원 실패 시 기존 데이터 롤백 시도
- 기존 v2/Legacy 폴더형 백업도 복원 가능

## 다운로드

최신 버전은 GitHub의 **Releases** 페이지에서 받을 수 있습니다.

Windows 64비트용 배포 파일:

- `CursorAI.BackupTool.exe` — 단일 실행 파일
- `CursorAI.BackupTool-win-x64.zip` — 압축 패키지

별도의 .NET 설치는 필요하지 않습니다.

## 사용 방법

1. `CursorAI.BackupTool.exe`를 실행합니다.
2. 백업할 경우 `일반 백업` 또는 `전체 AI 백업`을 선택합니다.
3. `백업` 버튼을 누릅니다.
4. 복원할 경우 목록에서 원하는 백업을 선택한 뒤 `복원` 버튼을 누릅니다.
5. 필요 없는 백업은 선택 후 `삭제`할 수 있습니다.

백업과 복원 중에는 현재 작업 단계와 경과 시간이 표시됩니다.

## 백업 모드

### 일반 백업

일상적으로 사용하기에 권장되는 기본 모드입니다.

Cursor 설정, 확장 프로그램, 프로젝트 관련 데이터 등을 백업하지만 용량이 큰 WorkspaceStorage 두 위치는 제외합니다.

제외되는 주요 항목:

- `%APPDATA%\Cursor\WorkspaceStorage`
- `%APPDATA%\Cursor\User\workspaceStorage`
- `%USERPROFILE%\.cursor\user-data`
- 각종 캐시, 로그 등 다시 생성 가능한 데이터

`.cursor\extensions`의 실제 확장 프로그램 파일은 포함됩니다.

### 전체 AI 백업

일반 백업 범위에 더해 다음 WorkspaceStorage 데이터까지 포함합니다.

- `%APPDATA%\Cursor\WorkspaceStorage`
- `%APPDATA%\Cursor\User\workspaceStorage`

프로젝트별 작업 상태와 AI 관련 작업 복구 범위를 넓히고 싶을 때 사용합니다.

WorkspaceStorage 크기에 따라 백업 용량과 시간이 크게 늘어날 수 있습니다.

## 복원

기본 복원은 현재 Cursor 데이터를 바로 삭제하지 않고 임시 위치로 이동한 뒤 백업본을 복원합니다.

복원 중 문제가 발생하면 이동해 둔 기존 데이터를 이용해 롤백을 시도합니다.

일반 백업으로 복원하는 경우 백업에 포함되지 않았던 현재 WorkspaceStorage와 `.cursor\user-data`는 가능한 경우 그대로 보존합니다.

### 복원 전 영구 안전 백업

GUI의 **`복원 전 영구 안전 백업 생성 (느림)`** 옵션을 켜면 복원 전에 현재 Cursor 상태를 별도 백업으로 하나 더 보관합니다.

이 옵션은 데이터가 많은 경우 시간이 오래 걸릴 수 있으므로 기본값은 꺼져 있습니다.

장기 보관용 복구 지점이 필요한 경우에만 사용하는 것을 권장합니다.

## 백업 저장 위치

백업은 프로그램 실행 파일이 있는 위치의 `backups` 폴더에 저장됩니다.

예:

```text
CursorAI.BackupTool.exe
backups\
  2026-09-10_153000\
  2026-09-10_181500\
```

프로그램을 이동할 경우 기존 `backups` 폴더도 함께 이동하면 기존 백업을 계속 사용할 수 있습니다.

## 백업 상태

백업 목록에는 상태가 표시됩니다.

- `SUCCESS` — 정상 완료
- `WARNING` — 백업은 완료됐지만 일부 선택 데이터가 없어 복구 범위가 줄어든 상태
- `INCOMPLETE` — 백업 도중 중단되었거나 실패한 미완성 백업
- `LEGACY` — 이전 버전에서 만든 폴더형 백업

`INCOMPLETE` 백업은 복원할 수 없습니다.

## Cursor 설치 유형

다음 설치 방식을 모두 지원합니다.

- 사용자 설치(User Setup)
- 시스템 설치(System Setup)

한 PC에서 만든 백업을 다른 설치 방식의 PC나 VM에 복원하는 경우도 지원하도록 구성되어 있습니다.

## 기존 배치 파일

저장소에는 이전 버전의 배치 기반 도구도 남아 있습니다.

새로 사용하는 경우에는 GUI 버전인 `CursorAI.BackupTool.exe` 사용을 권장합니다.

## 주의 사항

Cursor 사용자 데이터에는 프로젝트 경로, AI 작업 기록, MCP 설정, API key/token 등 민감한 정보가 포함될 수 있습니다.

백업 파일을 공개된 위치나 신뢰할 수 없는 저장소에 업로드하지 말고 안전하게 보관하세요.

복원 작업은 현재 Cursor 사용자 데이터를 변경하므로 중요한 작업 환경에서는 필요한 경우 먼저 별도의 백업을 만들어 두는 것을 권장합니다.

## 지원 환경

- Windows 10 이상
- 64비트 Windows
- Cursor 에디터

## License

MIT License
