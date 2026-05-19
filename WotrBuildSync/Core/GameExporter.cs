using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Newtonsoft.Json;
using WotrBuildSync.Schema;

namespace WotrBuildSync.Core
{
    public static class GameExporter
    {

        public static void LogDiagnostics(UnitEntityData unit, System.Action<string> log)
        {
            var prog = unit.Descriptor.Progression;
            log($"[진단] unit={unit.CharacterName} CharacterLevel={prog.CharacterLevel} MythicLevel={prog.MythicLevel}");

            log("[진단] --- Spellbooks ---");
            foreach (var sb in unit.Descriptor.Spellbooks)
            {
                int total = 0;
                for (int sl = 0; sl <= 9; sl++) total += sb.GetKnownSpells(sl).Count;
                log($"  {sb.Blueprint?.name} spontaneous={sb.Blueprint?.Spontaneous} knownTotal={total}");
            }

            // Selections 전체 덤프 — AttributeIncrease가 Selection으로 저장돼 있는지 확인
            log("[진단] --- All Selections ---");
            foreach (var kvp in prog.Selections)
            {
                var sel = kvp.Value;
                var selName = kvp.Key?.name ?? "(null)";
                foreach (var lvlKvp in sel.SelectionsByLevel)
                {
                    int lvl = lvlKvp.Key;
                    foreach (var f in lvlKvp.Value)
                        log($"  sel={selName} lv={lvl} feature={f?.name ?? "(null)"} mythic={sel.IsMythic}");
                }
            }

            // UnitProgressionData 전체 필드 덤프 — AttributeIncrease 저장 위치 탐색
            log("[진단] --- UnitProgressionData fields ---");
            var progType = unit.Descriptor.Progression.GetType();
            foreach (var f in progType.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance))
            {
                object val = null;
                try { val = f.GetValue(unit.Descriptor.Progression); } catch { val = "(err)"; }
                log($"  {f.FieldType.Name} {f.Name} = {val}");
            }
        }

        public static string Export(UnitEntityData unit)
        {
            return JsonConvert.SerializeObject(BuildFrom(unit), Formatting.Indented);
        }

        public static string ExportAnnotated(UnitEntityData unit)
        {
            var build = BuildFrom(unit);
            var names = CollectBlueprintNames(unit);
            string N(string guid) => guid == null ? null
                : names.TryGetValue(guid, out var n) ? $"{n}  [{guid}]" : guid;

            object AnnotateEntry(Schema.LevelEntry e) => new
            {
                e.Level,
                Class = N(e.ClassGuid),
                Archetypes = e.ArchetypeGuids.Select(N).ToList(),
                Selections = e.Selections.Select(s => new
                {
                    Selection = N(s.SelectionGuid),
                    Feature   = N(s.FeatureGuid),
                    s.Parameter,
                    Index = s.SelectionIndex,
                }).ToList(),
                e.SkillRanks,
                e.AttributeIncrease,
                Spells = e.Spells.Select(sp => new
                {
                    Spellbook = N(sp.SpellbookGuid),
                    sp.SpellLevel,
                    Spell  = N(sp.SpellGuid),
                    Index  = sp.SlotIndex,
                }).ToList(),
            };

            var annotated = new
            {
                CharacterName = unit.CharacterName,
                build.SchemaVersion,
                build.Type,
                Origin = build.Origin == null ? null : (object)new
                {
                    Race          = N(build.Origin.RaceGuid),
                    build.Origin.Alignment,
                    build.Origin.RaceStatBonus,
                    build.Origin.BaseAbilityScores,
                },
                LevelPath  = build.LevelPath.Select(e => AnnotateEntry(e)).ToList(),
                MythicPath = build.MythicPath?.Select(e => AnnotateEntry(e)).ToList(),
            };

            return JsonConvert.SerializeObject(annotated, Formatting.Indented);
        }

