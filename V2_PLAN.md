# CursorAI Backup Tool v2 계획

## 목적

기존 배치 스크립트의 단순한 사용성을 유지하면서 Cursor 백업/복원의 신뢰성과 복구 가능성을 높인다.

v2의 우선 목표:

1. 백업이 실제로 성공했는지 명확히 판정한다.
2. 복원 시 현재 데이터와 과거 백업이 섞이지 않도록 한다.
3. Cursor AI 작업 데이터까지 필요에 따라 보존할 수 있도록 백업 범위를 명확히 나눈다.

압축, 암호화, 클라우드 백업, 예약/증분 백업은 초기 v2 범위에서 제외했으며, 파일 수가 많은 폴더형 백업의 실제 Windows 성능 피드백에 따라 v3 아카이브 저장 형식을 후속으로 추가했다.

## v2 백업 정책

### 일반 백업 — 기본값

포함:

- `%APPDATA%\Cursor` 설정 및 핵심 사용자 데이터
- `globalStorage`
- `%LOCALAPPDATA%\Cursor` 중 캐시가 아닌 항목
- `%USERPROFILE%\.cursor` 중 `user-data` 제외 데이터
- `.cursor\extensions` 실파일
- Cursor 버전 정보와 확장 목록은 조회 가능 시 기록

제외:

- `%APPDATA%\Cursor\WorkspaceStorage`
- `%APPDATA%\Cursor\User\workspaceStorage`
- 일반 캐시/로그/Crashpad 등 재생성 가능한 데이터

### 전체 AI 백업 — 선택 옵션

일반 백업 범위에 더해 다음 두 위치를 모두 포함한다.

- `%APPDATA%\Cursor\WorkspaceStorage`
- `%APPDATA%\Cursor\User\workspaceStorage`

### 민감정보

`.cursor`에는 MCP 설정, 프로젝트 경로, Agent transcript 및 인증 정보가 포함될 가능성이 있다. 현재는 암호화를 구현하지 않지만 민감정보 포함 가능성을 실행 화면과 README에 표시한다.

## 백업 완료 정책

- `SUCCESS`: 필수 백업 범위 완료
- `WARNING`: 필수 백업은 완료됐지만 주요 선택 범위 누락 등 복구 범위 감소
- `FAILED`: 필수 데이터 백업 실패 또는 완료 상태를 보장할 수 없음

부가적인 Cursor 버전/확장 목록 조회 실패는 실제 백업 데이터가 정상이라면 INFO 성격으로 취급한다.

백업 폴더:

- 작업 중: `yyyy-MM-dd_HHmmss.incomplete`
- 완료 후: `yyyy-MM-dd_HHmmss`

`.incomplete` 폴더는 정상 복원 후보로 취급하지 않는다.

## 복원 정책

1. Cursor 실행 여부 확인
2. 실행 중이면 사용자 확인 후 자동 종료
3. 기본 복원은 현재 Cursor 데이터를 같은 볼륨의 `*.cursor-backup-old-<timestamp>` 경로로 이동해 롤백용으로 유지
4. 선택한 백업의 필수 데이터 확인
5. 백업 데이터 복원
6. Normal backup에서 제외된 현재 WorkspaceStorage 두 위치 보존
7. `.cursor\user-data` 보존
8. 대용량 제외 데이터는 같은 볼륨에서 디렉터리 이동으로 보존
9. 실패 시 새 복원 데이터를 정리하고 move-aside한 기존 데이터를 원위치로 롤백 시도
10. 성공 후 롤백용 임시 데이터 정리
11. 장기 보관용 `pre-restore_...` 아카이브가 필요한 경우에만 GUI의 `복원 전 영구 안전 백업 생성 (느림)` 옵션 사용

기본 복원은 영구 pre-restore ZIP 생성 단계를 건너뛰어, 실제 VM에서 수 분 이상 걸리던 중복 안전 백업 비용을 제거한다.

## SQLite 정책

`sqlite3.exe`는 백업 의존성에서 제거했다. 백업본의 `state.vscdb`에 자동 `VACUUM`을 실행하지 않는다.

## 확장 백업 정책

