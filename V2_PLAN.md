# CursorAI Backup Tool v2 계획

## 목적

기존 배치 스크립트의 단순한 사용성을 유지하면서, Cursor 백업/복원의 신뢰성과 복구 가능성을 높인다.

v2의 우선 목표는 기능 확장이 아니라 다음 세 가지다.

1. 백업이 실제로 성공했는지 명확히 판정한다.
2. 복원 시 현재 데이터와 과거 백업이 섞이지 않도록 한다.
3. Cursor AI 작업 데이터까지 필요에 따라 보존할 수 있도록 백업 범위를 명확히 나눈다.

압축, 암호화, 클라우드 백업, 예약/증분 백업은 현재 v2 범위에서 제외한다.
GUI는 백업/복원 정책과 배치 기반 동작이 안정화된 뒤 별도 단계에서 검토한다.

---

## 현재 v1 동작 기준

### 백업

현재 `cursor-backup.bat`는 다음 데이터를 백업한다.

- `%APPDATA%\Cursor`
  - 단, `WorkspaceStorage`, `User\WebStorage`, `User\CachedData`, `User\History`, `User\logs`, `logs`, `Cache` 제외
- `%LOCALAPPDATA%\Cursor`
  - `Cache`, `GPUCache`, `Code Cache`, `Service Worker`, `Crashpad` 제외
- `%USERPROFILE%\.cursor`
  - `user-data` 제외
- `cursor --version` 결과
- `cursor --list-extensions` 결과

백업 폴더명은 `yyyy-MM-dd_HHmm` 형식이다.

현재 `sqlite3.exe`가 없으면 백업을 시작하지 않으며, 복사된 `state.vscdb`에 `VACUUM`을 실행한다.

### 복원

현재 `cursor-restore.bat`는 선택한 백업을 다음 경로에 `robocopy /E`로 덮어쓴다.

- `Roaming\Cursor` → `%APPDATA%\Cursor`
- `Local\Cursor` → `%LOCALAPPDATA%\Cursor`
- `User\.cursor` → `%USERPROFILE%\.cursor`

기존 대상에만 존재하는 파일은 삭제하지 않으므로, 복원 후 과거 백업과 현재 데이터가 혼합될 수 있다.

### 확장 복원

`extensions.txt`의 확장 ID를 읽어 `cursor --install-extension`으로 순차 설치한다.

### Marketplace 도구

- `cursor_market_vscode.bat`: Cursor `product.json`의 `extensionsGallery`를 VS Code Marketplace 설정으로 직접 변경
- `cursor_market_cursor.bat`: `product.json.backup` 전체 파일을 다시 덮어써 원복

---

## v2 백업 정책

### 일반 백업 — 기본값

빠르고 용량을 과도하게 늘리지 않는 기본 백업이다.

포함:

- `%APPDATA%\Cursor`
  - 설정 및 핵심 사용자 데이터
  - `globalStorage` 포함
- `%LOCALAPPDATA%\Cursor`
  - Cursor 로컬 데이터 중 캐시가 아닌 항목
- `%USERPROFILE%\.cursor`
  - 프로젝트 transcript, rules, MCP 및 기타 사용자 데이터
- Cursor 버전 정보
- 설치 확장 목록

제외:

- `%APPDATA%\Cursor\User\workspaceStorage`
- 일반 캐시/로그/Crashpad 등 재생성 가능한 데이터

`workspaceStorage`는 용량 증가 가능성이 있으므로 기본 백업에서는 제외한다.

### 전체 AI 백업 — 선택 옵션

일반 백업 범위에 더해 다음을 포함한다.

- `%APPDATA%\Cursor\User\workspaceStorage`

목적은 프로젝트별 workspace 상태와 AI 대화/작업 복구 가능성을 최대한 높이는 것이다.

전체 AI 백업 사용 시 UI 또는 콘솔에서 `workspaceStorage` 포함 여부와 예상/실제 용량을 명확히 표시하는 방향으로 구현한다.

### 민감정보

`.cursor`에는 MCP 설정, 프로젝트 경로, Agent transcript 및 인증 정보가 포함될 가능성이 있다.

v2는 암호화를 구현하지 않지만, 백업에 민감정보가 포함될 수 있음을 README와 실행 화면에서 명확히 알린다.

---

## v2 백업 완료 정책

백업 결과는 다음 세 상태 중 하나로 판정한다.

- `SUCCESS`: 필수 백업 범위와 메타데이터 생성 완료
- `WARNING`: 백업은 사용 가능하지만 일부 선택 데이터 또는 부가 정보 생성 실패
- `FAILED`: 필수 데이터 복사 실패 또는 완료 상태를 보장할 수 없음

각 `robocopy` 작업의 종료 코드를 개별 확인한다.

백업 폴더는 초 단위로 생성한다.

- 작업 중: `yyyy-MM-dd_HHmmss.incomplete`
- 정상 완료 후: `yyyy-MM-dd_HHmmss`

중단되거나 실패한 `.incomplete` 폴더는 정상 복원 후보로 취급하지 않는다.

---

## v2 복원 정책

복원은 기존 데이터에 단순 덮어쓰는 방식이 아니라, 선택한 백업 상태로 교체하는 것을 기본으로 한다.