        static Dictionary<string, string> CollectBlueprintNames(UnitEntityData unit)
        {
            var names = new Dictionary<string, string>();
            void Add(SimpleBlueprint bp)
            {
                if (bp == null) return;
                names[bp.AssetGuid.ToString()] = GetDisplayName(bp);
            }

            var prog = unit.Descriptor.Progression;
            Add(prog.Race);

            foreach (var cls in prog.ClassesOrder) Add(cls);
            foreach (var cd in prog.Classes)
            {
                Add(cd.CharacterClass);
                foreach (var arch in cd.Archetypes) Add(arch);
            }

            foreach (var kvp in prog.Selections)
            {
                Add(kvp.Key);
                foreach (var lvlFeatures in kvp.Value.SelectionsByLevel.Values)
                    foreach (var f in lvlFeatures) Add(f);
            }

            foreach (var sb in unit.Descriptor.Spellbooks)
            {
                Add(sb.Blueprint);
                for (int sl = 0; sl <= 9; sl++)
                    foreach (var spell in sb.GetKnownSpells(sl))
                        if (spell?.Blueprint != null) Add(spell.Blueprint);
            }

            return names;
        }

        static string GetDisplayName(SimpleBlueprint bp)
        {
            // m_DisplayName 필드(LocalizedString)가 있으면 현재 로케일 문자열 반환, 없으면 내부 name
            var field = bp.GetType().GetField("m_DisplayName",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                var loc = field.GetValue(bp);
                if (loc != null)
                {
                    var str = loc.ToString();
                    if (!string.IsNullOrWhiteSpace(str)) return str;
                }
            }
            return bp.name;
        }

        public static PureBuild BuildFrom(UnitEntityData unit)
        {
            var prog = unit.Descriptor.Progression;
            var type = DetectType(unit);

            var regularClasses = prog.ClassesOrder.Where(c => c != null && !c.IsMythic).ToList();
            var mythicClasses  = prog.ClassesOrder.Where(c => c != null &&  c.IsMythic).ToList();

            return new PureBuild
            {
                Type      = type,
                Origin    = type != CharacterType.Companion ? ExtractOrigin(unit) : null,
                LevelPath = ReconstructPath(unit, regularClasses, isMythic: false),
                MythicPath = mythicClasses.Count > 0
                    ? ReconstructPath(unit, mythicClasses, isMythic: true)
                    : null,
            };
        }

        static CharacterType DetectType(UnitEntityData unit)
        {
            if (unit.IsCustomCompanion()) return CharacterType.Mercenary;
            if (unit.IsStoryCompanion())  return CharacterType.Companion;
            return CharacterType.Main;
        }

        static CharacterOrigin ExtractOrigin(UnitEntityData unit)
        {
            var prog  = unit.Descriptor.Progression;
            var stats = unit.Descriptor.Stats;

            return new CharacterOrigin
            {
                RaceGuid  = prog.Race?.AssetGuid.ToString(),
                Alignment = unit.Descriptor.Alignment.ValueRaw.ToString(),
                RaceStatBonus     = GetRaceStatBonus(unit),
                BaseAbilityScores = new Dictionary<string, int>
                {
                    { "Strength",     stats.Strength.BaseValue },
                    { "Dexterity",    stats.Dexterity.BaseValue },
                    { "Constitution", stats.Constitution.BaseValue },
                    { "Intelligence", stats.Intelligence.BaseValue },
                    { "Wisdom",       stats.Wisdom.BaseValue },
                    { "Charisma",     stats.Charisma.BaseValue },
                },
            };
        }

        static readonly System.Collections.Generic.HashSet<string> BaseStatNames =
            new System.Collections.Generic.HashSet<string>
            { "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma" };

        // 종족 선택 가능 스탯 보너스 — Human의 보너스 feat(race-sourced)과 구분하기 위해
        // feature.name이 6대 능력치 이름인 경우에만 반환
        static string GetRaceStatBonus(UnitEntityData unit)
        {
            var prog = unit.Descriptor.Progression;
            foreach (var kvp in prog.Selections)
            {
                var data = kvp.Value;
                if (data.IsMythic) continue;
                if (!data.SelectionsByLevel.TryGetValue(1, out var features)) continue;

                if (data.Source.Blueprint is BlueprintRace)
                {
                    foreach (var f in features)
                    {
                        if (f == null) continue;
                        if (BaseStatNames.Contains(f.name)) return f.name;
                    }
                }
            }
            return null;
        }

