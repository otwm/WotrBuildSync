# Import 구현 현황 및 미해결 문제

> 마지막 업데이트: 2026-05-22

---

## 현재 동작 상태 요약

| 항목 | 상태 | 비고 |
|---|---|---|
| 일반 레벨(1~20) 적용 | ✅ 작동 | 클래스·아키타입·피처·스킬 정상 |
| 신화 레벨 숫자 적용 | ✅ 작동 | `MythicLevel` 수치는 정상 증가 |
| 자동 부여 신화 피처 | ✅ 작동 | 클래스가 자동으로 주는 피처는 적용됨 |
| 신화 피처/특기 **선택** | ⚠️ 부분 작동 | `AddSelection` 호출 성공하나 인게임 적용 불확실 |
| 주문 복원 | ❌ 미작동 | 주문책 UI에는 없으나 실제 능력은 이전 상태 유지 |
| 아이템창 캐릭터 3D 미리보기 | ❌ 없음 | 모딩 전반에 걸친 기존 문제 (우리 코드 이전부터) |
| 이름 변경 방지 | ✅ 수정됨 | `FindCustomNameField`로 복사 전 저장·복원 |
| 파티뷰 유닛 사라짐 | 🔍 미확인 | `DumpUnitPartyState` 로그로 진단 필요 |

---

## 핵심 아키텍처: FullRespec 흐름

```
1. 진공 유닛 생성 (CreateUnitVacuum, DoNotCreateItems)
2. XP 주입 (Experience + MythicExperience)
3. LevelPlanData 구성 후 AddLevelPlan
4. LevelUpController.Start(LevelUp) × CharLevel번 반복
5. 신화: LevelUpController.Start(Mythic) + GetNextLevelPlan 패치로 플랜 주입
6. 안전 체크: 신화 미적용 시 전체 취소
7. CopyUnitDataBack (진공→원본, 이름 보존)
8. 신화 선택 델타: ApplySelectionDelta → TryAddSelection
9. 주문 추가: ApplySpells
```

---

## 미해결 문제 상세

### 1. 신화 선택이 인게임에서 실제로 활성화되는지 불명확

**로그 확인됨:**
```
[Import] AddSelection 성공: MythicAbilitySelection lv=1 → LeadingStrike
[Import] AddSelection 성공: FirstAscensionSelection lv=1 → LichFirstLevelsProgression
...
```

**실제 시그니처:**
```
UnitProgressionData.AddSelection(BlueprintFeatureSelection, FeatureSource, Int32, BlueprintFeature)
```

**문제:** `FeatureSource`를 `Activator.CreateInstance`로 빈 struct 생성해 전달함.  
`FeatureSource`에는 원래 `BlueprintCharacterClass`와 레벨 정보가 담겨야 할 것으로 추정.  
빈 `FeatureSource`로 호출하면 selection 레지스트리에는 등록되지만, 게임이 해당 피처를 캐릭터에 실제로 **적용**(Fact 활성화)하는지 불확실.

**확인 필요:** `AddSelection` 내부에서 feature fact를 자동으로 `unit.AddFact`하는지,  
아니면 fact 추가는 별도 과정인지.

**임시 폴백:** `AddSelection` 실패 시 `prog.Features.AddFeature(featBp)` 사용.  
`Features.AddFeature`는 호출 성공하지만 이것도 실제 활성화와 같은 것인지 미확인.

---

### 2. 주문 복원 실패

**증상:** `ApplySpells`는 호출되고 `spellbook.AddKnown`도 실행되지만,  
주문책 UI에는 주문이 없고 리스펙 이전의 주문들만 실제로 사용 가능.

**원인 추정:**
- `CopyUnitDataBack` 이후 `PostLoad`가 주문책을 초기 상태로 덮어씌움
- 또는 `ApplySpells`의 `spellbook.AddKnown`이 현재 활성화된 주문책 인스턴스에  
  반영되지 않고 사라짐 (PostLoad 이후 주문책 객체가 교체될 가능성)

**조사 필요:** `ApplySpells`를 `PostLoad` **이전**에 호출하거나,  
주문책을 PostLoad 이후 별도로 재구성해야 할 수도 있음.

