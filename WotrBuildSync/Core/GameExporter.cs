using System.Collections.Generic;
using System.Reflection;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp.Actions;
using Newtonsoft.Json;
using WotrBuildSync.Schema;

namespace WotrBuildSync.Core
{
    public static class GameExporter
    {
        static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly FieldInfo FieldLevelPlans =
            typeof(UnitProgressionData).GetField("m_LevelPlans", NonPublicInstance);

        static readonly FieldInfo FieldMythicLevelPlans =
            typeof(UnitProgressionData).GetField("m_MythicLevelPlans", NonPublicInstance);

        static readonly FieldInfo FieldSelectFeatureItem =
            typeof(SelectFeature).GetField("m_ItemFeature", NonPublicInstance);

        static readonly FieldInfo FieldSelectFeatureParam =
            typeof(SelectFeature).GetField("m_ItemParam", NonPublicInstance);

        public static string Export(UnitEntityData unit)
        {
            return JsonConvert.SerializeObject(BuildFrom(unit), Formatting.Indented);
        }

        public static PureBuild BuildFrom(UnitEntityData unit)
        {
            var prog = unit.Descriptor.Progression;
            var type = DetectType(unit);

            var levelPlans = (List<LevelPlanData>)FieldLevelPlans.GetValue(prog);
            var mythicPlans = (List<LevelPlanData>)FieldMythicLevelPlans.GetValue(prog);

            return new PureBuild
            {
                Type = type,
                Origin = type != CharacterType.Companion ? ExtractOrigin(unit, levelPlans) : null,
                LevelPath = ExtractPath(levelPlans),
                MythicPath = mythicPlans != null && mythicPlans.Count > 0 ? ExtractPath(mythicPlans) : null,
            };
        }

        static CharacterType DetectType(UnitEntityData unit)
        {
            if (unit.IsCustomCompanion())
                return CharacterType.Mercenary;
            if (unit.IsStoryCompanion())
                return CharacterType.Companion;
            return CharacterType.Main;
        }

        static CharacterOrigin ExtractOrigin(UnitEntityData unit, List<LevelPlanData> levelPlans)
        {
            var prog = unit.Descriptor.Progression;

            var scores = new Dictionary<string, int>
            {
                { "Strength",     10 },
                { "Dexterity",    10 },
                { "Constitution", 10 },
                { "Intelligence", 10 },
                { "Wisdom",       10 },
                { "Charisma",     10 },
            };

            string raceStatBonus = null;
            string alignment = unit.Descriptor.Alignment.ValueRaw.ToString();

            if (levelPlans != null && levelPlans.Count > 0)
            {
                foreach (var action in levelPlans[0].Actions)
                {
                    if (action is AddStatPoint addStat)
                    {
                        var key = addStat.Attribute.ToString();
                        if (scores.ContainsKey(key))
                            scores[key]++;
                    }
                    else if (action is SelectRaceStat raceStat)
                    {
                        raceStatBonus = raceStat.Attribute.ToString();
                    }
                }
            }

            return new CharacterOrigin
            {
                RaceGuid = prog.Race?.AssetGuid.ToString(),
                Alignment = alignment,
                RaceStatBonus = raceStatBonus,
                BaseAbilityScores = scores,
            };
        }

        static List<Schema.LevelEntry> ExtractPath(List<LevelPlanData> plans)
        {
            var entries = new List<Schema.LevelEntry>();
            if (plans == null) return entries;

            foreach (var plan in plans)
            {
                var entry = new Schema.LevelEntry { Level = plan.Level };

                foreach (var action in plan.Actions)
                {
                    switch (action)
                    {
                        case SelectClass selectClass:
                            entry.ClassGuid = selectClass.CharacterClass?.AssetGuid.ToString();
                            break;

                        case AddArchetype addArchetype:
                            if (addArchetype.Archetype != null)
                                entry.ArchetypeGuids.Add(addArchetype.Archetype.AssetGuid.ToString());
                            break;

                        case SelectFeature selectFeature:
                            entry.Selections.Add(ExtractFeatureSelection(selectFeature));
                            break;

                        case SpendSkillPoint spendSkill:
                        {
                            var key = spendSkill.Skill.ToString();
                            if (!entry.SkillRanks.ContainsKey(key))
                                entry.SkillRanks[key] = 0;
                            entry.SkillRanks[key]++;
                            break;
                        }

                        case SpendAttributePoint spendAttr:
                            entry.AttributeIncrease = spendAttr.Attribute.ToString();
                            break;

                        case SelectSpell selectSpell:
                            entry.Spells.Add(new SpellEntry
                            {
                                SpellbookGuid = selectSpell.Spellbook?.AssetGuid.ToString(),
                                SpellListGuid  = selectSpell.SpellList?.AssetGuid.ToString(),
                                SpellLevel     = selectSpell.SpellLevel,
                                SpellGuid      = selectSpell.Spell?.AssetGuid.ToString(),
                                SlotIndex      = selectSpell.SlotIndex,
                            });
                            break;
                    }
                }

                entries.Add(entry);
            }

            return entries;
        }

        static FeatureSelectionEntry ExtractFeatureSelection(SelectFeature action)
        {
            var featureBlueprint = FieldSelectFeatureItem.GetValue(action) as Kingmaker.Blueprints.Classes.BlueprintFeature;
            var param = FieldSelectFeatureParam.GetValue(action);

            string selectionGuid = null;
            if (action.Selection is SimpleBlueprint selectionBp)
                selectionGuid = selectionBp.AssetGuid.ToString();

            return new FeatureSelectionEntry
            {
                SelectionGuid  = selectionGuid,
                SelectionIndex = action.SelectionIndex,
                FeatureGuid    = featureBlueprint?.AssetGuid.ToString(),
                Parameter      = ExtractParam(param),
            };
        }

        static string ExtractParam(object param)
        {
            if (param == null) return null;
            if (param is SimpleBlueprint bp)
                return bp.AssetGuid.ToString();
            return param.ToString();
        }
    }
}
