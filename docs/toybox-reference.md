# ToyBox 참조 분석

ToyBox 소스 코드(https://github.com/xADDBx/ToyBox-Wrath) 분석 결과.
GameImporter/GameExporter 구현 시 참고용.

---

## Respec 진입 방법

ToyBox는 직접 구현하지 않고 게임 엔진에 위임한다.

```csharp
// ToyBox/Classes/Infrastructure/RespecHelper.cs
EventBus.RaiseEvent(delegate(IRespecInitiateUIHandler h) {
    h.HandleRespecInitiate(unit, onFinishCallback);
});
```

GameImporter도 동일한 패턴으로 respec을 트리거한 뒤, `LevelUpController`가 `CharBuildMode.Respec`으로 진입하면 `LevelPlanData`를 순서대로 적용한다.

---

## 고유 동료 처리 (`IsStoryCompanion`)

`LevelUpState`는 respec 시 `IsStoryCompanion()`으로 동반자를 감지한다.

| 조건 | CanSelectRace | CanSelectAlignment | CanSelectPortrait |
|---|---|---|---|
| 메인/용병 (Respec) | true | true | true |
| 고유 동료 (Respec) | **false** | **false** | **false** |

→ `PureBuild.Type = Companion`이면 `Origin`을 적용하지 않는 설계 근거.

---

## AddClassLevels vs m_LevelPlans 분리

고유 동료의 사전 레벨(`AddClassLevels` 컴포넌트)은 `LevelUpPlanProviders`에 등록되며 `m_LevelPlans`에는 기록되지 않는다.

```csharp
// AddClassLevels.OnTurnOn()
base.Owner.Progression.LevelUpPlanProviders.Add((this, base.Fact));

// AddClassLevels.OnTurnOff()
base.Owner.Progression.LevelUpPlanProviders.RemoveAll(...);
```

- **Export 시**: `m_LevelPlans`만 읽으면 플레이어 조작 레벨만 자동 추출됨 (사전 레벨 오염 없음)
- **Import 시**: 사전 레벨은 게임 엔진이 Blueprint 기반으로 복원하므로 별도 처리 불필요

---

## 빌드 Template/Preset 시스템

**ToyBox에 없음.** 우리 스키마(PureBuild)는 새로운 영역.
ToyBox는 인게임 실시간 편집 방식이며 빌드를 파일로 저장/불러오는 기능은 없다.