        static List<Schema.LevelEntry> ReconstructPath(
            UnitEntityData unit,
            List<BlueprintCharacterClass> classSequence,
            bool isMythic)
        {
            var prog    = unit.Descriptor.Progression;
            var entries = new List<Schema.LevelEntry>();
            var seenClasses = new HashSet<BlueprintCharacterClass>();

            // Feature facts: 파라미터 추출용 (레벨별로 매핑)
            var featuresByLevel = BuildFeaturesByLevel(unit);
            // 능력치 증가: 블루프린트 이름 패턴 *-attrib-{stat} 에서 추출
            var attrIncreases = !isMythic ? BuildAttributeIncreaseMap(unit) : null;

            for (int i = 0; i < classSequence.Count; i++)
            {
                int level = i + 1; // 1-based
                var cls   = classSequence[i];

                var entry = new Schema.LevelEntry
                {
                    Level     = level,
                    ClassGuid = cls.AssetGuid.ToString(),
                };

                // 아키타입: 해당 클래스 첫 레벨에만 기록
                if (!seenClasses.Contains(cls))
                {
                    seenClasses.Add(cls);
                    var classData = prog.Classes.FirstOrDefault(c => c.CharacterClass == cls);
                    if (classData?.Archetypes != null)
                        foreach (var arch in classData.Archetypes)
                            entry.ArchetypeGuids.Add(arch.AssetGuid.ToString());
                }

                // Feature Selections (이 레벨 + 레벨1에만 lv=0 background feat 포함)
                var levelsToCollect = level == 1
                    ? new[] { 0, 1 }
                    : new[] { level };

                foreach (var selLevel in levelsToCollect)
                foreach (var kvp in prog.Selections)
                {
                    if (kvp.Value.IsMythic != isMythic) continue;
                    if (!kvp.Value.SelectionsByLevel.TryGetValue(selLevel, out var featuresAtLevel)) continue;

                    var selectionGuid = kvp.Key.AssetGuid.ToString();

                    for (int idx = 0; idx < featuresAtLevel.Count; idx++)
                    {
                        var featureBp = featuresAtLevel[idx];
                        if (featureBp == null) continue;

                        entry.Selections.Add(new FeatureSelectionEntry
                        {
                            SelectionGuid  = selectionGuid,
                            SelectionIndex = idx,
                            FeatureGuid    = featureBp.AssetGuid.ToString(),
                            Parameter      = GetFeatureParam(featureBp, selLevel, featuresByLevel),
                        });
                    }
                }

                entries.Add(entry);
            }

            // 스킬 랭크: 총 랭크를 레벨 1부터 순서대로 배분 (1레벨당 1랭크)
            if (!isMythic)
                DistributeSkillRanks(unit, entries);

            // 스펠: 자연시전자(Spontaneous) 스펠북의 알려진 주문을 마지막 레벨에 추가
            if (!isMythic && entries.Count > 0)
                AppendKnownSpells(unit, entries);

            // AttributeIncrease: *-attrib-{stat} 블루프린트에서 추출
            if (attrIncreases != null)
            {
                foreach (var entry in entries)
                {
                    if (attrIncreases.TryGetValue(entry.Level, out var stat))
                        entry.AttributeIncrease = stat;
                }
            }

            return entries;
        }

        static readonly StatType[] SkillTypes =
        {
            StatType.SkillAthletics,
            StatType.SkillMobility,
            StatType.SkillThievery,
            StatType.SkillStealth,
            StatType.SkillKnowledgeArcana,
            StatType.SkillKnowledgeWorld,
            StatType.SkillLoreNature,
            StatType.SkillLoreReligion,
            StatType.SkillPerception,
            StatType.SkillPersuasion,
            StatType.SkillUseMagicDevice,
        };

        static void DistributeSkillRanks(UnitEntityData unit, List<Schema.LevelEntry> entries)
        {
            var stats = unit.Descriptor.Stats;
            // 스킬별 총 랭크 수집
            var skillTotals = new Dictionary<string, int>();
            foreach (var skillType in SkillTypes)
            {
                var stat = stats.GetStat(skillType);
                if (stat == null) continue;
                int ranks = stat.BaseValue;
                if (ranks > 0)
                    skillTotals[skillType.ToString()] = ranks;
            }

            // 레벨 1부터 순서대로 각 스킬 1랭크씩 배분
            var remaining = new Dictionary<string, int>(skillTotals);
            foreach (var entry in entries)
            {
                foreach (var skillName in SkillTypes.Select(s => s.ToString()))
                {
                    if (!remaining.TryGetValue(skillName, out int left) || left <= 0) continue;
                    if (!entry.SkillRanks.ContainsKey(skillName))
                        entry.SkillRanks[skillName] = 0;
                    entry.SkillRanks[skillName]++;
                    remaining[skillName]--;
                }
            }
        }