- `.cursor\extensions` 실파일을 Normal/Full AI 모두 백업
- 확장 ID 목록 유지
- 가능한 경우 버전 포함 목록 추가
- 재설치 성공/실패 개별 집계
- Marketplace 제거, 연결 불가, 특정 버전 미배포, 수동 VSIX/오프라인 설치 상황에서도 실파일 복구가 가능하도록 한다.

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
구현 완료, Windows 실사용 검증 및 성능 개선 반영 중

구현 범위:

- .NET 8 WinForms / win-x64
- single-file self-contained publish
- Normal / Full AI 백업 선택
- 백업 목록, 크기, 상태, 생성 시각
- 백업 / 복원 / 삭제 / 새로고침
- 기본 move-aside 롤백 및 선택형 영구 pre-restore 백업
- 단일 GUI 인스턴스
- 작업 중 창 종료 차단

실사용 피드백 반영:

- 백업 시작 시 Cursor 실행 중이면 알림창에서 자동 종료 여부 확인
- 자동 종료 후 Cursor 프로세스가 완전히 종료됐는지 확인한 뒤 작업 시작
- 백업 중 `Cursor.exe`를 직접 CLI 용도로 실행하지 않도록 수정해 Cursor 자동 재실행 방지
- User Setup / System Setup / Program Files (x86) 설치 경로 탐색
- Cursor 버전은 `Cursor.exe` 파일 버전 정보에서 읽음
- 확장 목록은 `resources\app\bin\cursor.cmd` 또는 PATH의 `cursor.cmd` 사용
- 부가 메타데이터 실패는 실제 백업 데이터가 정상이라면 INFO 처리
- 백업/복원 중 Marquee 진행 바, 현재 처리 단계, 경과 시간 표시
- 백업 자체 완료 직후 진행 UI를 종료하고 목록 새로고침을 분리
- 영구 pre-restore 아카이브는 기본 비활성화하고 선택 옵션으로 유지

### T13 — v3 아카이브 저장 형식
구현 완료, Windows 실사용 검증 진행 중

- 새 GUI 백업은 `roaming.zip`, `local.zip`, `cursor-user.zip` 영역별 아카이브 사용
- `CompressionLevel.NoCompression`으로 파일 개수 감소를 목표로 하고 압축률은 목표로 하지 않음
- `backupVersion: 3`, `storageFormat: archive-v3`
- `totalBytes`를 메타데이터에 저장해 목록 크기 재귀 계산 최소화
- `.cursor\extensions` 실파일 유지
- Normal/Full AI WorkspaceStorage 정책 유지
- 기존 v2/Legacy 폴더형 백업 복원 호환 유지

## 실제 Windows 검증 상태

2026-09-10 현재 확인:

- Cursor 실행 중 백업 시작 → 종료 확인 및 자동 종료 동작 확인
- 백업 중 Cursor 자동 재실행 문제 수정 확인
- 백업 완료 후 진행 바/경과 시간 계속 유지 문제 수정 확인
- Normal 백업에서 두 WorkspaceStorage 제외 후 백업 용량 감소 확인
- 원본 PC **User Setup** → 대상 VM **System Setup** 교차 설치 유형으로 백업/복원 완료
- 교차 설치 복원 후 Cursor 실행 및 기본 동작에서 큰 문제 없음
- Normal 복원에서 기존 `%APPDATA%\Cursor\User\workspaceStorage` 보존 확인
- `.cursor\extensions`는 VM의 임의 테스트 파일을 남기지 않고 백업본으로 교체됨을 확인
- 복원 전 pre-restore 아카이브가 VM에서 수 분 이상 걸리는 것을 확인하고 선택 옵션으로 최적화

기본 복원 최적화는 코드/문서 반영까지 진행하고 별도 수동 검증 단계는 생략한다.

## 변경 원칙

- Task 범위 밖 기능은 임의로 추가하지 않는다.
- 기존 정상 동작을 이유 없이 변경하지 않는다.
- 백업 데이터 삭제가 발생할 수 있는 변경은 보수적으로 처리한다.
- 실제 Windows 검증 결과를 기준으로 T12/T13을 최종 확정한다.
