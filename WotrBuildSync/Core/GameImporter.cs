using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.ElementsSystem;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Persistence.JsonUtility;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Class.LevelUp.Actions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityModManagerNet;
using WotrBuildSync.Schema;
using FeatureSelectionEntry = WotrBuildSync.Schema.FeatureSelectionEntry;
using LevelEntry = WotrBuildSync.Schema.LevelEntry;

namespace WotrBuildSync.Core
{
    public static class GameImporter
    {
        // ── 공개 진입점 ──────────────────────────────────────────────────────

        public static void Import(UnitEntityData unit, string jsonPath)
        {
            var json = File.ReadAllText(jsonPath);
            var build = JsonConvert.DeserializeObject<PureBuild>(json);
            Apply(unit, build);
        }

        public static void ImportFull(UnitEntityData unit, string jsonPath)
        {
            var json = File.ReadAllText(jsonPath);
            var build = JsonConvert.DeserializeObject<PureBuild>(json);
            FullRespec(unit, build);
        }

        // ── 델타 임포트 (같은 클래스 구조, 피처 선택만 교체) ────────────────

        /// <summary>
        /// PureBuild의 Selection을 현재 캐릭터와 비교해 다른 것만 ReplaceFeature로 교체한다.
        /// 클래스/아키타입/스킬/스탯 변경은 미지원.
        /// </summary>
        public static void Apply(UnitEntityData unit, PureBuild build)
        {
            var log = Main.ModEntry.Logger;
            var prog = unit.Descriptor.Progression;
            int changed = 0;

            log.Log($"[Import] 시작 — CharLv={prog.CharacterLevel} MythicLv={prog.MythicLevel}");

            foreach (var entry in build.LevelPath)
                changed += ApplySelectionDelta(prog, entry, log);
            if (build.MythicPath != null)
                foreach (var entry in build.MythicPath)
                    changed += ApplySelectionDelta(prog, entry, log);

            ApplySpells(unit, build);
            log.Log($"[Import] 완료 — {changed}개 교체");
        }

        static int ApplySelectionDelta(UnitProgressionData prog, LevelEntry entry,
            UnityModManager.ModEntry.ModLogger log)
        {
            int changed = 0;
            foreach (var sel in entry.Selections)
            {
                var selBp = Bp<BlueprintFeatureSelection>(sel.SelectionGuid);
                if (selBp == null) continue;
                var newFeatBp = Bp<BlueprintFeature>(sel.FeatureGuid);
                if (newFeatBp == null) continue;

                var current = prog.GetSelections(selBp, entry.Level);
                var currentFeat = current.Count > sel.SelectionIndex ? current[sel.SelectionIndex] : null;

                if (currentFeat == newFeatBp) continue;

                if (currentFeat != null)
                {
                    log.Log($"[Import] {selBp.name} lv={entry.Level}[{sel.SelectionIndex}]: {currentFeat.name} → {newFeatBp.name}");
                    prog.ReplaceFeature(currentFeat, newFeatBp);
                    changed++;
                }
                else
                {
                    // 기존 선택 없음 → AddSelection으로 직접 추가
                    if (TryAddSelection(prog, selBp, newFeatBp, entry.Level, log))
                        changed++;
                }
            }
            return changed;
        }

        // AddSelection(BlueprintFeatureSelection, FeatureSource, Int32, BlueprintFeature) 캐시
        static MethodInfo s_addSelectionDirect;
        static Type s_featureSourceType;

        static void EnsureAddSelectionCache(UnityModManager.ModEntry.ModLogger log)
        {
            if (s_addSelectionDirect != null) return;

            // FeatureSource 타입 탐색
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { s_featureSourceType = asm.GetType("Kingmaker.UnitLogic.FeatureSource"); }
                catch { }
                if (s_featureSourceType != null) break;
            }

            // 시그니처: AddSelection(BlueprintFeatureSelection, FeatureSource, Int32, BlueprintFeature)
            s_addSelectionDirect = typeof(UnitProgressionData).GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == "AddSelection" &&
                    m.GetParameters().Length == 4 &&
                    m.GetParameters()[3].ParameterType == typeof(BlueprintFeature));

