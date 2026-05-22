using System;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Kingmaker.Blueprints.Classes;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Class.LevelUp;
using Kingmaker.UnitLogic.Class.LevelUp.Actions;

namespace WotrBuildSync.Core
{
    public static class CharInfoPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchLevelProgressionEntryVMCtor(harmony);
            PatchLevelProgressionEntryView(harmony);
            PatchUnitProgressionVM(harmony);
            PatchGetNextLevelPlan(harmony);
            PatchSelectClassCheck(harmony);
            PatchSelectClassApply(harmony);
            PatchApplyClassMechanics(harmony);
        }

        // ── GetNextLevelPlan 패치 (신화 레벨업 플랜 오버라이드) ───────────────
        // FullRespec의 신화 레벨 적용 시 LevelUpPlanProviders가 비어 있어도
        // 우리가 직접 빌드한 LevelPlanData를 반환하도록 한다.
        internal static LevelPlanData GetNextLevelPlanOverride;

        static void PatchGetNextLevelPlan(Harmony harmony)
        {
            try
            {
                var method = typeof(UnitProgressionData).GetMethod(
                    "GetNextLevelPlan",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(bool) }, null);
                if (method == null)
                {
                    Main.ModEntry.Logger.Warning("[Hook] UnitProgressionData.GetNextLevelPlan 없음");
                    return;
                }
                harmony.Patch(method, prefix: new HarmonyMethod(
                    typeof(CharInfoPatches), nameof(GetNextLevelPlan_Prefix)));
                Main.ModEntry.Logger.Log("[Hook] 패치: UnitProgressionData.GetNextLevelPlan");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[Hook] GetNextLevelPlan 패치 실패: {ex.Message}");
            }
        }

        static bool GetNextLevelPlan_Prefix(bool mythic, ref LevelPlanData __result)
        {
            if (mythic && GetNextLevelPlanOverride != null)
            {
                var stack = new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: false);
                var frames = stack.GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>();
                var caller = string.Join(" → ", frames.Take(6)
                    .Select(f => f.GetMethod())
                    .Where(m => m != null)
                    .Select(m => $"{m.DeclaringType?.Name}.{m.Name}"));
                Main.ModEntry.Logger.Log($"[Hook] GetNextLevelPlan override 주입 — actions={GetNextLevelPlanOverride.Actions?.Length} | {caller}");
                __result = GetNextLevelPlanOverride;
                return false;
            }
            return true;
        }

        // ── SelectClass.Check 패치 (클래스 선택 제한 우회) ──────────────────
        // SelectClass.Check(state)가 false를 반환하면 Execute가 조용히 리턴.
        // BypassMythicClassCheck 동안에만 강제로 true 반환.
        internal static bool BypassMythicClassCheck = false;

        // ILevelUpAction.Check(LevelUpState, UnitDescriptor) 패치
        static void PatchSelectClassCheck(Harmony harmony)
        {
            try
            {
                // UnitDescriptor 타입을 컴파일 타임에 참조할 수 없으므로 런타임에 파라미터 수/타입으로 탐색
                var method = typeof(SelectClass).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        (m.Name == "Check" || m.Name.EndsWith(".Check")) &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType == typeof(LevelUpState));
                if (method == null)
                {
                    Main.ModEntry.Logger.Warning("[Hook] SelectClass.Check(LevelUpState, UnitDescriptor) 없음");
                    return;
                }
                harmony.Patch(method, prefix: new HarmonyMethod(
                    typeof(CharInfoPatches), nameof(SelectClassCheck_Prefix)));
                Main.ModEntry.Logger.Log("[Hook] 패치: SelectClass.Check");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[Hook] SelectClass.Check 패치 실패: {ex.Message}");
            }
        }

        static bool SelectClassCheck_Prefix(ref bool __result)
        {
            if (BypassMythicClassCheck)
            {
                __result = true;
                return false;
            }
            return true;
        }

        // ── SelectClass.Apply 진단 패치 (BypassMythicClassCheck 활성 시에만 로그 출력) ──
        // 신화 레벨업 시 SelectClass.Apply가 실제로 호출되는지, SelectedClass가 올바른지 확인용.
        // 진단이 완료되면 PatchSelectClassApply 호출 제거 가능.
        static void PatchSelectClassApply(Harmony harmony)
        {
            try
            {
                var method = typeof(SelectClass).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        (m.Name == "Apply" || m.Name.EndsWith(".Apply")) &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType == typeof(LevelUpState));
                if (method == null)
                {
                    Main.ModEntry.Logger.Warning("[Hook] SelectClass.Apply(LevelUpState, UnitDescriptor) 없음");
                    return;
                }
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(CharInfoPatches), nameof(SelectClassApply_Prefix)),
                    postfix: new HarmonyMethod(typeof(CharInfoPatches), nameof(SelectClassApply_Postfix)));
                Main.ModEntry.Logger.Log("[Hook] 패치: SelectClass.Apply");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[Hook] SelectClass.Apply 패치 실패: {ex.Message}");
            }
        }

        static void SelectClassApply_Prefix(object __instance, LevelUpState state)
        {
            if (!BypassMythicClassCheck) return;
            try
            {
                var classBp = __instance.GetType()
                    .GetProperty("CharacterClass", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(__instance);
                Main.ModEntry.Logger.Log(
                    $"[Diag] SelectClass.Apply 진입: class={classBp} mode={state.Mode} " +
                    $"IsClassSelected={state.IsClassSelected} IsMythicClassSelected={state.IsMythicClassSelected}");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Log($"[Diag] SelectClass.Apply prefix err: {ex.Message}"); }
        }

        static void SelectClassApply_Postfix(LevelUpState state, object unit)
        {
            if (!BypassMythicClassCheck) return;
            try
            {
                var prog = GetField(unit, "Progression");
                var mythicLv = GetField(prog, "MythicLevel");
                var charLv   = GetField(prog, "CharacterLevel");
                Main.ModEntry.Logger.Log(
                    $"[Diag] SelectClass.Apply 완료: IsClassSelected={state.IsClassSelected} " +
                    $"IsMythicClassSelected={state.IsMythicClassSelected} SelectedClass={state.SelectedClass?.name} " +
                    $"MythicLv={mythicLv} CharLv={charLv}");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Log($"[Diag] SelectClass.Apply postfix err: {ex.Message}"); }
        }

        // ── ApplyClassMechanics.Apply(LevelUpState, UnitDescriptor) 진단 패치 ─
        static void PatchApplyClassMechanics(Harmony harmony)
        {
            try
            {
                var method = typeof(ApplyClassMechanics).GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m =>
                        m.Name == "Apply" &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType == typeof(LevelUpState));
                if (method == null)
                {
                    Main.ModEntry.Logger.Warning("[Hook] ApplyClassMechanics.Apply(LevelUpState, UnitDescriptor) 없음");
                    return;
                }
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(CharInfoPatches), nameof(ApplyClassMechanics_Prefix)),
                    postfix: new HarmonyMethod(typeof(CharInfoPatches), nameof(ApplyClassMechanics_Postfix)));
                Main.ModEntry.Logger.Log("[Hook] 패치: ApplyClassMechanics.Apply");
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[Hook] ApplyClassMechanics.Apply 패치 실패: {ex.Message}");
            }
        }

        static void ApplyClassMechanics_Prefix(LevelUpState state, object unit)
        {
            if (!BypassMythicClassCheck) return;
            try
            {
                var prog     = GetField(unit, "Progression");
                var mythicLv = GetField(prog, "MythicLevel");
                var charLv   = GetField(prog, "CharacterLevel");
                var sb = new StringBuilder("[Diag] ApplyClassMechanics.Apply 진입:");
                sb.AppendLine($"  SelectedClass={state.SelectedClass?.name}");
                sb.AppendLine($"  NextObligatoryMythicClass={state.NextObligatoryMythicClass?.name}");
                sb.AppendLine($"  IsClassSelected={state.IsClassSelected}");
                sb.AppendLine($"  IsMythicClassSelected={state.IsMythicClassSelected}");
                sb.AppendLine($"  Mode={state.Mode}");
                sb.AppendLine($"  NextClassLevel={state.NextClassLevel}");
                if (prog != null)
                    sb.AppendLine($"  Unit MythicLv={mythicLv} CharLv={charLv}");
                Main.ModEntry.Logger.Log(sb.ToString());
            }
            catch (Exception ex) { Main.ModEntry.Logger.Log($"[Diag] ACM prefix err: {ex.Message}"); }
        }

        static void ApplyClassMechanics_Postfix(object unit)
        {
            if (!BypassMythicClassCheck) return;
            try
            {
                var prog     = GetField(unit, "Progression");
                var mythicLv = GetField(prog, "MythicLevel");
                var charLv   = GetField(prog, "CharacterLevel");
                Main.ModEntry.Logger.Log(prog != null
                    ? $"[Diag] ApplyClassMechanics.Apply 종료: MythicLv={mythicLv} CharLv={charLv}"
                    : "[Diag] ApplyClassMechanics.Apply 종료: unit=null");
            }
            catch (Exception ex) { Main.ModEntry.Logger.Log($"[Diag] ACM postfix err: {ex.Message}"); }
        }

        // ── UnitProgressionVM.RefreshData 진단 패치 ─────────────────────────
        // 캐릭터 창에서 클래스/피처 진행표가 갱신될 때마다 방대한 필드 덤프를 출력.
        // 신화 선택이 UI에 반영되는지 확인하는 용도 — 조사 완료 후 제거 가능.
        static void PatchUnitProgressionVM(Harmony harmony)
        {
            var vmType = FindType("UnitProgressionVM");
            if (vmType == null)
            {
                Main.ModEntry.Logger.Warning("[Hook] UnitProgressionVM 타입 없음");
                return;
            }

            var method = AccessTools.Method(vmType, "RefreshData");
            if (method == null)
            {
                Main.ModEntry.Logger.Warning("[Hook] UnitProgressionVM.RefreshData 없음");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(
                typeof(CharInfoPatches), nameof(UnitProgressionVM_RefreshData)));
            Main.ModEntry.Logger.Log("[Hook] 패치: UnitProgressionVM.RefreshData");
        }

        static void UnitProgressionVM_RefreshData(object __instance)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[UnitProgressionVM] RefreshData ──────────────");

                // ClassProgressionVms
                var classVmsField = __instance.GetType().GetField("ClassProgressionVms",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var classVms = classVmsField?.GetValue(__instance) as System.Collections.IEnumerable;
                bool dumpedProgVmFields = false;

                if (classVms != null)
                {
                    foreach (var classVm in classVms)
                    {
                        var className  = GetField(classVm, "Name");
                        var classLevel = GetField(classVm, "Level");
                        sb.AppendLine($"\n  [Class] {className}  Level={classLevel}");

                        var progVms = GetField(classVm, "ProgressionVms") as System.Collections.IEnumerable;
                        if (progVms == null) continue;

                        foreach (var progVm in progVms)
                        {
                            var bpProg = GetField(progVm, "BlueprintProgression");
                            sb.AppendLine($"    [Progression] {bpProg}");

                            // 첫 번째 progVm의 필드 목록 덤프 (필드명 파악용)
                            if (!dumpedProgVmFields)
                            {
                                dumpedProgVmFields = true;
                                sb.AppendLine($"    [ProgVM fields of {progVm.GetType().Name}]:");
                                var allFields = progVm.GetType().GetFields(
                                    BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                                foreach (var f in allFields)
                                {
                                    object val = null;
                                    try { val = f.GetValue(progVm); } catch { val = "(err)"; }
                                    sb.AppendLine($"      {f.FieldType.Name} {f.Name} = {val}");
                                }
                            }

                            // 이름 패턴으로 컬렉션 필드 탐색
                            var allF = progVm.GetType().GetFields(
                                BindingFlags.Public | BindingFlags.NonPublic |
                                BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                            foreach (var f in allF)
                            {
                                if (!(f.GetValue(progVm) is System.Collections.IEnumerable list)) continue;
                                if (f.FieldType == typeof(string)) continue;

                                bool printed = false;
                                foreach (var item in list)
                                {
                                    if (item == null) continue;
                                    var level   = GetField(item, "Level");
                                    var feature = GetField(item, "Feature");
                                    if (level == null && feature == null) break;
                                    if (!printed) { sb.AppendLine($"      [{f.Name}]:"); printed = true; }

                                    var name     = GetField(item, "Name");
                                    var isSel    = GetField(item, "IsSelection");
                                    // Feature는 BlueprintFeatureBase 직접 참조 — Unity name 필드는 소문자
                                    var featName = feature == null ? "null"
                                        : GetField(feature, "name")?.ToString() ?? "?";
                                    sb.AppendLine($"        lv={level}  isSel={isSel}  feat={featName}  ({name})");
                                }
                            }
                        }
                    }
                }

                // FeatProgressionVM.m_FeatureEntries — FeatureEntry + UIFeature 구조 덤프
                var featVmField = __instance.GetType().GetField("FeatProgressionVM",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var featVm = featVmField?.GetValue(__instance);
                if (featVm != null)
                {
                    sb.AppendLine("\n  [FeatProgressionVM] m_FeatureEntries:");

                    // ProgressionSourceFeatures backing field: Dictionary<FeatureEntry, UIFeature>
                    var srcFeatField = featVm.GetType().GetField("<ProgressionSourceFeatures>k__BackingField",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    var srcFeatDict = srcFeatField?.GetValue(featVm) as System.Collections.IDictionary;

                    var entries = GetField(featVm, "m_FeatureEntries") as System.Collections.IEnumerable;
                    bool dumpedEntryFields = false;

                    if (entries != null)
                    {
                        foreach (var entry in entries)
                        {
                            if (entry == null) continue;
                            var lvl = GetField(entry, "Level");

                            // 첫 번째 entry의 전체 필드 덤프
                            if (!dumpedEntryFields)
                            {
                                dumpedEntryFields = true;
                                sb.AppendLine("    [First FeatureEntry fields]:");
                                foreach (var f in entry.GetType().GetFields(
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    object v = null;
                                    try { v = f.GetValue(entry); } catch { v = "(err)"; }
                                    sb.AppendLine($"      {f.FieldType.Name} {f.Name} = {v}");
                                }

                                // UIFeature 구조 덤프
                                var uiF0 = srcFeatDict?[entry];
                                if (uiF0 != null)
                                {
                                    sb.AppendLine("    [First UIFeature fields]:");
                                    foreach (var f in uiF0.GetType().GetFields(
                                        BindingFlags.Public | BindingFlags.NonPublic |
                                        BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                                    {
                                        object v = null;
                                        try { v = f.GetValue(uiF0); } catch { v = "(err)"; }
                                        sb.AppendLine($"      {f.FieldType.Name} {f.Name} = {v}");
                                    }
                                }
                            }

                            // FeatureEntry.Feature는 BlueprintFeatureBase 직접 참조
                            var bp2 = GetField(entry, "Feature");
                            string nm = bp2 == null ? "?" : GetField(bp2, "name")?.ToString() ?? "?";

                            sb.AppendLine($"    lv={lvl}  feat={nm}");
                        }
                    }
                }

                Main.ModEntry.Logger.Log(sb.ToString());
            }
            catch (Exception ex)
            {
                Main.ModEntry.Logger.Error($"[Hook] UnitProgressionVM dump 오류: {ex.Message}");
            }
        }

        static object GetField(object obj, string fieldName)
        {
            if (obj == null) return null;
            var f = obj.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            try { return f?.GetValue(obj); } catch { return null; }
        }

        // ── LevelProgressionEntryVM 생성자 진단 패치 ────────────────────────
        // 레벨 진행표 VM이 생성될 때마다 index/currentLevel을 로그에 출력.
        // 신화 레벨 적용 후 VM이 올바른 레벨을 읽는지 확인용 — 조사 완료 후 제거 가능.
        static void PatchLevelProgressionEntryVMCtor(Harmony harmony)
        {
            var vmType = FindType("LevelProgressionEntryVM");
            if (vmType == null)
            {
                Main.ModEntry.Logger.Warning("[Hook] LevelProgressionEntryVM 타입 없음");
                return;
            }

            // int index, IReadOnlyReactiveProperty<int> currentLevel
            var ctor = vmType.GetConstructors().FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length == 2 && p[0].ParameterType == typeof(int);
            });

            if (ctor == null)
            {
                Main.ModEntry.Logger.Warning("[Hook] LevelProgressionEntryVM 생성자 없음");
                LogConstructors(vmType);
                return;
            }

            harmony.Patch(ctor, postfix: new HarmonyMethod(
                typeof(CharInfoPatches), nameof(LevelProgressionVM_Ctor_Postfix)));
            Main.ModEntry.Logger.Log("[Hook] 패치: LevelProgressionEntryVM.ctor(int, IReadOnlyReactiveProperty<int>)");
        }

        static void LevelProgressionVM_Ctor_Postfix(object __instance, int index, object currentLevel)
        {
            var levelValue = currentLevel?.GetType()
                .GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(currentLevel);

            Main.ModEntry.Logger.Log(
                $"[LevelProgressionEntryVM] ctor  index={index}  currentLevel.Value={levelValue}");

            // 부모 탐색: index==1일 때만 콜 스택 출력
            if (index == 1)
            {
                var stack = new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: false);
                var sb = new StringBuilder();
                sb.AppendLine("[LevelProgressionEntryVM] ── call stack (index=1) ──");
                foreach (System.Diagnostics.StackFrame frame in stack.GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>())
                {
                    var m = frame.GetMethod();
                    if (m == null) continue;
                    sb.AppendLine($"  {m.DeclaringType?.FullName}.{m.Name}");
                }
                Main.ModEntry.Logger.Log(sb.ToString());
            }
        }

        // ── LevelProgressionEntryView.BindViewImplementation 진단 패치 ──────
        // UI View가 ViewModel에 바인딩될 때 Index/IsAvailable/IsSelected 등을 로그에 출력.
        // 신화 선택 UI 불반영 원인 분석용 — 조사 완료 후 제거 가능.
        static void PatchLevelProgressionEntryView(Harmony harmony)
        {
            var viewType = FindType("LevelProgressionEntryView");
            if (viewType == null)
            {
                Main.ModEntry.Logger.Warning("[Hook] LevelProgressionEntryView 타입 없음");
                return;
            }

            var method = AccessTools.Method(viewType, "BindViewImplementation");
            if (method == null)
            {
                Main.ModEntry.Logger.Warning("[Hook] LevelProgressionEntryView.BindViewImplementation 없음");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(
                typeof(CharInfoPatches), nameof(LevelProgressionView_Bind_Postfix)));
            Main.ModEntry.Logger.Log("[Hook] 패치: LevelProgressionEntryView.BindViewImplementation");
        }

        static void LevelProgressionView_Bind_Postfix(object __instance)
        {
            var vmProp = __instance.GetType().GetProperty("ViewModel",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            var vm = vmProp?.GetValue(__instance);
            if (vm == null || vm.GetType().Name != "LevelProgressionEntryVM") return;

            // index, IsAvailable, IsSelected 등 핵심 필드만 출력
            var sb = new StringBuilder();
            sb.AppendLine("[LevelProgressionEntryView] Bind — VM snapshot:");
            foreach (var name in new[] { "Index", "CurrentLevel", "IsAvailable", "IsSelected", "ClassData", "Archetypes" })
            {
                var prop = vm.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var field = vm.GetType().GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                object val = null;
                try { val = prop?.GetValue(vm) ?? field?.GetValue(vm); }
                catch { val = "(error)"; }
                if (prop != null || field != null)
                    sb.AppendLine($"  {name} = {val}");
            }

            Main.ModEntry.Logger.Log(sb.ToString());
        }

        // ── 유틸 ───────────────────────────────────────────────────────────
        static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                        if (t.Name == name) return t;
                }
                catch { }
            }
            return null;
        }

        static void LogConstructors(Type t)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Hook] {t.Name} 생성자 목록:");
            foreach (var c in t.GetConstructors())
                sb.AppendLine($"  ({string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
            Main.ModEntry.Logger.Log(sb.ToString());
        }
    }
}
