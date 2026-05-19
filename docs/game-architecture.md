# WoTR 게임 아키텍처 (빌드 관련)

`Assembly-CSharp.dll` 디컴파일 기반. 빌드 import/export에 필요한 클래스만 정리.

---

## 클래스 다이어그램

```mermaid
classDiagram
    UnitDescriptor --> UnitProgressionData : Progression
    UnitProgressionData --> ClassData : Classes[]
    UnitProgressionData --> LevelPlanData : m_LevelPlans[]
    UnitProgressionData --> LevelPlanData : m_MythicLevelPlans[]
    ClassData --> BlueprintCharacterClass : CharacterClass
    ClassData --> BlueprintArchetype : Archetypes[]
    ClassData --> BlueprintSpellbook : Spellbook

    LevelPlanData --> ILevelUpAction : Actions[]

    class UnitProgressionData {
        +BlueprintRace Race
        +List~ClassData~ Classes
        +List~LevelPlanData~ m_LevelPlans
        +List~LevelPlanData~ m_MythicLevelPlans
        +int CharacterLevel
        +int MythicLevel
    }

    class ClassData {
        +BlueprintCharacterClass CharacterClass
        +int Level
        +List~BlueprintArchetype~ Archetypes
        +BlueprintSpellbook Spellbook
    }

    class LevelPlanData {
        +int Level
        +ILevelUpAction[] Actions
    }

    class ILevelUpAction {
        <<interface>>
        +Check(state, unit) bool
        +Apply(state, unit)
    }

    ILevelUpAction <|-- SelectRace
    ILevelUpAction <|-- SelectAlignment
    ILevelUpAction <|-- SelectRaceStat
    ILevelUpAction <|-- AddStatPoint
    ILevelUpAction <|-- SelectClass
    ILevelUpAction <|-- AddArchetype
    ILevelUpAction <|-- SelectFeature
    ILevelUpAction <|-- SpendSkillPoint
    ILevelUpAction <|-- SpendAttributePoint
    ILevelUpAction <|-- SelectSpell

    class SelectRace {
        +BlueprintRace Race
    }
    class SelectAlignment {
        +Alignment Alignment
    }
    class SelectRaceStat {
        +StatType Stat
    }
    class AddStatPoint {
        +StatType Attribute
        +int Value
    }
    class SelectClass {
        +BlueprintCharacterClass CharacterClass
    }
    class AddArchetype {
        +BlueprintCharacterClass CharacterClass
        +BlueprintArchetype Archetype
    }
    class SelectFeature {
        +IFeatureSelection Selection
        +int SelectionIndex
        +BlueprintFeature Feature
        +object Param
    }
    class SpendSkillPoint {
        +StatType Skill
    }
    class SpendAttributePoint {
        +StatType Attribute
    }
    class SelectSpell {
        +BlueprintSpellbook Spellbook
        +BlueprintSpellList SpellList
        +int SpellLevel
        +BlueprintAbility Spell
        +int SlotIndex
    }
```

---

## 레벨업 흐름

`LevelPlanData.Actions[]`에 저장된 액션이 `LevelUpController`에 의해 순서대로 실행된다.

### 레벨 1 (캐릭터 생성)
```
SelectRace → SelectAlignment → AddStatPoint(×6) → SelectRaceStat? → SelectClass → AddArchetype? → SelectFeature(×N) → SpendSkillPoint(×N)
```

### 레벨 2~20
```
SelectClass → AddArchetype? → SelectFeature(×N) → SpendSkillPoint(×N) → SpendAttributePoint?
```
- `SpendAttributePoint`는 캐릭터 레벨 4, 8, 12, 16, 20에만 발생

### 신화 레벨 (m_MythicLevelPlans)
```
SelectClass(신화클래스) → SelectFeature(×N)
```
- 첫 번째 신화 레벨에서만 신화 클래스 선택

---

## m_LevelPlans (주의: 비어 있을 수 있음)

`UnitProgressionData.m_LevelPlans`는 레벨업 기록을 저장하지만, 캐릭터 생성 방식에 따라 비어 있다.

- **비어 있는 케이스**: 레벨 플랜 시스템 도입 전 생성된 캐릭터, 또는 프리셋/모드로 생성된 캐릭터
- **대안**: `prog.ClassesOrder` + `prog.Selections` + `prog.Race` + `unit.Descriptor.Stats` 조합으로 현재 빌드 상태를 재구성

현재 GameExporter는 이 대안 방식을 사용한다.

---

## AttributeIncrease (레벨 4/8/12/16/20 능력치 증가)

실제 게임 데이터 탐색 결과:

| 위치 | 존재 여부 | 비고 |
|------|-----------|------|
| `prog.Selections` | **없음** | diagnostics.log 확인, lv=4/8/12 항목 없음 |
| `unit.Descriptor.Facts` (StatType param) | **없음** | Stat-param Features 섹션 비어 있음 |
| `FeatProgressionVM.m_FeatureEntries` | **있음** | lv=4,8,12,16,20 항목 확인 |