            log.Log(s_addSelectionDirect != null
                ? $"[Import] AddSelection(selection, FeatureSource, level, feature) 확인됨"
                : "[Import] AddSelection 4-param 없음 — 폴백 사용");
        }

        static bool TryAddSelection(UnitProgressionData prog,
            BlueprintFeatureSelection selBp, BlueprintFeature featBp,
            int level, UnityModManager.ModEntry.ModLogger log)
        {
            EnsureAddSelectionCache(log);

            // ── 방법 1: AddSelection(selection, FeatureSource, level, feature) ──
            // Items 검색 불필요 — featBp를 직접 마지막 인자로 전달
            if (s_addSelectionDirect != null && s_featureSourceType != null)
            {
                try
                {
                    // FeatureSource는 struct → default 인스턴스 생성 (class 없음)
                    // 주의: FeatureSource에는 원래 BlueprintCharacterClass + 레벨 정보가 필요할 수 있음.
                    // 빈 struct로 호출하면 selection 레지스트리에는 등록되지만
                    // 게임이 해당 피처를 실제로 Fact로 활성화하는지 미확인 (import-status.md Issue #1).
                    object source = s_featureSourceType.IsValueType
                        ? Activator.CreateInstance(s_featureSourceType)
                        : null;
                    s_addSelectionDirect.Invoke(prog, new object[] { selBp, source, level, featBp });
                    log.Log($"[Import] AddSelection 성공: {selBp.name} lv={level} → {featBp.name}");
                    return true;
                }
                catch (Exception ex)
                {
                    log.Log($"[Import] AddSelection 오류({ex.InnerException?.Message ?? ex.Message}) — Features.AddFeature 시도");
                }
            }

            // ── 방법 2: Features.AddFeature ──────────────────────────────────
            try
            {
                var featuresObj = prog.GetType()
                    .GetProperty("Features", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(prog);
                if (featuresObj == null) { log.Warning("[Import] prog.Features 없음"); return false; }

                var hasFact = featuresObj.GetType().GetMethod("HasFact",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(BlueprintFeature) }, null);
                if (hasFact != null && (bool)hasFact.Invoke(featuresObj, new object[] { featBp }))
                {
                    log.Log($"[Import] {featBp.name} 이미 있음"); return false;
                }

                var addFeat = featuresObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AddFeature" &&
                        m.GetParameters().Length >= 1 &&
                        typeof(BlueprintScriptableObject).IsAssignableFrom(m.GetParameters()[0].ParameterType));
                if (addFeat == null) { log.Warning("[Import] Features.AddFeature 없음"); return false; }

                var pms = addFeat.GetParameters();
                addFeat.Invoke(featuresObj, pms.Length == 1
                    ? new object[] { featBp }
                    : new object[] { featBp, null });
                log.Log($"[Import] Features.AddFeature 성공(폴백): {featBp.name}");
                return true;
            }
            catch (Exception ex) { log.Log($"[Import] Features.AddFeature 오류: {ex.Message}"); }

            log.Warning($"[Import] {selBp.name} → {featBp.name} 추가 실패");
            return false;
        }

        static void ApplySpells(UnitEntityData unit, PureBuild build)
        {
            var allSpells = new List<SpellEntry>();
            foreach (var e in build.LevelPath) allSpells.AddRange(e.Spells);
            if (build.MythicPath != null)
                foreach (var e in build.MythicPath) allSpells.AddRange(e.Spells);

            foreach (var spellEntry in allSpells)
            {
                var sbBp = Bp<BlueprintSpellbook>(spellEntry.SpellbookGuid);
                if (sbBp == null) continue;
                var spellbook = unit.Descriptor.GetSpellbook(sbBp);
                if (spellbook == null) continue;
                var spellBp = Bp<BlueprintAbility>(spellEntry.SpellGuid);
                if (spellBp == null) continue;

                if (!spellbook.IsKnown(spellBp))
                    spellbook.AddKnown(spellEntry.SpellLevel, spellBp);
            }
        }

        // ── 풀 리스펙 (클래스/아키타입 포함 전체 재빌드) ────────────────────

        /// <summary>
        /// 진공 유닛을 생성해 PureBuild로 레벨업한 뒤, JSON으로 원본 유닛에 복사한다.
        /// 일반 레벨(1~20)은 LevelPlanData + LevelUpController로 처리.
        /// 신화 레벨은 복사 후 델타 방식으로 처리.
        /// </summary>
        public static void FullRespec(UnitEntityData unit, PureBuild build)
        {
            var log = Main.ModEntry.Logger;
            log.Log($"[FullRespec] 시작 — CharLv={unit.Descriptor.Progression.CharacterLevel} MythicLv={unit.Descriptor.Progression.MythicLevel}");

            var origPosition = unit.Position;

            var applyPlanMethod = typeof(LevelUpController).GetMethod(
                "ApplyLevelUpPlan",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var mMythicDisabledField = typeof(UnitProgressionData).GetField(
                "m_MythicLevelPlanDisabled",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            // 1. 진공 유닛 생성 (아이템 없이)
            UnitEntityData newUnit;
            using (ContextData<UnitEntityData.DoNotCreateItems>.Request())
                newUnit = Game.Instance.CreateUnitVacuum(unit.Blueprint);

            // 2. XP 설정
            newUnit.Progression.AdvanceExperienceTo(unit.Descriptor.Progression.Experience, log: false);
            newUnit.Progression.AdvanceMythicExperience(unit.Descriptor.Progression.MythicExperience, log: false);

            // 3. 일반 레벨 플랜 구성 및 추가
            foreach (var entry in build.LevelPath)
            {
                var plan = BuildLevelPlan(entry, log);
                if (plan != null)
                    newUnit.Progression.AddLevelPlan(plan);
            }

            // 4. 일반 레벨 순차 적용
            int safety = 0;
            while (safety++ < 25 &&
                   newUnit.Progression.CanLevelUpNormal &&
                   newUnit.Progression.GetNextLevelPlan(false) != null)
            {
                int lvBefore = newUnit.Progression.CharacterLevel;
                var ctrl = LevelUpController.Start(newUnit, LevelUpState.CharBuildMode.LevelUp,
                    instantCommit: false, ignoreSettings: true);
                if (ctrl.LevelUpActions.Count == 0 && applyPlanMethod != null)
                    applyPlanMethod.Invoke(ctrl, new object[] { true });
                ctrl.Commit();
                if (newUnit.Progression.CharacterLevel == lvBefore)
                {
                    log.Log("[FullRespec] 레벨업 진행 없음 — 중단");
                    break;
                }
                log.Log($"[FullRespec] 일반 레벨 {newUnit.Progression.CharacterLevel} 적용");
            }

            if (Game.Instance.LevelUpController?.Unit == newUnit)
                Game.Instance.LevelUpController = null;
            log.Log($"[FullRespec] 진공 유닛 일반 레벨 완료: {newUnit.Progression.CharacterLevel}");

            // 5. 신화 레벨 적용 (copy-back 전, 진공 유닛에)
            // GetNextLevelPlan(true) 패치로 LevelUpPlanProviders 의존성 우회
            // 신화 실패 시에도 copy-back은 반드시 실행되어야 하므로 전체 try-catch
            if (build.MythicPath != null)
            {
                try
                {
                    foreach (var entry in build.MythicPath)
                    {
                        var mythicPlan = BuildMythicLevelPlan(entry, log);
                        if (mythicPlan == null)
                        {
                            log.Log($"[FullRespec] 신화 플랜 빌드 실패 lv={entry.Level} — 건너뜀");
                            continue;
                        }

                        mMythicDisabledField?.SetValue(newUnit.Descriptor.Progression, false);
                        int mythicBefore = newUnit.Descriptor.Progression.MythicLevel;

                        CharInfoPatches.GetNextLevelPlanOverride = mythicPlan;
                        // CanSelectClass 우회: Start() 안에서 SelectClass가 실행되므로 여기서 활성화
                        CharInfoPatches.BypassMythicClassCheck = true;
                        try
                        {
                            var ctrl = LevelUpController.Start(newUnit, LevelUpState.CharBuildMode.Mythic,
                                instantCommit: false, ignoreSettings: true);
                            if (ctrl != null)
                            {
                                if (ctrl.LevelUpActions.Count == 0 && applyPlanMethod != null)
                                {
                                    applyPlanMethod.Invoke(ctrl, new object[] { true });
                                }

                                // 액션 목록 진단
                                var sbA = new System.Text.StringBuilder();
                                sbA.AppendLine($"[FullRespec] Mythic Start OK — actions={ctrl.LevelUpActions.Count} charLv={newUnit.Progression.CharacterLevel}");
                                foreach (var a in ctrl.LevelUpActions)
                                    sbA.AppendLine($"  {a?.GetType().Name}");
                                log.Log(sbA.ToString());

                                                // 주입: SelectedClass + NextObligatoryMythicClass 동시 설정
                                var classBp = Bp<BlueprintCharacterClass>(entry.ClassGuid);
                                if (classBp != null) InjectSelectedClass(ctrl, classBp, log);
                                // UnitProgressionData 메서드 덤프 (첫 번째 신화 레벨에서만)
                                if (entry.Level == 1) DumpProgressionMethods(newUnit, log);

                                ctrl.Commit();
                                log.Log($"[FullRespec] Commit 후 charLv={newUnit.Progression.CharacterLevel} mythicLv={newUnit.Descriptor.Progression.MythicLevel}");
                            }
                            else
                            {
                                log.Log($"[FullRespec] Mythic Start null — lv={entry.Level}");
                            }
                        }
                        finally
                        {
                            CharInfoPatches.BypassMythicClassCheck = false;
                            CharInfoPatches.GetNextLevelPlanOverride = null;
                        }

                        int mythicAfter = newUnit.Descriptor.Progression.MythicLevel;
                        if (mythicAfter <= mythicBefore)
                        {
                            log.Log($"[FullRespec] 신화 레벨 {entry.Level} 적용 실패 (mythic {mythicBefore}→{mythicAfter}) — 중단");
                            break;
                        }
                        log.Log($"[FullRespec] 신화 레벨 {mythicAfter} 적용");
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"[FullRespec] 신화 루프 오류 (copy-back은 계속): {ex}");
                }
                finally
                {
                    CharInfoPatches.GetNextLevelPlanOverride = null;
                    if (Game.Instance.LevelUpController?.Unit == newUnit)
                        Game.Instance.LevelUpController = null;
                }
                log.Log($"[FullRespec] 진공 유닛 신화 레벨 완료: {newUnit.Descriptor.Progression.MythicLevel}");
            }

            // 6. 신화 미적용 안전 체크 — copy-back하면 원본 신화 상태가 파괴되므로 중단
            int expectedMythicLevels = build.MythicPath?.Count ?? 0;
            int vacuumMythicLv = newUnit.Descriptor.Progression.MythicLevel;
            if (expectedMythicLevels > 0 && vacuumMythicLv == 0)
            {
                log.Error($"[FullRespec] 신화 {expectedMythicLevels}레벨 적용 실패 (진공 유닛 MythicLv={vacuumMythicLv}) — copy-back 취소, 원본 보존");
                try { if (newUnit.IsInState) newUnit.MarkForDestroy(); else newUnit.Dispose(); } catch { }
                return;
            }

            // 7. 진공 유닛 → 원본 유닛 복사
            try
            {
                CopyUnitDataBack(unit, newUnit, log);
            }
            catch (Exception ex)
            {
                log.Error($"[FullRespec] 데이터 복사 실패: {ex.Message}");
                throw;
            }
            finally
            {
                if (newUnit.IsInState) newUnit.MarkForDestroy();
                else newUnit.Dispose();
            }

            // 위치 복원
            unit.Position = origPosition;

            // 8. 신화 선택 델타 (copy-back 후 누락 선택 보완)
            // 진공 유닛 신화 레벨업 시 SelectFeature.Item이 null인 경우가 많아
            // 플랜에 포함되지 못한 선택을 여기서 AddSelection으로 직접 보완.
            if (build.MythicPath != null)
            {
                int mythicChanged = 0;
                foreach (var entry in build.MythicPath)
                    mythicChanged += ApplySelectionDelta(unit.Descriptor.Progression, entry, log);
                if (mythicChanged > 0)
                    log.Log($"[FullRespec] 신화 선택 보완: {mythicChanged}개");
            }

            // 9. 주문 추가
            // 주의: PostLoad 이후에 호출되므로 AddKnown이 현재 활성 주문책에 반영되지 않을 수 있음.
            // 주문책 UI에는 안 보이지만 이전 주문은 여전히 사용 가능한 증상의 원인 (import-status.md Issue #2).
            ApplySpells(unit, build);

            log.Log($"[FullRespec] 완료 — CharLv={unit.Descriptor.Progression.CharacterLevel} MythicLv={unit.Descriptor.Progression.MythicLevel}");
        }

        // ── 레벨 플랜 빌더 ────────────────────────────────────────────────────

        // 신화 레벨은 SelectClass + SelectFeature만 포함 (스킬/능력치/클래스메카닉 없음)
        static LevelPlanData BuildMythicLevelPlan(LevelEntry entry, UnityModManager.ModEntry.ModLogger log)
        {
            var classBp = Bp<BlueprintCharacterClass>(entry.ClassGuid);
            if (classBp == null)
            {
                log.Warning($"[FullRespec] 신화 클래스 BP 없음: {entry.ClassGuid} (lv={entry.Level})");
                return null;
            }

            var actions = new List<ILevelUpAction>();
            actions.Add(new SelectClass(classBp, ignoreAlignment: true));

            foreach (var sel in entry.Selections ?? new List<FeatureSelectionEntry>())
            {
                var selBp = Bp<BlueprintFeatureSelection>(sel.SelectionGuid);
                var featBp = Bp<BlueprintFeature>(sel.FeatureGuid);
                if (selBp == null || featBp == null) continue;

                var sf = MakeSelectFeature(selBp, sel.SelectionIndex, featBp, sel.Parameter);
                if (sf?.Item != null)
                    actions.Add(sf);
                else
                    log.Warning($"[FullRespec] 신화 SelectFeature Item 없음: sel={selBp.name} feat={featBp.name} lv={entry.Level}");
            }

            var plan = new LevelPlanData(entry.Level, actions.ToArray());
            plan.PostLoad();
            log.Log($"[FullRespec] 신화 플랜 빌드 — lv={entry.Level} class={classBp.name} actions={actions.Count}");
            return plan;
        }

        static LevelPlanData BuildLevelPlan(LevelEntry entry, UnityModManager.ModEntry.ModLogger log)
        {
            var classBp = Bp<BlueprintCharacterClass>(entry.ClassGuid);
            if (classBp == null)
            {
                log.Warning($"[FullRespec] 클래스 BP 없음: {entry.ClassGuid} (lv={entry.Level})");
                return null;
            }

            var actions = new List<ILevelUpAction>();

            // 1. 클래스 선택 (정렬 무시)
            actions.Add(new SelectClass(classBp, ignoreAlignment: true));

            // 2. 아키타입 (클래스 첫 레벨에만 설정됨)
            foreach (var archGuid in entry.ArchetypeGuids ?? new List<string>())
            {
                var archBp = Bp<BlueprintArchetype>(archGuid);
                if (archBp != null)
                    actions.Add(new AddArchetype(classBp, archBp));
            }

            // 3. 클래스 메카닉 적용 (BAB, 세이브, 스킬, 진행표)
            actions.Add(new ApplyClassMechanics());

            // 4. 주문서 초기화
            actions.Add(new ApplySpellbook());

            // 5. 능력치 포인트 (레벨 4, 8, 12, 16, 20)
            if (!string.IsNullOrEmpty(entry.AttributeIncrease) &&
                Enum.TryParse<StatType>(entry.AttributeIncrease, out var attrStat))
            {
                actions.Add(new SpendAttributePoint(attrStat));
            }

            // 6. 스킬 포인트 풀 설정
            actions.Add(new ApplySkillPoints());

            // 7. 스킬 포인트 소비 (각 랭크별 1개씩)
            foreach (var kv in entry.SkillRanks ?? new Dictionary<string, int>())
            {
                if (!Enum.TryParse<StatType>(kv.Key, out var skillStat)) continue;
                for (int i = 0; i < kv.Value; i++)
                    actions.Add(new SpendSkillPoint(skillStat));
            }

            // 8. 피처 선택
            foreach (var sel in entry.Selections ?? new List<FeatureSelectionEntry>())
            {
                var selBp = Bp<BlueprintFeatureSelection>(sel.SelectionGuid);
                var featBp = Bp<BlueprintFeature>(sel.FeatureGuid);
                if (selBp == null || featBp == null) continue;

                var sf = MakeSelectFeature(selBp, sel.SelectionIndex, featBp, sel.Parameter);
                if (sf?.Item != null)
                    actions.Add(sf);
                else
                    log.Warning($"[FullRespec] SelectFeature Item 없음: sel={selBp.name} feat={featBp.name} lv={entry.Level}");
            }

            // 9. 주문은 레벨업 플랜에 포함하지 않음 — 복사 후 직접 AddKnown 사용

            var plan = new LevelPlanData(entry.Level, actions.ToArray());
            plan.PostLoad(); // SelectFeature.Item 초기화
            return plan;
        }

        // ── SelectFeature 직접 생성 (reflection) ────────────────────────────

        static readonly MethodInfo s_selectFeaturePostLoad =
            typeof(SelectFeature).GetMethod("PostLoad", BindingFlags.Public | BindingFlags.Instance);

        static SelectFeature MakeSelectFeature(
            BlueprintFeatureSelection selBp, int selIndex,
            BlueprintFeature featBp, string paramStr)
        {
            var sf = (SelectFeature)FormatterServices.GetUninitializedObject(typeof(SelectFeature));
            SetField(sf, typeof(SelectFeature), "Selection", selBp);
            SetField(sf, typeof(SelectFeature), "SelectionIndex", selIndex);
            SetField(sf, typeof(SelectFeature), "m_ItemFeature", featBp);

            // 파라미터 설정 (Weapon Focus 등)
            var param = ParseFeatureParam(paramStr);
            if (param != null)
                SetField(sf, typeof(SelectFeature), "m_ItemParam", param);

            // Selection.Items에서 Feature == featBp인 항목을 찾아 Item 설정
            s_selectFeaturePostLoad?.Invoke(sf, null);
            return sf;
        }

        static object ParseFeatureParam(string paramStr)
        {
            if (string.IsNullOrEmpty(paramStr)) return null;

            // GUID 형식이면 Blueprint 파라미터
            if (paramStr.Length == 32 || (paramStr.Length == 36 && paramStr.Contains('-')))
            {
                var bp = Bp<BlueprintScriptableObject>(paramStr);
                if (bp != null)
                    return new Kingmaker.Blueprints.Classes.Selection.FeatureParam(bp);
            }

            // WeaponCategory 시도
            if (Enum.TryParse<Kingmaker.Enums.WeaponCategory>(paramStr, out var wc))
                return new Kingmaker.Blueprints.Classes.Selection.FeatureParam(wc);

            // SpellSchool 시도
            if (Enum.TryParse<Kingmaker.Blueprints.Classes.Spells.SpellSchool>(paramStr, out var ss))
                return new Kingmaker.Blueprints.Classes.Selection.FeatureParam(ss);

            // StatType 시도
            if (Enum.TryParse<StatType>(paramStr, out var st))
                return new Kingmaker.Blueprints.Classes.Selection.FeatureParam(st);

            return null;
        }

        static void SetField(object obj, Type type, string fieldName, object value)
        {
            var f = type.GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            f?.SetValue(obj, value);
        }

        // LevelUpController.State.SelectedClass를 직접 반영 (CanSelectClass 검사 우회 후 검증)
        static void InjectSelectedClass(LevelUpController ctrl, BlueprintCharacterClass classBp,
            UnityModManager.ModEntry.ModLogger log)
        {
            try
            {
                // State 획득
                object stateObj = null;
                foreach (var name in new[] { "State", "m_State" })
                {
                    var p = ctrl.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null) { stateObj = p.GetValue(ctrl); if (stateObj != null) break; }
                    var f = ctrl.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) { stateObj = f.GetValue(ctrl); if (stateObj != null) break; }
                }
                if (stateObj == null) { log.Log("[FullRespec] InjectSelectedClass: State 없음"); return; }

                var t = stateObj.GetType();

                // SelectedClass 읽기 — 이미 설정됐는지 확인
                object current = ReadField(stateObj, t, "SelectedClass") ?? ReadField(stateObj, t, "m_SelectedClass");
                log.Log($"[FullRespec] InjectSelectedClass 진입 — 현재 SelectedClass={current}");

                if (current != null && current == (object)classBp)
                {
                    log.Log("[FullRespec] InjectSelectedClass: 이미 올바르게 설정됨");
                    return;
                }

                // backing field 우선 (setter 검증 우회)
                bool injected = false;
                foreach (var candidate in new[] { "<SelectedClass>k__BackingField", "m_SelectedClass", "SelectedClass" })
                {
                    var f = t.GetField(candidate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f == null) continue;
                    f.SetValue(stateObj, classBp);
                    injected = true;
                    break;
                }
                if (!injected)
                {
                    // 프로퍼티 setter 시도
                    var p = t.GetProperty("SelectedClass", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p?.CanWrite == true) { p.SetValue(stateObj, classBp); injected = true; }
                }

                // NextObligatoryMythicClass도 설정 (Mythic 모드에서 ApplyClassMechanics가 이걸 볼 수 있음)
                bool injectedObligatory = false;
                foreach (var candidate in new[] { "NextObligatoryMythicClass", "m_NextObligatoryMythicClass",
                                                  "<NextObligatoryMythicClass>k__BackingField" })
                {
                    var f = t.GetField(candidate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) { f.SetValue(stateObj, classBp); injectedObligatory = true; break; }
                    var p = t.GetProperty(candidate, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p?.CanWrite == true) { p.SetValue(stateObj, classBp); injectedObligatory = true; break; }
                }

                // 검증: 읽어서 실제로 설정됐는지 확인
                object readback = ReadField(stateObj, t, "SelectedClass") ?? ReadField(stateObj, t, "m_SelectedClass");
                object readbackObl = ReadField(stateObj, t, "NextObligatoryMythicClass");
                log.Log($"[FullRespec] InjectSelectedClass {(injected ? "시도" : "실패")}: SelectedClass={readback}  NextObligatoryMythicClass={readbackObl} (obl주입={injectedObligatory})");

                if (readback == null || readback != (object)classBp)
                {
                    // 필드 전체 덤프 (디버그)
                    var sb = new System.Text.StringBuilder("[FullRespec] LevelUpState 필드 목록:");
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                        sb.AppendLine($"  {f.FieldType.Name} {f.Name} = {TrySafeGet(f, stateObj)}");
                    log.Log(sb.ToString());
                }
            }
            catch (Exception ex)
            {
                log.Log($"[FullRespec] InjectSelectedClass 오류: {ex.Message}");
            }
        }

        static void DumpProgressionMethods(UnitEntityData unit, UnityModManager.ModEntry.ModLogger log)
        {
            try
            {
                var t = unit.Descriptor.Progression.GetType();
                var sb = new System.Text.StringBuilder($"[Diag] UnitProgressionData 메서드 ({t.Name}):");
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                {
                    var ps = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    sb.AppendLine($"  [{m.DeclaringType?.Name}] {m.ReturnType.Name} {m.Name}({ps})");
                }
                log.Log(sb.ToString());
            }
            catch (Exception ex) { log.Log($"[Diag] DumpProgressionMethods 오류: {ex.Message}"); }
        }

        static object ReadField(object obj, Type t, string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (f != null) try { return f.GetValue(obj); } catch { }
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (p != null) try { return p.GetValue(obj); } catch { }
            return null;
        }

        static string TrySafeGet(FieldInfo f, object obj)
        {
            try { return f.GetValue(obj)?.ToString() ?? "null"; } catch { return "(err)"; }
        }

        // ── 진공 유닛 → 원본 유닛 데이터 복사 ───────────────────────────────
        // 전체 유닛을 복사하되, 이름(CustomName)만 원본을 보존한다.
        // 이름은 블루프린트 기본값("성전 사령관")이 진공 유닛에 상속되므로 덮어써지는 것을 방지.

        static void CopyUnitDataBack(
            UnitEntityData target, UnitEntityData source,
            UnityModManager.ModEntry.ModLogger log)
        {
            log.Log("[FullRespec] 유닛 데이터 복사 시작");

            DumpDescriptorFields(target, "복사전-원본", log);
            DumpDescriptorFields(source, "복사전-진공", log);

            // 이름 필드를 복사 전에 저장
            var nameField = FindCustomNameField(target.Descriptor);
            string savedName = nameField?.GetValue(target.Descriptor) as string;
            log.Log($"[FullRespec] 이름 보존: '{savedName}'");

            var origResolver = DefaultJsonSettings.DefaultSettings.ContractResolver;
            try
            {
                DefaultJsonSettings.DefaultSettings.ContractResolver = new RespecContractResolver();
                var serializer = JsonSerializer.Create(DefaultJsonSettings.DefaultSettings);

                var jObj = JObject.FromObject(source, serializer);
                jObj.Remove("UniqueId");
                jObj.Remove("m_AutoUseAbility");

                if (jObj["Descriptor"] is JObject desc)
                {
                    // m_Inventory: 아이템은 진공 유닛이 DoNotCreateItems로 생성됐으므로 비어있음 — 원본 유지
                    desc.Remove("m_Inventory");
                    // Body: 외형/장비 모델 관련 — 덮어쓰면 인벤토리 3D 미리보기 소실 가능성 (확인 필요)
                    desc.Remove("Body");
                    desc.Remove("UISettings");
                }

                var jsonStr = jObj.ToString()
                    .Replace(source.UniqueId, target.UniqueId);

                JsonConvert.PopulateObject(jsonStr, target, DefaultJsonSettings.DefaultSettings);
            }
            finally
            {
                DefaultJsonSettings.DefaultSettings.ContractResolver = origResolver;
            }

            // 이름 복원 (복사로 덮어쓰여진 경우)
            if (nameField != null && savedName != null)
            {
                nameField.SetValue(target.Descriptor, savedName);
                log.Log($"[FullRespec] 이름 복원: '{savedName}'");
            }

            // PostLoad로 내부 상태 재초기화
            // ForcePostLoad: 이미 로드된 유닛에도 PostLoad를 강제 실행
            // Respec: 게임이 리스펙 컨텍스트임을 인식하도록 (일부 서브시스템 재초기화 분기)
            // 주의: PostLoad가 주문책·Facts 상태를 덮어쓸 수 있으므로 ApplySpells는 이후에 호출해야 함
            using (ContextData<EntityDataBase.ForcePostLoad>.Request())
            using (ContextData<UnitEntityData.Respec>.Request())
            {
                target.PostLoad();
            }

            DumpDescriptorFields(target, "복사후-원본", log);

            // 파티 상태 진단 — 두 번째 리스펙에서 뷰가 사라지는 원인 파악용 (import-status.md Issue 4)
            DumpUnitPartyState(target, log);

            log.Log("[FullRespec] 유닛 데이터 복사 완료");
        }

        static void DumpDescriptorFields(UnitEntityData unit, string label, UnityModManager.ModEntry.ModLogger log)
        {
            try
            {
                var desc = unit.Descriptor;
                var t = desc.GetType();
                var sb = new System.Text.StringBuilder($"[DescriptorDump:{label}] 필드 목록:");
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                {
                    try
                    {
                        var val = f.GetValue(desc);
                        var valStr = val == null ? "null" : $"({val.GetType().Name}) {val}";
                        sb.AppendLine($"  {f.FieldType.Name} {f.Name} = {valStr}");
                    }
                    catch { sb.AppendLine($"  {f.FieldType.Name} {f.Name} = (err)"); }
                }
                log.Log(sb.ToString());
            }
            catch (Exception ex) { log.Log($"[DescriptorDump] 오류: {ex.Message}"); }
        }

        static void DumpUnitPartyState(UnitEntityData unit, UnityModManager.ModEntry.ModLogger log)
        {
            try
            {
                var player = Game.Instance?.Player;
                bool inParty     = player?.Party?.Contains(unit) == true;
                bool inRemote    = player?.RemoteCompanions?.Contains(unit) == true;
                bool isInGame    = unit.IsInGame;
                var sb = new System.Text.StringBuilder("[FullRespec] 파티 상태 진단:");
                sb.AppendLine($"  IsInGame={isInGame}  inParty={inParty}  inRemote={inRemote}");
                sb.AppendLine($"  CharLv={unit.Descriptor.Progression.CharacterLevel}  MythicLv={unit.Descriptor.Progression.MythicLevel}");

                // UnitEntityData / EntityDataBase 필드 중 IsInGame 관련 탐색
                foreach (var f in unit.GetType().GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                {
                    var n = f.Name.ToLower();
                    if (!n.Contains("ingame") && !n.Contains("state") && !n.Contains("hold")) continue;
                    try { sb.AppendLine($"  {f.Name} = {f.GetValue(unit)}"); } catch { }
                }
                log.Log(sb.ToString());
            }
            catch (Exception ex) { log.Log($"[FullRespec] DumpUnitPartyState 오류: {ex.Message}"); }
        }

        // UnitDescriptor에서 캐릭터 커스텀 이름 필드를 반사로 탐색
        static FieldInfo FindCustomNameField(object descriptor)
        {
            var t = descriptor.GetType();
            foreach (var candidate in new[] { "CustomName", "m_CustomName", "m_OverrideName", "m_Name" })
            {
                var f = t.GetField(candidate,
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (f != null && f.FieldType == typeof(string))
                    return f;
            }
            return null;
        }

        // ── 공용 헬퍼 ────────────────────────────────────────────────────────

        static T Bp<T>(string guid) where T : BlueprintScriptableObject
        {
            if (string.IsNullOrEmpty(guid)) return null;
            return ResourcesLibrary.TryGetBlueprint<T>(guid);
        }
    }
}
