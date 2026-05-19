using System;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace WotrBuildSync.Core
{
    public static class CharInfoPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchLevelProgressionEntryVMCtor(harmony);
            PatchLevelProgressionEntryView(harmony);
            PatchUnitProgressionVM(harmony);
        }

        // ── UnitProgressionVM.RefreshData ───────────────────────────────────
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

        // ── LevelProgressionEntryVM 생성자 ──────────────────────────────────
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

        // ── LevelProgressionEntryView.BindViewImplementation ───────────────
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