---

### 3. 아이템창 캐릭터 3D 미리보기 소실

**증상:** 인벤토리 화면 중앙의 3D 캐릭터 모델이 사라짐.

**히스토리:** 사용자 확인으로 이 모딩 세션 이전부터 존재하던 문제.  
우리 코드가 직접 원인은 아닐 가능성이 높음.

**관련 가능성:** `CopyUnitDataBack`에서 `Body` 필드를 JSON에서 제거하고 있음.  
`Body`는 외형/장비 모델 관련 컴포넌트일 가능성이 있어, 제외함으로써  
미리보기가 초기화되지 않을 수 있음. 단 이를 포함하면 다른 문제가 생길 수 있음.

---

### 4. 게임 상태 불완전 초기화

**증상 종합:**
- 주문책 UI ≠ 실제 사용 가능 주문
- 신화 선택 UI에 반영 안 됨 (캐릭터 정보창)
- 리스펙 직후에도 이전 캐릭터의 일부 상태가 남아있음

**근본 원인:** `CopyUnitDataBack`의 `PopulateObject` + `PostLoad` 조합이  
게임의 모든 서브시스템을 완전히 재초기화하지 못함.  
게임 자체의 Respec은 `IRespecInitiateUIHandler.HandleRespecInitiate`를 통해  
내부적으로 "strip and rebuild" 방식으로 동작하는데, 우리 방식은 이것을  
JSON 복사로 흉내 내는 것이라 한계가 있음.

---

## 알려진 게임 API

### 확인된 메서드
```csharp
// 선택 등록 (FeatureSource는 빈 struct로 우선 사용 중)
UnitProgressionData.AddSelection(
    BlueprintFeatureSelection selection,
    FeatureSource source,           // Kingmaker.UnitLogic.FeatureSource (struct)
    int level,
    BlueprintFeature feature)

// 피처 교체 (기존 선택이 있을 때)
UnitProgressionData.ReplaceFeature(BlueprintFeature old, BlueprintFeature new)

// 현재 선택 조회
UnitProgressionData.GetSelections(BlueprintFeatureSelection, int level) → List<BlueprintFeature>

// 피처 직접 추가 (폴백)
UnitProgressionData.Features.AddFeature(BlueprintFeature)
```

### 미확인 / 조사 필요
```
FeatureSource 구조체의 실제 필드 및 올바른 생성 방법
UnitDescriptor.CustomName 의 정확한 필드명 (현재 반사로 후보 탐색 중)
주문책 재구성 API
게임 Respec 내부 로직 (IRespecInitiateUIHandler 구현 클래스)
```

---

## 다음 세션에서 시도할 방향

### A. FeatureSource 올바르게 채우기
```csharp
// 신화 클래스 + 레벨 정보를 FeatureSource에 주입
// FeatureSource의 필드 탐색: Assembly-CSharp.dll 디컴파일 또는 리플렉션
var source = Activator.CreateInstance(featureSourceType);
SetField(source, "m_ClassSource", mythicClassBp);
SetField(source, "m_Archetype", null);
// → AddSelection(selBp, source, level, featBp) 재시도
```

### B. 주문 복원 순서 변경
```csharp
// 현재: CopyUnitDataBack → PostLoad → ApplySpells
// 시도: CopyUnitDataBack → ApplySpells → PostLoad
// 또는: PostLoad 이후 주문책을 완전히 재구성
```

### C. 게임 자체 Respec API 활용
```
IRespecInitiateUIHandler.HandleRespecInitiate(unit, successCallback)
→ successCallback에서 신화 선택 + 주문 적용
장점: 게임이 직접 strip+rebuild → 모든 서브시스템 정상 초기화
단점: UI가 열림, 유저 클릭 필요 (또는 자동화 방법 탐색 필요)
```

### D. 진단: AddSelection 호출 후 fact 활성화 여부 확인
```
AddSelection 직후 prog.GetSelections(selBp, level) 호출해
실제로 등록됐는지 확인하는 로그 추가
```