1. Cursor 실행 여부 확인
2. 실행 중이면 안전한 복원을 위해 종료
3. 현재 Cursor 데이터를 `pre-restore_yyyy-MM-dd_HHmmss` 형태로 안전 백업
4. 선택한 백업의 완료 상태 및 필수 데이터 확인
5. 기존 대상 데이터를 임시 위치로 이동
6. 백업 데이터 복사
7. 복사 성공 여부 확인
8. 성공 시 복원 완료
9. 실패 시 기존 데이터로 되돌릴 수 있는 상태 유지

`robocopy /MIR`로 현재 사용자 데이터를 즉시 삭제하는 방식은 사용하지 않는다.

복원 후보는 정상 완료된 백업만 기본 표시한다.

---

## SQLite 정책

`sqlite3.exe`는 백업의 필수 의존성에서 제거한다.

백업본의 `state.vscdb`에 자동 `VACUUM`을 실행하지 않는다.

DB 최적화가 필요하면 백업/복원과 분리된 유지보수 기능으로 추후 검토한다.

---

## 확장 백업 정책

기본적으로 확장 ID 목록을 보존한다.

가능한 경우 버전 정보도 함께 기록한다.

VSIX 파일 전체 백업은 용량과 복잡도 증가 때문에 v2 기본 범위에서 제외한다.

확장 재설치 실패는 전체 Cursor 데이터 복원 실패와 구분해서 표시한다.

---

## Marketplace 기능 정책

`product.json` 직접 수정 기능은 핵심 백업/복원 기능과 분리한다.

v2에서 유지할 경우 `Legacy / 고급 기능`으로 취급한다.

특히 오래된 `product.json.backup` 전체를 새 Cursor 버전에 덮어쓰는 방식은 개선 또는 폐기 대상으로 본다.

---

## 작업 순서

### T01 — 현재 동작 및 v2 정책 확정

- 현재 v1 백업/복원 동작 문서화
- 일반 백업 / 전체 AI 백업 범위 확정
- 복원 교체 정책 확정
- SQLite 및 Marketplace 처리 방향 확정

상태: 이 문서 반영으로 완료

### T02 — 백업 오류 판정 수정

- 세 `robocopy` 결과를 각각 확인
- `SUCCESS / WARNING / FAILED` 판정
- 실패했는데 완료 메시지가 출력되는 문제 수정
- 존재하지 않는 선택 경로와 실제 복사 실패를 구분

### T03 — 백업 생성 구조 개선

- 초 단위 백업 폴더명 적용
- `.incomplete` staging 적용
- 완료 후에만 정상 백업명으로 확정
- 중간 실패 백업이 복원 목록에 나타나지 않도록 처리

### T04 — Cursor CLI 탐색 수정

- 탐색한 `%CURSOR_CMD%`를 버전 조회와 확장 목록 생성에 실제 사용
- 사용자 설치 / 시스템 설치 / PATH 순서 유지

### T05 — 백업 범위 재정리

- 일반 백업 기본값 유지
- `workspaceStorage` 기본 제외
- 전체 AI 백업 선택 옵션 추가
- 캐시/로그 제외 목록 재검토
- 민감정보 포함 가능성 안내

### T06 — 복원 안전성 개선

- 복원 전 현재 상태 자동 안전 백업
- 기존 데이터와 백업 데이터 혼합 방지
- 실패 시 기존 상태 복구 가능하도록 처리
- `.incomplete` 백업 복원 차단

### T07 — 백업 메타데이터 추가

- `backup-info.json`
- 생성 시각
- 백업 형식 버전
- Cursor 버전
- 일반/전체 AI 백업 구분
- `workspaceStorage` 포함 여부
- 백업 결과 상태
- 필요 시 용량/파일 수

### T08 — 확장 백업 개선

- 확장 ID 보존
- 가능한 경우 버전 정보 추가
- 재설치 결과 개별 확인

### T09 — SQLite 의존성 제거

- `sqlite3.exe` 없어도 정상 백업 가능하게 변경
- 자동 `VACUUM` 제거
- 저장소 내 bundled `sqlite3.exe` 유지 여부 재검토

### T10 — Marketplace 기능 정리

- 핵심 기능에서 분리
- 최신 Cursor에서 의미와 위험성 재검토
- 유지 시 Legacy/고급 기능으로 명확히 표시
- 전체 `product.json` 원복 방식 개선 또는 폐기

### T11 — 백업 관리 개선

- 정상/불완전 백업 구분
- 생성일/크기/백업 종류 표시
- 오래된 백업 삭제 기능 검토

### T12 — GUI 검토 및 구현

- T01~T11 정책과 동작을 기반으로 Windows GUI 전환 검토
- 핵심은 백업/복원/백업 목록에 한정
- 불필요한 기능 확장은 하지 않음

---

## 변경 원칙

- 각 Task는 지정된 범위만 변경한다.
- 다음 Task를 선행 구현하지 않는다.
- 기존 정상 동작을 이유 없이 함께 변경하지 않는다.
- 백업 데이터 삭제가 발생할 수 있는 변경은 보수적으로 처리한다.
- 배치 기반 v2 동작 검증이 끝나기 전 GUI로 전환하지 않는다.
