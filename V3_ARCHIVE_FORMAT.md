# v3 Archive Backup Format

새 GUI 백업은 파일 수가 많은 Cursor 데이터를 백업 폴더에 그대로 복제하지 않고 영역별 ZIP 아카이브로 저장한다.

## Layout

```text
backups/
  yyyy-MM-dd_HHmmss/
    backup-info.json
    roaming.zip
    local.zip                 # 해당 데이터가 있을 때만
    cursor-user.zip           # .cursor가 있을 때만
    cursor_version.txt        # 조회 가능할 때만
    extensions.txt            # CLI 조회 가능할 때만
    extensions_with_versions.txt # CLI 지원 시
```

백업 생성 중에는 폴더명 끝에 `.incomplete`가 붙으며 필수 아카이브와 메타데이터 생성이 끝난 뒤 최종 이름으로 변경한다.

## Scope

### Normal

`roaming.zip`에서 다음 두 WorkspaceStorage를 모두 제외한다.

- `%APPDATA%\Cursor\WorkspaceStorage`
- `%APPDATA%\Cursor\User\workspaceStorage`

`.cursor\extensions`는 Marketplace 제거, 오프라인/VSIX 설치, 특정 버전 재설치 불가 상황에 대비해 `cursor-user.zip` 안에 실파일로 보존한다.

`.cursor\user-data`는 기존 정책대로 제외한다.

### Full AI

Normal과 동일하되 두 WorkspaceStorage를 `roaming.zip`에 포함한다.

## Restore compatibility

- v3 archive: `roaming.zip`, `local.zip`, `cursor-user.zip`을 직접 해제한다.
- v2/Legacy folder: 기존 `Roaming\Cursor`, `Local\Cursor`, `User\.cursor` 폴더 구조를 계속 지원한다.
- Normal 복원에서는 백업에 포함되지 않은 현재 WorkspaceStorage 두 위치와 `.cursor\user-data`를 같은 볼륨의 디렉터리 이동으로 보존한다.
- 복원 전 현재 상태는 v3 `pre-restore_...` 아카이브 백업으로 남긴다.

## Metadata

`backup-info.json` 주요 필드:

- `backupVersion`: `3`
- `storageFormat`: `archive-v3`
- `createdAt`
- `cursorVersion`
- `type`: `normal`, `full-ai`, `pre-restore`
- `workspaceStorageIncluded`
- `status`
- `totalBytes`: 목록 표시용 백업 파일 크기 캐시

`totalBytes`가 존재하는 v3 백업은 목록을 표시할 때 전체 백업 내용을 다시 재귀 탐색하지 않는다.

## Performance policy

아카이브 방식의 목적은 압축률이 아니라 **파일 개수를 줄이는 것**이다. 초기 구현의 `CompressionLevel.Fastest`도 `.cursor\extensions`처럼 작은 파일이 매우 많은 영역에서는 Deflate CPU 비용 때문에 폴더 복사보다 느려질 수 있어 사용하지 않는다.

현재 v3 생성은 .NET 내장 ZIP의 `CompressionLevel.NoCompression`을 사용한다. 즉 ZIP은 수만 개 파일을 몇 개의 컨테이너 파일로 묶는 용도이며, 원본 크기 절감은 목표로 하지 않는다.

생성 중에는 일정량마다 출력 스트림을 flush해서 탐색기에서도 아카이브 파일 크기 증가를 관찰할 수 있도록 한다. 별도 7-Zip 실행 파일이나 외부 압축 프로그램은 필요하지 않다.

주요 효과:

- 작은 파일 압축에 쓰이던 CPU 시간 제거
- 백업 폴더 삭제 속도 개선
- VM/USB/NAS 등으로 백업 이동·복사 단순화
- 목록 크기 조회 비용 감소
- 수많은 작은 파일을 백업 대상 폴더에 개별 생성하는 비용 감소

복원 시에는 실제 Cursor 데이터 파일을 다시 생성해야 하므로 파일 수에 따른 복원 시간 자체를 완전히 제거할 수는 없다.

## Windows validation record

2026-09-10 실제 Windows 환경에서 다음 교차 설치 복원을 확인했다.

- 원본 PC: Cursor **User Setup**
- 대상 VM: Cursor **System Setup**
- 원본 PC에서 백업 생성 후 VM에서 복원 완료
- 복원 후 Cursor 실행 및 기본 동작에서 큰 문제 없음

이 결과로 v3 백업 데이터가 Cursor 설치 위치 자체에 종속되지 않고 User Setup → System Setup 환경 간에도 복원 가능한 것을 1차 확인했다.

Normal 복원 보존 정책도 실제 VM에서 다음과 같이 확인했다.

- VM의 `%APPDATA%\Cursor\User\workspaceStorage`에 테스트 파일을 만든 뒤 Normal 복원 수행
- 복원 후 테스트 파일이 그대로 남아 있어, Normal 백업에서 제외된 기존 User workspaceStorage가 보존됨을 확인
- VM의 `.cursor\extensions`에만 있던 테스트 파일은 복원 후 사라짐을 확인
- 이는 extensions가 보존 대상이 아니라 백업본의 실제 확장 파일로 교체되는 현재 정책과 일치함

현재 VM에는 `%APPDATA%\Cursor\WorkspaceStorage`와 `.cursor\user-data`가 존재하지 않아 해당 두 위치의 보존은 아직 실제 검증하지 못했다.

아직 별도 확인이 필요한 항목:

- Normal 복원 시 기존 `%APPDATA%\Cursor\WorkspaceStorage` 보존 (대상 VM에 실제 폴더가 있을 때)
- Normal 복원 시 기존 `.cursor\user-data` 보존 (대상 VM에 실제 폴더가 있을 때)
- Full AI 백업/복원 시 두 WorkspaceStorage 교체 확인
- 기존 v2/Legacy 폴더 백업 복원 호환성
- 오프라인/VSIX 설치 확장의 실제 복구 확인
- `NoCompression` 적용 후 백업 시간 재측정
- 복원 전 pre-restore 안전 백업의 긴 수행 시간 및 진행 가시성 개선
