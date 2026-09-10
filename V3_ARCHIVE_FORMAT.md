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

## Rationale

아카이브 방식의 목적은 최고 압축률이 아니라 파일 개수를 줄이는 것이다. .NET 내장 ZIP과 `CompressionLevel.Fastest`를 사용하므로 별도 7-Zip 실행 파일이나 외부 압축 프로그램이 필요하지 않다.

주요 효과:

- 백업 폴더 삭제 속도 개선
- VM/USB/NAS 등으로 백업 이동·복사 단순화
- 목록 크기 조회 비용 감소
- 수많은 작은 파일을 백업 대상 폴더에 개별 생성하는 비용 감소

복원 시에는 실제 Cursor 데이터 파일을 다시 생성해야 하므로 파일 수에 따른 복원 시간 자체를 완전히 제거할 수는 없다.