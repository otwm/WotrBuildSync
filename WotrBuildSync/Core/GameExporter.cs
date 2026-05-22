using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Blueprints.Classes.Selection;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Newtonsoft.Json;
using WotrBuildSync.Schema;

namespace WotrBuildSync.Core
{
    public static class GameExporter
    {
        public static void BruteForceDiagnostic(UnitEntityData unit, System.Action<string> log)
        {
            log("=== [무차별 대입] 데이터 발굴 시작 ===");
            var prog = unit.Descriptor.Progression;

            // 1. 유닛이 가진 모든 '피처(Feature)' 중 이름에 'Attribute'가 들어간 것 전수 조사
            log("--- [1] 모든 피처 중 Attribute 검색 ---");
            foreach (var f in prog.Features)
            {
                if (f.Blueprint.name.IndexOf("Attribute", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    log(
                        $"발견 피처: {f.Blueprint.name} | Level: {f.SourceLevel} | Source: {f.SourceProgression?.name ?? "null"}");
                }
            }

            // 2. 유닛이 가진 모든 '진행(Progression)' 객체 조사 (리플렉션 사용)
            log("--- [2] 모든 진행(m_Progressions) 데이터 조사 ---");
            try
            {
                var field = prog.GetType().GetField("m_Progressions",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var m_Progressions = field?.GetValue(prog) as System.Collections.IDictionary;
                if (m_Progressions != null)
                {
                    foreach (System.Collections.DictionaryEntry de in m_Progressions)
                    {
                        var bp = de.Key as Kingmaker.Blueprints.SimpleBlueprint;
                        log($"진행 BP: {bp?.name}");
                        // 각 진행 BP의 레벨 엔트리 조사
                        var pBp = bp as Kingmaker.Blueprints.Classes.BlueprintProgression;
                        if (pBp != null)
                        {
                            foreach (var entry in pBp.LevelEntries)
                            {
                                log($"  -> BP Level {entry.Level}: {entry.Features.Count}개 항목");
                                foreach (var f in entry.Features)
                                {
                                    if (f.name.Contains("Attribute") || f.name.Contains("Selection"))
                                        log($"     - {f.name}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log($"에러: {e.Message}");
            }

            // 3. Selections 데이터 재검증
            log("--- [3] Selections 데이터 재검증 ---");
            foreach (var kvp in prog.Selections)
            {
                log($"Selection Key: {kvp.Key.name}");
                // 모든 레벨의 데이터를 다 찍어봄
                foreach (var lv in kvp.Value.SelectionsByLevel)
                {
                    foreach (var feat in lv.Value)
                    {
                        log($"  -> Level {lv.Key}: {feat?.name}");
                    }
                }
            }

            log("=== [무차별 대입] 종료 ===");
        }

        public static void FinalDiagnostic(UnitEntityData unit, System.Action<string> log)
        {
            log("=== [진단] GetSelections 데이터 확정 스캔 시작 ===");
            var prog = unit.Descriptor.Progression;
            var featsProg = Kingmaker.Blueprints.Root.BlueprintRoot.Instance.Progression.FeatsProgression;

            if (featsProg == null)
            {
                log("FeatsProgression을 찾을 수 없습니다.");
                return;
            }

            foreach (var entry in featsProg.LevelEntries)
            {
                // 4, 8, 12 레벨 집중 조사
                if (entry.Level > 0 && entry.Level % 4 == 0)
                {
                    log($"--- Level {entry.Level} 조사 ---");
                    foreach (var feature in entry.Features)
                    {
                        if (feature is Kingmaker.Blueprints.Classes.Selection.BlueprintFeatureSelection selection)
                        {
                            log($"  [Selection] {selection.name}");

                            // 리플렉션으로 GetSelectionsForProgession (오타 버전)과 GetSelectionsForProgression (정상 버전) 모두 시도
                            var methods = new[] { "GetSelectionsForProgession", "GetSelectionsForProgression" };
                            foreach (var methodName in methods)
                            {
                                try
                                {
                                    var method = prog.GetType().GetMethod(methodName,
                                        new Type[]
                                        {
                                            typeof(Kingmaker.Blueprints.Classes.Selection.BlueprintFeatureSelection),
                                            typeof(int), typeof(Kingmaker.Blueprints.Classes.BlueprintProgression)
                                        });

                                    if (method != null)
                                    {
                                        var result =
                                            method.Invoke(prog, new object[] { selection, entry.Level, featsProg }) as
                                                System.Collections.IEnumerable;
                                        if (result != null)
                                        {
                                            foreach (var item in result)
                                            {
                                                log($"    -> [{methodName}] 발견: {item}");
                                            }
                                        }
                                        else
                                        {
                                            log($"    -> [{methodName}] 결과가 null입니다.");
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    log($"    -> [{methodName}] 호출 에러: {e.Message}");
                                }
                            }
                        }
                    }
                }
            }

            log("=== [진단] 종료 ===");
        }

        public static void BruteForceHunt(UnitEntityData unit, System.Action<string> log)
        {
            log("=== [브루트 포스] 독수리 아이콘(AttributeIncrease) 정체 찾기 ===");
            var prog = unit.Descriptor.Progression;

            // 1. m_Selections의 모든 키(Selection)를 필터링 없이 전부 출력
            log("--- [1] 모든 Selection Key 및 데이터 전수 조사 ---");
            var selectionsField = prog.GetType().GetField("m_Selections",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var selections = selectionsField?.GetValue(prog) as System.Collections.IDictionary;

            if (selections != null)
            {
                foreach (System.Collections.DictionaryEntry entry in selections)
                {
                    var key = entry.Key as Kingmaker.Blueprints.SimpleBlueprint;
                    log($"[Selection Key] {key?.name} (GUID: {key?.AssetGuid})");

                    // 해당 선택지 내부의 모든 레벨별 데이터 출력
                    var selData = entry.Value;
                    var lvField = selData.GetType().GetField("m_SelectionsByLevel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var lvDict = lvField?.GetValue(selData) as System.Collections.IDictionary;
                    if (lvDict != null)
                    {
                        foreach (System.Collections.DictionaryEntry le in lvDict)
                        {
                            if (le.Value is System.Collections.IEnumerable list)
                            {
                                foreach (var item in list)
                                {
                                    log($"  -> Level {le.Key}: {item}");
                                }
                            }
                        }
                    }
                }
            }

            // 2. unit.Descriptor.Facts (모든 특기/능력) 중 숨겨진 것까지 조사
            log("--- [2] 모든 Facts(Features) 중 'Attribute' 포함된 것 찾기 ---");
            foreach (var fact in unit.Descriptor.Facts.List)
            {
                if (fact.Blueprint.name.IndexOf("Attribute", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    fact.Blueprint.name.IndexOf("Stat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    log(
                        $"[Fact 발견] Name: {fact.Blueprint.name}, Source: {fact.MaybeContext?.AssociatedBlueprint?.name ?? "null"}");
                }
            }

            log("=== [브루트 포스] 종료 ===");
        }


        public static void FinalHunt(UnitEntityData unit, System.Action<string> log)
        {
            log("=== [최종 헌트] BasicFeatsProgression 비밀 주머니 열기 ===");
            var prog = unit.Descriptor.Progression;
            var m_ProgressionsField = prog.GetType().GetField("m_Progressions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var m_Progressions = m_ProgressionsField?.GetValue(prog) as System.Collections.IDictionary;

            if (m_Progressions != null)
            {
                foreach (System.Collections.DictionaryEntry de in m_Progressions)
                {
                    var bp = de.Key as Kingmaker.Blueprints.SimpleBlueprint;
                    if (bp != null && bp.name.Contains("BasicFeatsProgression"))
                    {
                        log($"[대상 발견] {bp.name} 내부 전수 조사");
                        var pData = de.Value; // ProgressionData 객체

                        // ProgressionData의 모든 비공개 필드를 뒤져서 '데이터가 들어있는' 필드 찾기
                        var fields = pData.GetType().GetFields(System.Reflection.BindingFlags.NonPublic |
                                                               System.Reflection.BindingFlags.Instance);
                        foreach (var f in fields)
                        {
                            var val = f.GetValue(pData);
                            if (val == null) continue;

                            // 1. 만약 딕셔너리(m_Selections 등)라면 내부 키/값 쌍을 모두 출력
                            if (val is System.Collections.IDictionary dict)
                            {
                                log($"  - 필드 {f.Name} (Dictionary) 내용:");
                                foreach (System.Collections.DictionaryEntry kvp in dict)
                                {
                                    log($"    Key: {kvp.Key} | Value: {kvp.Value}");
                                    // Value가 FeatureSelectionData라면 그 안의 레벨별 데이터까지 추적
                                    var m_SelectionsByLevelField = kvp.Value?.GetType().GetField("m_SelectionsByLevel",
                                        System.Reflection.BindingFlags.NonPublic |
                                        System.Reflection.BindingFlags.Instance);
                                    var lvDict =
                                        m_SelectionsByLevelField?.GetValue(kvp.Value) as System.Collections.IDictionary;
                                    if (lvDict != null)
                                    {
                                        foreach (System.Collections.DictionaryEntry lvEntry in lvDict)
                                        {
                                            log($"      -> Level {lvEntry.Key}: {lvEntry.Value}");
                                            if (lvEntry.Value is System.Collections.IEnumerable list)
                                            {
                                                foreach (var item in list) log($"         => Item: {item}");
                                            }
                                        }
                                    }
                                }
                            }
                            // 2. 만약 리스트나 배열이라면 내용 출력
                            else if (val is System.Collections.IEnumerable list && !(val is string))
                            {
                                log($"  - 필드 {f.Name} (Collection) 내용:");
                                foreach (var item in list) log($"    Item: {item}");
                            }
                        }
                    }
                }
            }

            log("=== [최종 헌트] 종료 ===");
        }

        public static void DeepDiveScan(UnitEntityData unit, System.Action<string> log)
        {
            log($"=== [딥 다이브] {unit.CharacterName} 데이터 발굴 시작 ===");
            var prog = unit.Descriptor.Progression;
            var progType = prog.GetType();

            // 1. m_Progressions (진행 데이터) 전수 조사
            log("--- [1] m_Progressions (비공개 필드) 조사 ---");
            var m_ProgressionsField = progType.GetField("m_Progressions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m_ProgressionsField != null)
            {
                var dict = m_ProgressionsField.GetValue(prog) as System.Collections.IDictionary;
                if (dict != null)
                {
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        log($"Progression Key: {entry.Key}"); // 여기서 'BasicFeatsProgression' 등이 찍힙니다.
                    }
                }
            }

            // 2. m_Selections (선택 기록) 전수 조사
            log("--- [2] m_Selections (비공개 필드) 조사 ---");
            var m_SelectionsField = progType.GetField("m_Selections",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m_SelectionsField != null)
            {
                var dict = m_SelectionsField.GetValue(prog) as System.Collections.IDictionary;
                if (dict != null)
                {
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        log($"Selection Key: {entry.Key}");
                    }
                }
            }

            // 3. 스탯 모디파이어(Modifiers)의 모든 필드 강제 출력
            // (mod.Value 등이 에러 난다면 리플렉션으로 필드명을 직접 찾습니다)
            log("--- [3] 스탯 보너스(Modifiers) 객체 구조 분석 ---");
            var stats = new[] { StatType.Strength, StatType.Dexterity, StatType.Charisma };
            foreach (var st in stats)
            {
                var stat = unit.Descriptor.Stats.GetStat(st);
                if (stat == null) continue;
                foreach (var mod in stat.Modifiers)
                {
                    log($"[Stat: {st}] Mod 객체 필드 목록:");
                    var fields = mod.GetType().GetFields(System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.NonPublic |
                                                         System.Reflection.BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        try
                        {
                            log($"  - {f.Name}: {f.GetValue(mod)}");
                        }
                        catch
                        {
                        }
                    }
                }
            }

            log("=== [딥 다이브] 종료 ===");
        }


        public static void LogBasicFeatsInternal(UnitEntityData unit, System.Action<string> log)
        {
            log("=== [로그] BasicFeatsProgression 내부 데이터 발굴 시작 ===");
            var prog = unit.Descriptor.Progression;

            try
            {
                // 1. 모든 진행 데이터(m_Progressions) 딕셔너리 가져오기
                var m_ProgressionsField = prog.GetType().GetField("m_Progressions",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var m_Progressions = m_ProgressionsField?.GetValue(prog) as System.Collections.IDictionary;

                if (m_Progressions != null)
                {
                    foreach (System.Collections.DictionaryEntry de in m_Progressions)
                    {
                        var bp = de.Key as Kingmaker.Blueprints.SimpleBlueprint;
                        // 'BasicFeatsProgression' 이름을 가진 객체 집중 조사
                        if (bp != null && bp.name.Contains("BasicFeatsProgression"))
                        {
                            log($"[발견] {bp.name} 객체 내부 조사 중...");
                            var pData = de.Value; // ProgressionData

                            // 2. ProgressionData 내부의 모든 비공개 필드 목록 출력 (m_Selections 찾기)
                            var pFields = pData.GetType().GetFields(System.Reflection.BindingFlags.NonPublic |
                                                                    System.Reflection.BindingFlags.Instance);
                            foreach (var pf in pFields)
                            {
                                log($"  - 필드명: {pf.Name} ({pf.FieldType.Name})");

                                // 3. 만약 필드가 딕셔너리 형태라면 내부 키값들 출력
                                if (pf.GetValue(pData) is System.Collections.IDictionary dict)
                                {
                                    foreach (System.Collections.DictionaryEntry se in dict)
                                    {
                                        log($"    -> Key: {se.Key}");

                                        // 4. 선택 데이터(FeatureSelectionData) 내부의 레벨별 선택값 조사
                                        var selData = se.Value;
                                        var lvField = selData.GetType().GetField("m_SelectionsByLevel",
                                            System.Reflection.BindingFlags.NonPublic |
                                            System.Reflection.BindingFlags.Instance);
                                        var lvDict = lvField?.GetValue(selData) as System.Collections.IDictionary;
                                        if (lvDict != null)
                                        {
                                            foreach (System.Collections.DictionaryEntry le in lvDict)
                                            {
                                                log($"       [Level {le.Key}] 데이터 존재함");
                                                if (le.Value is System.Collections.IEnumerable list)
                                                {
                                                    foreach (var item in list) log($"         => {item}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log($"[에러] 로그 추출 중 오류: {e.Message}");
            }

            log("=== [로그] 데이터 발굴 끝 ===");
        }

        public static void DeepScan(UnitEntityData unit, System.Action<string> log)
        {
            log($"=== [딥 스캔] {unit.CharacterName} 시작 ===");
            var prog = unit.Descriptor.Progression;

            // 1. 모든 스탯의 보너스(Modifiers) 구조를 강제로 뜯어보기
            log("--- [1] 스탯 모디파이어 구조 분석 ---");
            StatType[] stats = { StatType.Strength, StatType.Dexterity, StatType.Charisma }; // 대표로 3개만
            foreach (var st in stats)
            {
                var stat = unit.Descriptor.Stats.GetStat(st);
                if (stat == null) continue;
                foreach (var mod in stat.Modifiers)
                {
                    log($"[Stat: {st}] 객체 타입: {mod.GetType().Name}");
                    // 리플렉션으로 모든 필드 강제 출력
                    var fields = mod.GetType().GetFields(System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.NonPublic |
                                                         System.Reflection.BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        try
                        {
                            log($"    Field: {f.Name} = {f.GetValue(mod)}");
                        }
                        catch
                        {
                        }
                    }
                }
            }

            // 2. 모든 진행 데이터(m_Progressions) 내부의 숨겨진 선택 기록 스캔
            log("--- [2] 모든 진행 데이터(m_Progressions) 내부 스캔 ---");
            try
            {
                var m_ProgressionsField = prog.GetType().GetField("m_Progressions",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var m_Progressions = m_ProgressionsField?.GetValue(prog) as System.Collections.IDictionary;
                if (m_Progressions != null)
                {
                    foreach (System.Collections.DictionaryEntry de in m_Progressions)
                    {
                        var bp = de.Key as Kingmaker.Blueprints.SimpleBlueprint;
                        var pData = de.Value;
                        log($"Progression: {bp?.name}");

                        // pData(ProgressionData) 내부의 모든 필드 조사
                        var pFields = pData.GetType().GetFields(System.Reflection.BindingFlags.Public |
                                                                System.Reflection.BindingFlags.NonPublic |
                                                                System.Reflection.BindingFlags.Instance);
                        foreach (var pf in pFields)
                        {
                            // 딕셔너리나 리스트 형태인 필드만 집중 조사
                            if (pf.FieldType.Name.Contains("Dictionary") || pf.FieldType.Name.Contains("List"))
                            {
                                log($"  -> Collection Field: {pf.Name}");
                                // 만약 이게 선택 기록(Selections)이라면 내용물 확인
                                if (pf.Name.Contains("Selections"))
                                {
                                    var dict = pf.GetValue(pData) as System.Collections.IDictionary;
                                    if (dict != null)
                                    {
                                        foreach (System.Collections.DictionaryEntry se in dict)
                                        {
                                            var selBp = se.Key as Kingmaker.Blueprints.SimpleBlueprint;
                                            log($"     - Selection Key: {selBp?.name}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log($"Err: {e.Message}");
            }

            log("=== [딥 스캔] 종료 ===");
        }


        public static void LogLevelPlansDetailed(UnitEntityData unit, System.Action<string> log)
        {
            log("=== [진단] m_LevelPlans 상세 분석 시작 ===");
            try
            {
                var prog = unit.Descriptor.Progression;
                var field = prog.GetType().GetField("m_LevelPlans",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var plans = field?.GetValue(prog) as System.Collections.IList;
                if (plans == null)
                {
                    log("m_LevelPlans 필드를 찾을 수 없거나 null입니다.");
                    return;
                }

                log($"m_LevelPlans 개수: {plans.Count}");

                foreach (var plan in plans)
                {
                    // Level 값 읽기
                    var levelField = plan.GetType().GetField("Level") ?? plan.GetType().GetField("m_Level",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    object levelVal = levelField?.GetValue(plan) ?? "?";

                    // Actions 리스트 읽기
                    var actionsField = plan.GetType().GetField("Actions") ?? plan.GetType().GetField("m_Actions",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var actions = actionsField?.GetValue(plan) as System.Collections.IEnumerable;

                    if (actions != null)
                    {
                        foreach (var action in actions)
                        {
                            log($"[Level {levelVal}] Action Type: {action.GetType().Name}");

                            // 해당 Action의 모든 필드 조사
                            var fields = action.GetType().GetFields(
                                System.Reflection.BindingFlags.Public |
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance);

                            foreach (var f in fields)
                            {
                                object val = "null";
                                try
                                {
                                    val = f.GetValue(action);
                                }
                                catch
                                {
                                    val = "err";
                                }

                                log($"    -> Field: {f.Name} ({f.FieldType.Name}) = {val}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log($"진단 중 에러 발생: {e.Message}");
            }

            log("=== [진단] m_LevelPlans 상세 분석 끝 ===");
        }

        public static void LogAllFeatures(UnitEntityData unit, System.Action<string> log)
        {
            log($"=== [진단] {unit.CharacterName} 모든 피처 목록 시작 ===");

            // 1. Progression.Features 조사 (UI가 참조하는 곳)
            foreach (var f in unit.Descriptor.Progression.Features)
            {
                log(
                    $"[Feature] Name: {f.Blueprint?.name}, Level: {f.SourceLevel}, Source: {f.Source?.Blueprint?.name}");
            }

            // 2. Facts.List 조사 (엔진 내부 데이터)
            foreach (var fact in unit.Descriptor.Facts.List)
            {
                if (fact is Kingmaker.UnitLogic.Feature feature)
                {
                    log($"[Fact] Name: {feature.Blueprint?.name}, Level: {feature.SourceLevel}");
                }
            }

            log($"=== [진단] 모든 피처 목록 끝 ===");
        }


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
                try
                {
                    val = f.GetValue(unit.Descriptor.Progression);
                }
                catch
                {
                    val = "(err)";
                }

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
                    Feature = N(s.FeatureGuid),
                    s.Parameter,
                    Index = s.SelectionIndex,
                }).ToList(),
                e.SkillRanks,
                e.AttributeIncrease,
                Spells = e.Spells.Select(sp => new
                {
                    Spellbook = N(sp.SpellbookGuid),
                    sp.SpellLevel,
                    Spell = N(sp.SpellGuid),
                    Index = sp.SlotIndex,
                }).ToList(),
            };

            var annotated = new
            {
                CharacterName = unit.CharacterName,
                build.SchemaVersion,
                build.Type,
                Origin = build.Origin == null
                    ? null
                    : (object)new
                    {
                        Race = N(build.Origin.RaceGuid),
                        build.Origin.Alignment,
                        build.Origin.RaceStatBonus,
                        build.Origin.BaseAbilityScores,
                    },
                LevelPath = build.LevelPath.Select(e => AnnotateEntry(e)).ToList(),
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
                foreach (var f in lvlFeatures)
                    Add(f);
            }

            foreach (var sb in unit.Descriptor.Spellbooks)
            {
                Add(sb.Blueprint);
                for (int sl = 0; sl <= 9; sl++)
                    foreach (var spell in sb.GetKnownSpells(sl))
                        if (spell?.Blueprint != null)
                            Add(spell.Blueprint);
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
            var mythicClasses = prog.ClassesOrder.Where(c => c != null && c.IsMythic).ToList();

            return new PureBuild
            {
                Type = type,
                Origin = type != CharacterType.Companion ? ExtractOrigin(unit) : null,
                LevelPath = ReconstructPath(unit, regularClasses, isMythic: false),
                MythicPath = mythicClasses.Count > 0
                    ? ReconstructPath(unit, mythicClasses, isMythic: true)
                    : null,
            };
        }

        /// <summary>
        /// 케릭터 타입 감지
        /// </summary>
        /// 
        /// <param name="unit"></param>
        /// <returns></returns>
        static CharacterType DetectType(UnitEntityData unit)
        {
            if (unit.IsCustomCompanion()) return CharacterType.Mercenary;
            if (unit.IsStoryCompanion()) return CharacterType.Companion;
            return CharacterType.Main;
        }

        static CharacterOrigin ExtractOrigin(UnitEntityData unit)
        {
            var prog = unit.Descriptor.Progression;
            var stats = unit.Descriptor.Stats;

            return new CharacterOrigin
            {
                RaceGuid = prog.Race?.AssetGuid.ToString(),
                Alignment = unit.Descriptor.Alignment.ValueRaw.ToString(),
                RaceStatBonus = GetRaceStatBonus(unit),
                BaseAbilityScores = new Dictionary<string, int>
                {
                    { "Strength", stats.Strength.BaseValue },
                    { "Dexterity", stats.Dexterity.BaseValue },
                    { "Constitution", stats.Constitution.BaseValue },
                    { "Intelligence", stats.Intelligence.BaseValue },
                    { "Wisdom", stats.Wisdom.BaseValue },
                    { "Charisma", stats.Charisma.BaseValue },
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
            var prog = unit.Descriptor.Progression;
            var entries = new List<Schema.LevelEntry>();
            var seenClasses = new HashSet<BlueprintCharacterClass>();

            // Feature facts: 파라미터 추출용 (레벨별로 매핑)
            var featuresByLevel = BuildFeaturesByLevel(unit);
            // 능력치 증가: 블루프린트 이름 패턴 *-attrib-{stat} 에서 추출
            var attrIncreases = !isMythic ? BuildAttributeIncreaseMap(unit) : null;

            for (int i = 0; i < classSequence.Count; i++)
            {
                int level = i + 1; // 1-based
                var cls = classSequence[i];

                var entry = new Schema.LevelEntry
                {
                    Level = level,
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
                            SelectionGuid = selectionGuid,
                            SelectionIndex = idx,
                            FeatureGuid = featureBp.AssetGuid.ToString(),
                            Parameter = GetFeatureParam(featureBp, selLevel, featuresByLevel),
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
                        if (spell?.Blueprint == null)
                        {
                            idx++;
                            continue;
                        }

                        // 두루마리 복사본, 임시 스펠, 신화 스펠리스트 출처 등 제외 (ToyBox SpellsEditor 방식)
                        if (spell.IsTemporary
                            || spell.CopiedFromScroll
                            || spell.IsFromMythicSpellList
                            || spell.IsMysticTheurgeCombinedSpell
                            || spell.SourceItem != null)
                        {
                            idx++;
                            continue;
                        }

                        lastEntry.Spells.Add(new SpellEntry
                        {
                            SpellbookGuid = spellbookGuid,
                            SpellListGuid = null,
                            SpellLevel = spellLevel,
                            SpellGuid = spell.Blueprint.AssetGuid.ToString(),
                            SlotIndex = idx,
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
            var prog = unit.Descriptor.Progression;

            // 1. UI가 사용하는 '특기 진행(FeatsProgression)' 블루프린트를 가져옵니다.
            var featsProg = Kingmaker.Blueprints.Root.BlueprintRoot.Instance.Progression.FeatsProgression;
            if (featsProg == null) return map;

            // 2. 4, 8, 12... 레벨 항목을 훑습니다.
            foreach (var entry in featsProg.LevelEntries)
            {
                if (entry.Level > 0 && entry.Level % 4 == 0)
                {
                    foreach (var feature in entry.Features)
                    {
                        if (feature is BlueprintFeatureSelection selection)
                        {
                            // 3. UIUtilityUnit에서 확인된 함수를 호출합니다. (오타 'Progession' 주의!)
                            // 만약 컴파일 에러가 나면 GetSelectionsForProgression (s 두개)으로 시도해 보세요.
                            var selectedFeatures = prog.GetSelectionsForProgession(selection, entry.Level, featsProg);

                            if (selectedFeatures != null)
                            {
                                foreach (var selected in selectedFeatures)
                                {
                                    if (selected == null) continue;

                                    // 4. 피처 이름에서 스탯명을 추출합니다 (예: AttributeIncreaseCharisma)
                                    foreach (var statName in AttribStatNames)
                                    {
                                        if (selected.name.IndexOf(statName, StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            map[entry.Level] = char.ToUpper(statName[0]) +
                                                               statName.Substring(1).ToLower();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
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

                var key = (feature.Blueprint.AssetGuid.ToString(), feature.SourceLevel);
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
            if (param.SpellSchool.HasValue) return param.SpellSchool.Value.ToString();
            if (param.StatType.HasValue) return param.StatType.Value.ToString();
            return null;
        }
    }
}