        static void AppendKnownSpells(UnitEntityData unit, List<Schema.LevelEntry> entries)
        {
            var lastEntry = entries[entries.Count - 1];
            foreach (var spellbook in unit.Descriptor.Spellbooks)
            {
                if (!spellbook.Blueprint.Spontaneous) continue;
                var spellbookGuid = spellbook.Blueprint.AssetGuid.ToString();

                for (int spellLevel = 0; spellLevel <= 9; spellLevel++)
                {
                    var known = spellbook.GetKnownSpells(spellLevel);
                    int idx = 0;
                    foreach (var spell in known)
                    {
                        if (spell?.Blueprint == null) { idx++; continue; }

                        // 두루마리 복사본, 임시 스펠, 신화 스펠리스트 출처 등 제외 (ToyBox SpellsEditor 방식)
                        if (spell.IsTemporary
                            || spell.CopiedFromScroll
                            || spell.IsFromMythicSpellList
                            || spell.IsMysticTheurgeCombinedSpell
                            || spell.SourceItem != null)
                        { idx++; continue; }

                        lastEntry.Spells.Add(new SpellEntry
                        {
                            SpellbookGuid = spellbookGuid,
                            SpellListGuid = null,
                            SpellLevel    = spellLevel,
                            SpellGuid     = spell.Blueprint.AssetGuid.ToString(),
                            SlotIndex     = idx,
                        });
                        idx++;
                    }
                }
            }
        }

        // 블루프린트 이름 *-attrib-{stat} 패턴으로 캐릭터 레벨별 능력치 증가 추출
        static readonly HashSet<string> AttribStatNames =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            { "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma" };

        static Dictionary<int, string> BuildAttributeIncreaseMap(UnitEntityData unit)
        {
            var map = new Dictionary<int, string>();
            foreach (var fact in unit.Descriptor.Facts.List)
            {
                if (!(fact is Kingmaker.UnitLogic.Feature feature)) continue;
                if (feature.Blueprint == null) continue;
                var bpName = feature.Blueprint.name;
                if (bpName == null) continue;

                var idx = bpName.IndexOf("-attrib-", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                var statPart = bpName.Substring(idx + 8);
                if (!AttribStatNames.Contains(statPart)) continue;

                var statName = char.ToUpper(statPart[0]) + statPart.Substring(1);
                var srcLevel = feature.SourceLevel;
                if (srcLevel > 0 && !map.ContainsKey(srcLevel))
                    map[srcLevel] = statName;
            }
            return map;
        }

        // unit의 Feature facts를 (blueprint GUID, SourceLevel) → param string 으로 인덱싱
        static Dictionary<(string guid, int level), string> BuildFeaturesByLevel(UnitEntityData unit)
        {
            var map = new Dictionary<(string, int), string>();
            foreach (var fact in unit.Descriptor.Facts.List)
            {
                if (!(fact is Kingmaker.UnitLogic.Feature feature)) continue;
                if (feature.Blueprint == null) continue;

                var key   = (feature.Blueprint.AssetGuid.ToString(), feature.SourceLevel);
                var param = ExtractParam(feature.Param);
                if (param != null && !map.ContainsKey(key))
                    map[key] = param;
            }
            return map;
        }

        static string GetFeatureParam(
            SimpleBlueprint featureBp,
            int level,
            Dictionary<(string guid, int level), string> featuresByLevel)
        {
            var key = (featureBp.AssetGuid.ToString(), level);
            return featuresByLevel.TryGetValue(key, out var param) ? param : null;
        }

        static string ExtractParam(Kingmaker.Blueprints.Classes.Selection.FeatureParam param)
        {
            if (param == null) return null;
            if (param.Blueprint != null) return param.Blueprint.AssetGuid.ToString();
            if (param.WeaponCategory.HasValue) return param.WeaponCategory.Value.ToString();
            if (param.SpellSchool.HasValue)    return param.SpellSchool.Value.ToString();
            if (param.StatType.HasValue)       return param.StatType.Value.ToString();
            return null;
        }
    }
}