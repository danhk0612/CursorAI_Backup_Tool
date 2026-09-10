# CursorAI Backup Tool v2 계획

## 목적

기존 배치 스크립트의 단순한 사용성을 유지하면서 Cursor 백업/복원의 신뢰성과 복구 가능성을 높인다.

v2의 우선 목표:

1. 백업이 실제로 성공했는지 명확히 판정한다.
2. 복원 시 현재 데이터와 과거 백업이 섞이지 않도록 한다.
3. Cursor AI 작업 데이터까지 필요에 따라 보존할 수 있도록 백업 범위를 명확히 나눈다.

압축, 암호화, 클라우드 백업, 예약/증분 백업은 현재 v2 범위에서 제외한다.

## v2 백업 정책

### 일반 백업 — 기본값

포함:

- `%APPDATA%\Cursor` 설정 및 핵심 사용자 데이터
- `globalStorage`
- `%LOCALAPPDATA%\Cursor` 중 캐시가 아닌 항목
- `%USERPROFILE%\.cursor` 중 `user-data` 제외 데이터
- Cursor 버전 정보와 확장 목록은 CLI 사용 가능 시 기록

제외:

- `%APPDATA%\Cursor\User\workspaceStorage`
- 일반 캐시/로그/Crashpad 등 재생성 가능한 데이터

### 전체 AI 백업 — 선택 옵션

일반 백업 범위에 더해 `%APPDATA%\Cursor\User\workspaceStorage`를 포함한다.

### 민감정보

`.cursor`에는 MCP 설정, 프로젝트 경로, Agent transcript 및 인증 정보가 포함될 가능성이 있다. v2는 암호화를 구현하지 않지만 민감정보 포함 가능성을 실행 화면과 README에 표시한다.

## 백업 완료 정책

- `SUCCESS`: 필수 백업 범위와 메타데이터 생성 완료
- `WARNING`: 백업은 사용 가능하지만 일부 선택 데이터 또는 부가 정보 생성 실패
- `FAILED`: 필수 데이터 복사 실패 또는 완료 상태를 보장할 수 없음

백업 폴더:

- 작업 중: `yyyy-MM-dd_HHmmss.incomplete`
- 완료 후: `yyyy-MM-dd_HHmmss`

`.incomplete` 폴더는 정상 복원 후보로 취급하지 않는다.

## 복원 정책

1. Cursor 실행 여부 확인
2. 실행 중이면 사용자 확인 후 자동 종료
3. 현재 Cursor 데이터를 `pre-restore_yyyy-MM-dd_HHmmss`로 안전 백업
4. 선택한 백업의 필수 데이터 확인
5. 기존 대상 데이터를 임시 위치로 이동
6. 백업 데이터 복원
7. Normal backup에서 제외된 현재 `workspaceStorage` 보존
8. `.cursor\user-data` 보존
9. 실패 시 기존 데이터 롤백 시도

## SQLite 정책

`sqlite3.exe`는 백업 의존성에서 제거했다. 백업본의 `state.vscdb`에 자동 `VACUUM`을 실행하지 않는다.

## 확장 백업 정책

- 확장 ID 목록 유지
- 가능한 경우 버전 포함 목록 추가
- 재설치 성공/실패 개별 집계
- VSIX 전체 백업은 제외

## Marketplace 정책

`product.json` 직접 수정 기능은 Legacy/고급 기능으로 분리한다. 원복 시 현재 `product.json` 전체를 과거 파일로 덮어쓰지 않고 `extensionsGallery` 범위만 복원하도록 개선한다.

## 작업 상태

### T01 — 현재 동작 및 v2 정책 확정
완료

### T02 — 백업 오류 판정 수정
완료

### T03 — 백업 생성 구조 개선
완료

### T04 — Cursor CLI 탐색 수정
완료

### T05 — 백업 범위 재정리
완료

### T06 — 복원 안전성 개선
완료

### T07 — 백업 메타데이터 추가
완료

### T08 — 확장 백업 개선
완료

### T09 — SQLite 의존성 제거
완료

### T10 — Marketplace 기능 정리
완료

### T11 — 백업 관리 개선
완료

### T12 — C# WinForms GUI
구현 완료, Windows 실사용 검증 진행 중

구현 범위:

- .NET 8 WinForms / win-x64
- single-file self-contained publish
- Normal / Full AI 백업 선택
- 백업 목록, 크기, 상태, 생성 시각
- 백업 / 복원 / 삭제 / 새로고침
- 복원 전 안전 백업 및 실패 롤백

실사용 피드백 반영:

- 백업 시작 시 Cursor 실행 중이면 알림창에서 자동 종료 여부 확인
- 자동 종료 후 Cursor 프로세스가 완전히 종료됐는지 확인한 뒤 작업 시작
- 백업 중 `Cursor.exe`를 직접 CLI 용도로 실행하지 않도록 수정해 Cursor 자동 재실행 방지
- Cursor CLI는 `resources\app\bin\cursor.cmd` 또는 PATH의 `cursor.cmd`만 사용
- CLI를 찾지 못하면 버전/확장 목록만 WARNING으로 건너뜀
- 백업/복원 중 단계 기반 진행 바 표시
- 현재 처리 단계 및 경과 시간 표시
- 큰 데이터 복사 중 진행률이 정체되어도 현재 작업과 경과 시간을 확인 가능

## 변경 원칙

- Task 범위 밖 기능은 임의로 추가하지 않는다.
- 기존 정상 동작을 이유 없이 변경하지 않는다.
- 백업 데이터 삭제가 발생할 수 있는 변경은 보수적으로 처리한다.
- 실제 Windows 검증 결과를 기준으로 T12를 최종 확정한다.
