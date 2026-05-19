using System.Collections.Generic;

namespace WotrBuildSync.Schema
{
    public enum CharacterType
    {
        Main,       // 메인 캐릭터
        Companion,  // 고유 동료 (Origin = null, LevelPath는 첫 조작 레벨부터)
        Mercenary   // 용병
    }

    /// <summary>
    /// 게임 친화적 GUID 기반 빌드 스키마 (Pure JSON 레이어).
    /// CodexAdapter가 이 타입과 웹사이트 스키마(def.ts의 Build 타입) 사이를 변환한다.
    /// 게임 내부 구조는 docs/game-architecture.md 참조.
    /// </summary>
    public class PureBuild
    {
        public string SchemaVersion { get; set; } = "1.0";

        /// 캐릭터 타입. Companion이면 Origin은 null (Blueprint가 종족/스탯 보유).
        public CharacterType Type { get; set; }

        /// 캐릭터 생성 선택 (종족, 정렬, 스탯). 빌드 경로와 분리.
        /// Companion 타입은 null.
        public CharacterOrigin Origin { get; set; }

        /// 레벨업 경로 — 일반 레벨 1~20 (UnitProgressionData.m_LevelPlans)
        public List<LevelEntry> LevelPath { get; set; } = new List<LevelEntry>();

        /// 신화 경로 — 레벨 1~10 (UnitProgressionData.m_MythicLevelPlans). 없으면 null.
        public List<LevelEntry> MythicPath { get; set; }
    }

    /// <summary>
    /// 캐릭터 생성 시 결정되는 선택들. LevelPath/MythicPath와 분리.
    /// </summary>
    public class CharacterOrigin
    {
        /// BlueprintRace GUID (SelectRace 액션)
        public string RaceGuid { get; set; }

        /// Alignment enum 이름 (예: "LawfulGood", "TrueNeutral") (SelectAlignment 액션)
        public string Alignment { get; set; }

        /// 종족 스탯 보너스 선택 — StatType 이름 (예: "Intelligence") (SelectRaceStat 액션).
        /// 보너스 선택이 없는 종족은 null.
        public string RaceStatBonus { get; set; }

        /// 포인트 배분 최종값 (종족 보정 전, AddStatPoint 결과).
        /// 키: StatType 이름 ("Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma")
        public Dictionary<string, int> BaseAbilityScores { get; set; }
    }

    /// <summary>
    /// 하나의 LevelPlanData에 대응. LevelPath/MythicPath 공통으로 사용.
    /// </summary>
    public class LevelEntry
    {
        public int Level { get; set; }

        /// BlueprintCharacterClass GUID (SelectClass 액션)
        public string ClassGuid { get; set; }

        /// BlueprintArchetype GUID 목록 (AddArchetype 액션).
        /// 한 클래스에 복수 아키타입 가능. 보통 해당 클래스의 첫 레벨에만 설정.
        public List<string> ArchetypeGuids { get; set; } = new List<string>();

        /// SelectFeature 액션 목록
        public List<FeatureSelectionEntry> Selections { get; set; } = new List<FeatureSelectionEntry>();

        /// SpendSkillPoint 액션 집계.
        /// 키: StatType 이름 (예: "SkillAthletics"), 값: 이 레벨에서 추가한 랭크 수.
        public Dictionary<string, int> SkillRanks { get; set; } = new Dictionary<string, int>();

        /// SpendAttributePoint 액션 (캐릭터 레벨 4·8·12·16·20에만 설정).
        /// StatType 이름 (예: "Strength"). 해당 레벨이 아니면 null.
        public string AttributeIncrease { get; set; }

        /// SelectSpell 액션 목록. 자연/음유시인 등 known-spell 방식 시전자만 사용.
        public List<SpellEntry> Spells { get; set; } = new List<SpellEntry>();
    }

    /// <summary>
    /// SelectFeature 액션 하나에 대응.
    /// </summary>
    public class FeatureSelectionEntry
    {
        /// BlueprintFeatureSelection GUID — 선택 슬롯 (어떤 feat 슬롯인지)
        public string SelectionGuid { get; set; }

        /// 동일 SelectionGuid가 한 레벨에 복수 존재할 때의 인덱스 (SelectFeature.SelectionIndex)
        public int SelectionIndex { get; set; }

        /// BlueprintFeature GUID — 실제로 선택된 feature
        public string FeatureGuid { get; set; }

        /// 파라미터가 있는 feature용 값 (예: Weapon Focus → 무기 GUID, Spell Focus → 마법 학파 이름).
        /// Blueprint이면 AssetGuid, enum이면 ToString() 값. 파라미터 없으면 null.
        public string Parameter { get; set; }
    }

    /// <summary>
    /// SelectSpell 액션 하나에 대응.
    /// </summary>
    public class SpellEntry
    {
        /// BlueprintSpellbook GUID
        public string SpellbookGuid { get; set; }

        /// BlueprintSpellList GUID (신화 주문록 등 복수 주문 목록 구분용)
        public string SpellListGuid { get; set; }

        /// 주문 레벨 (0~9)
        public int SpellLevel { get; set; }

        /// BlueprintAbility GUID
        public string SpellGuid { get; set; }

        /// 같은 주문 레벨 내 슬롯 인덱스 (SelectSpell.SlotIndex)
        public int SlotIndex { get; set; }
    }
}