`FeatProgressionVM.m_FeatureEntries`의 `ProgressionVM+FeatureEntry`를 통해 접근:
- `entry.Level` → 4, 8, 12, 16, 20
- `entry.Feature` → `BlueprintFeatureBase` (블루프린트 직접 참조, `name` 소문자 필드로 이름 획득)
- 해당 블루프린트가 Selection 타입이면 `prog.Selections[bp][level]`에서 선택된 스탯 추출 가능

---

## UI MVVM 레이어 (Character Info)

네임스페이스: `Kingmaker.UI.MVVM._VM.ServiceWindows.CharacterInfo`

### 계층 구조

```
UnitProgressionVM
├── ClassProgressionVM[]         (각 클래스별)
│   └── ProgressionVM[]          (각 BlueprintProgression별, AddAdditionalProgressions 포함)
│       ├── m_Determinators      : List<FeatureEntry>
│       ├── m_ProgressionLines   : Dictionary<int, Dictionary<int, FeatureEntry>>
│       ├── MainChupaChupsList   : List<FeatureProgressionChupaChupsVM>
│       ├── MainChupaChupsLines  : List<List<FeatureProgressionChupaChupsVM>>
│       ├── AdditionalChupaChupsList : List<FeatureProgressionChupaChupsVM>
│       ├── ProgressionSourceFeatures : Dictionary<FeatureEntry, UIFeature>
│       ├── BlueprintProgression
│       ├── ProgressionData      : Kingmaker.UnitLogic.ProgressionData
│       ├── Unit                 : UnitDescriptor
│       └── LevelProgressionVM
└── FeatProgressionVM            (피트 전용, BaseProgressionVM<FeatureProgressionChupaChupsVM> 상속)
    └── m_FeatureEntries         : List<FeatureEntry>
```

### ProgressionVM+FeatureEntry 구조

인게임 실측 결과 (`Assembly-CSharp.dll` 리플렉션):

| 필드 | 타입 | 설명 |
|------|------|------|
| `Feature` | `BlueprintFeatureBase` | 블루프린트 직접 참조 (Unity `name` 소문자 필드로 이름 접근) |
| `Level` | `int` | 이 피처를 얻는 레벨 |
| `Index` | `int` | 같은 레벨 내 순서 |
| `IsConnected` | `bool` | UI 연결선 표시용 |
| `Rank` | `int` | |
| `DifType` | `ClassArchetypeDifType` | `Normal` 등 |

### FeatureProgressionChupaChupsVM 구조

| 필드 | 타입 | 설명 |
|------|------|------|
| `Level` | `int` | |
| `Name` | `string` | 표시 이름 (한글 가능) |
| `IsSelection` | `bool` | 선택 가능 피처 여부 |
| `Feature` | `UIFeature` | 피처 정보 (추가 탐색 필요) |

### RefreshData 호출 시점

`UnitProgressionVM.RefreshData()`는 캐릭터 정보 창 내에서 탭 또는 캐릭터를 전환할 때 호출된다 (`UnitProgressionVM.<.ctor>b__6_1` 람다 → ReactiveProperty 변경 → RefreshData).

---

## 스탯 포인트 배분 (StatsDistribution)

캐릭터 생성 시 25포인트 지급. 모든 스탯 기본값 10.

| 스탯값 | 누적 비용 |
|--------|-----------|
| 7      | -4 (환불) |
| 8      | -2        |
| 9      | -1        |
| 10     | 0         |
| 11     | 1         |
| 12     | 2         |
| 13     | 3         |
| 14     | 5         |
| 15     | 7         |
| 16     | 10        |
| 17     | 13        |
| 18     | 17        |

범위: 최소 7 ~ 최대 18 (포인트 충분 시)

---

## 스킬 관련

- 스킬은 Blueprint GUID가 없고 `StatType` enum으로 식별
- 주요 StatType 예시: `SkillAthletics`, `SkillMobility`, `SkillThievery`, `SkillStealth`, `SkillKnowledgeArcana`, `SkillKnowledgeWorld`, `SkillLoreNature`, `SkillLoreReligion`, `SkillPerception`, `SkillPersuasion`, `SkillUseMagicDevice`
- 레벨당 스킬 랭크 상한: 해당 캐릭터 레벨 (같은 스킬에 레벨당 최대 1랭크)

---

## Pure JSON ↔ 게임 매핑 요약

| Pure JSON 필드 | 게임 액션/클래스 |
|---|---|
| `RaceGuid` | `SelectRace.Race` GUID |
| `Alignment` | `SelectAlignment.Alignment` enum 이름 |
| `RaceStatBonus` | `SelectRaceStat.Stat` StatType 이름 |
| `BaseAbilityScores` | `AddStatPoint` 결과 (`StatsDistribution.StatValues`) |
| `LevelEntry.ClassGuid` | `SelectClass.CharacterClass` GUID |
| `LevelEntry.ArchetypeGuids` | `AddArchetype.Archetype` GUID (복수 가능) |
| `LevelEntry.Selections` | `SelectFeature` 액션들 |
| `LevelEntry.SkillRanks` | `SpendSkillPoint` 액션들 (StatType → 횟수) |
| `LevelEntry.AttributeIncrease` | `SpendAttributePoint.Attribute` StatType 이름 |
| `LevelEntry.Spells` | `SelectSpell` 액션들 |
