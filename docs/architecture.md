# Import/Export 아키텍처 설계

## 목표

- [1차]게임(WotR) ↔ Codex 웹사이트 간 빌드 데이터 교환
- [1차]모드가 웹사이트에 종속되지 않도록 설계
- [1차]어댑터를 독립 모듈로 분리하여 웹사이트/서버 이식 가능성 확보
- [2차]게임과 사이트간 직접적인 어댑터 통신 및 사이트 ui를 통한 빌드 sync
- [사이트]사이트에서 빌드 입력시 피트 검증 필요 검토

---

## 데이터 흐름

```
[게임] ──export──▶ pure JSON (로컬 파일, GUID 기반)
[게임] ◀──import── pure JSON (로컬 파일, GUID 기반)

[pure JSON] ◀──▶ [CodexAdapter] ◀──▶ [웹사이트 스키마]
```

---

## 스키마 전략

| 레이어 | 포맷 | 설명 |
|--|--|--|
| 웹사이트 | 이름 기반 (현재 `Build` 타입 유지) | 변경 없음 |
| Pure JSON | GUID 기반 | 게임 친화적, 모드 단독 사용 가능 |
| 어댑터 | 양쪽 변환 담당 | 독립 모듈로 분리 |

- **웹사이트 스키마**는 현재 `client/src/data/builds/def.ts`의 `Build` 타입 그대로 유지
- **Pure JSON**은 게임 내부 Blueprint GUID 기반으로 구성 (ToyBox 등 기존 모드 포맷 참고)
- 어댑터 위치: **모드 안에** 두되, 로직을 독립 모듈로 분리

---

## 모드 구조

```
WotrBuildSync/
├── Core/
│   ├── GameExporter.cs       # 게임 → pure JSON (로컬 파일)
│   └── GameImporter.cs       # pure JSON → 게임 (리스펙 + 배치 레벨업)
│
├── Adapter/
│   └── CodexAdapter.cs       # pure JSON ↔ 웹사이트 스키마 변환
│                             # 추후 TypeScript 포팅 대상
└── UI/
    └── ModMenu.cs
```

---

## 어댑터 분리 원칙

- `CodexAdapter`는 변환 책임만 가짐 (게임 API, 웹 API 호출 없음)
- 입력/출력이 모두 순수 데이터 (POCO/DTO)
- 나중에 웹사이트 서버에서 필요하면 이 모듈 로직만 TypeScript로 포팅

---

## Import 방식 (게임 → 게임)

- 캐릭터를 레벨 1로 초기화 후 레벨업 선택을 순차 자동 적용(또는 고유 동료인 경우, 해당 동료 고유 시작 부터 적용)
- 게임 엔진(`LevelUpController`)이 각 단계 검증 담당
- 적용 실패 항목은 로그에 기록 → 유저에게 수동 선택 필요 항목 안내
- 게임 내 기본 임포트에 대한 검토 필요.(특정 캐릭의 경우 이미 가이드 빌드가 존재. 그것을 재활용 할 수 있는 지 검토) 

### 검증 리스크 및 대응

| 리스크 | 대응 |
|--|--|
| 종족 불일치 (예: 인간 전용 피트) | 해당 슬롯 스킵 + 알림 |
| 패치로 Blueprint GUID 변경 | 버전별 매핑 테이블 관리 |
| 스탯 배분 순서 의존성 | 빌드 데이터에 스탯 배분 순서 포함 |
| 신화 경로 | 일반 레벨업과 별도 처리 |

---

## 개발 순서

1. 공통 Pure JSON 스키마 확정
2. C# / UMM 학습
3. 모드 export (게임 → pure JSON)
4. CodexAdapter (pure JSON ↔ 웹사이트 스키마)
5. 모드 import (pure JSON → 게임)

## 참조
site build 스키마: ./def.ts