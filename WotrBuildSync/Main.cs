using System;
using System.IO;
using HarmonyLib;
using Kingmaker;
using UnityEngine;
using UnityModManagerNet;
using WotrBuildSync.Core;

namespace WotrBuildSync
{
    public class Main
    {
        internal static UnityModManager.ModEntry ModEntry;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;
            modEntry.OnUpdate = OnUpdate;

            var harmony = new Harmony("com.wotrbuildsync");
            CharInfoPatches.Apply(harmony);

            return true;
        }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            // Ctrl+Shift+E → 메인 캐릭터 Export
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.E))
            {
                TryExport();
            }
        }

        static void TryExport()
        {
            try
            {
                var player = Game.Instance?.Player;
                var unit = player?.MainCharacter.Value;
                if (unit == null)
                {
                    ModEntry.Logger.Warning("MainCharacter is null — 세이브 로드 후 시도하세요.");
                    return;
                }

                var logLines = new System.Collections.Generic.List<string>();
                GameExporter.LogDiagnostics(unit, logLines.Add);
                GameExporter.LogAllFeatures(unit, logLines.Add);

                var logPath = Path.Combine(ModEntry.Path, "diagnostics.log");
                File.WriteAllLines(logPath, logLines);
                ModEntry.Logger.Log($"진단 로그 저장: {logPath}");

                var json = GameExporter.Export(unit);
                var path = Path.Combine(ModEntry.Path, "export.json");
                File.WriteAllText(path, json);
                ModEntry.Logger.Log($"Export 완료: {path}");

                var annotated = GameExporter.ExportAnnotated(unit);
                var annotatedPath = Path.Combine(ModEntry.Path, "export_readable.json");
                File.WriteAllText(annotatedPath, annotated);
                ModEntry.Logger.Log($"Readable export 완료: {annotatedPath}");
            }
            catch (Exception ex)
            {
                ModEntry.Logger.Error($"Export 실패: {ex}");
            }
        }
    }
}
