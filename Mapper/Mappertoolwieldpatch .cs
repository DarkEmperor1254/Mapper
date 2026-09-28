using System.Collections.Generic;
using Gear;
using HarmonyLib;
using UnityEngine;

// ===== 발사 직후 무기를 바꿨다가 Mapper를 다시 들면 생기는 문제 대응 =====
//
// 확인된 사실(로그):
//  - 발사 시퀀스 도중 무기를 넣으면 m_inFiringSequence가 True로 남음 → 상태 정리로 해결됨.
//  - 깜빡임이 시작되기 전에 무기를 넣으면 m_flashOn / m_flashTimer는 False / 0인데도
//    Mapper의 플래시(시각 효과)가 켜진 채로 남음 → 플래그가 아니라 실제 오브젝트 상태가 남는 것.
//
// 대응: Mapper를 들었을 때(정상 상태)의 라이트/렌더러/자식 오브젝트 상태를 기준값으로 저장해 두고,
//       발사가 중단된 채로 넣어졌다면 기준값과 달라진 항목을 로그로 남기고 기준값으로 되돌림.
[HarmonyPatch(typeof(MapperTool))]
public class MapperToolWieldPatch
{
    private class Snapshot
    {
        public Dictionary<int, bool> ObjectActive = new Dictionary<int, bool>();
        public Dictionary<int, bool> RendererEnabled = new Dictionary<int, bool>();
        public Dictionary<int, bool> LightEnabled = new Dictionary<int, bool>();
        public Dictionary<int, float> LightIntensity = new Dictionary<int, float>();
    }

    // MapperTool 인스턴스별 기준값
    private static readonly Dictionary<int, Snapshot> s_baselines = new Dictionary<int, Snapshot>();

    private static string Dump(MapperTool t)
    {
        return $"isMapping={t.m_isMapping} inFiringSequence={t.m_inFiringSequence} " +
               $"locked={t.m_lockedState} aiming={t.m_isAiming} " +
               $"coolDownTimer={t.m_coolDownTimer:F2} scanStep={t.m_mapperScanDepthStep} " +
               $"flashOn={t.m_flashOn} flashTimer={t.m_flashTimer:F2} mappingVer={t.m_mappingVer} " +
               $"currentChargeUp={t.m_currentChargeUp:F2} lastFireChargeUp={t.m_lastFireChargeUp:F2}";
    }

    private static bool IsIdle(MapperTool t)
    {
        return !t.m_inFiringSequence && !t.m_isMapping && !t.m_isAiming && !t.m_flashOn;
    }

    private static Snapshot Capture(MapperTool t)
    {
        var snap = new Snapshot();
        int rootId = t.gameObject.GetInstanceID();

        foreach (var tr in t.GetComponentsInChildren<Transform>(true))
        {
            if (tr == null) continue;
            int id = tr.gameObject.GetInstanceID();
            if (id == rootId) continue; // 루트(무기 자체)는 건드리지 않음
            snap.ObjectActive[id] = tr.gameObject.activeSelf;
        }

        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            snap.RendererEnabled[r.GetInstanceID()] = r.enabled;
        }

        foreach (var l in t.GetComponentsInChildren<Light>(true))
        {
            if (l == null) continue;
            int id = l.GetInstanceID();
            snap.LightEnabled[id] = l.enabled;
            snap.LightIntensity[id] = l.intensity;
        }

        return snap;
    }

    // 기준값과 현재 상태를 비교해서 다른 항목을 로그로 남기고, restore가 true면 기준값으로 되돌림.
    private static int DiffAndRestore(MapperTool t, Snapshot baseline, bool restore)
    {
        int diffs = 0;
        int rootId = t.gameObject.GetInstanceID();

        foreach (var tr in t.GetComponentsInChildren<Transform>(true))
        {
            if (tr == null) continue;
            var go = tr.gameObject;
            int id = go.GetInstanceID();
            if (id == rootId) continue;
            if (!baseline.ObjectActive.TryGetValue(id, out bool want)) continue;
            if (go.activeSelf == want) continue;

            diffs++;
            MapperFixPlugin.Log.LogInfo($"[MapperTool] visual diff: GameObject '{go.name}' active={go.activeSelf} (baseline {want})");
            if (restore) go.SetActive(want);
        }

        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!baseline.RendererEnabled.TryGetValue(r.GetInstanceID(), out bool want)) continue;
            if (r.enabled == want) continue;

            diffs++;
            MapperFixPlugin.Log.LogInfo($"[MapperTool] visual diff: Renderer '{r.gameObject.name}' enabled={r.enabled} (baseline {want})");
            if (restore) r.enabled = want;
        }

        foreach (var l in t.GetComponentsInChildren<Light>(true))
        {
            if (l == null) continue;
            int id = l.GetInstanceID();

            if (baseline.LightEnabled.TryGetValue(id, out bool wantEnabled) && l.enabled != wantEnabled)
            {
                diffs++;
                MapperFixPlugin.Log.LogInfo($"[MapperTool] visual diff: Light '{l.gameObject.name}' enabled={l.enabled} (baseline {wantEnabled})");
                if (restore) l.enabled = wantEnabled;
            }

            if (baseline.LightIntensity.TryGetValue(id, out float wantIntensity) && !Mathf.Approximately(l.intensity, wantIntensity))
            {
                diffs++;
                MapperFixPlugin.Log.LogInfo($"[MapperTool] visual diff: Light '{l.gameObject.name}' intensity={l.intensity:F2} (baseline {wantIntensity:F2})");
                if (restore) l.intensity = wantIntensity;
            }
        }

        return diffs;
    }

    [HarmonyPatch(nameof(MapperTool.OnWield))]
    [HarmonyPrefix]
    static void OnWieldPrefix(MapperTool __instance)
    {
        if (!MapperFixSettings.VerboseLogging || __instance == null) return;
        MapperFixPlugin.Log.LogInfo($"[MapperTool] OnWield (before)   {Dump(__instance)}");
    }

    [HarmonyPatch(nameof(MapperTool.OnWield))]
    [HarmonyPostfix]
    static void OnWieldPostfix(MapperTool __instance)
    {
        if (__instance == null) return;

        try
        {
            if (MapperFixSettings.VerboseLogging)
                MapperFixPlugin.Log.LogInfo($"[MapperTool] OnWield (after)    {Dump(__instance)}");

            // 정상(대기) 상태로 들었을 때만 기준값을 갱신함. 중단된 상태를 기준으로 삼으면 안 되므로.
            if (MapperFixSettings.FixStuckAfterWeaponSwap && IsIdle(__instance))
            {
                s_baselines[__instance.GetInstanceID()] = Capture(__instance);
            }
        }
        catch (System.Exception ex)
        {
            MapperFixPlugin.Log.LogWarning("[MapperTool] baseline capture threw: " + ex);
        }
    }

    [HarmonyPatch(nameof(MapperTool.OnUnWield))]
    [HarmonyPrefix]
    static void OnUnWieldPrefix(MapperTool __instance)
    {
        if (!MapperFixSettings.VerboseLogging || __instance == null) return;
        MapperFixPlugin.Log.LogInfo($"[MapperTool] OnUnWield (before) {Dump(__instance)}");
    }

    [HarmonyPatch(nameof(MapperTool.OnUnWield))]
    [HarmonyPostfix]
    static void OnUnWieldPostfix(MapperTool __instance)
    {
        if (__instance == null) return;

        try
        {
            if (MapperFixSettings.VerboseLogging)
                MapperFixPlugin.Log.LogInfo($"[MapperTool] OnUnWield (after)  {Dump(__instance)}");

            if (!MapperFixSettings.FixStuckAfterWeaponSwap) return;

            // 무기를 집어넣은 뒤에는 발사 시퀀스가 돌고 있을 수 없음 (코루틴은 이미 죽었음).
            // 그런데도 "발사 중" 플래그가 남아있다면 시퀀스가 중간에 끊긴 것이므로 정리함.
            bool interrupted = __instance.m_inFiringSequence || __instance.m_isMapping || __instance.m_flashOn;

            // 플래그는 이미 정상인데 시각 상태만 남는 경우(깜빡임 시작 전에 교체)도 있어서,
            // 기준값이 있으면 시각 상태 차이도 확인함. 차이가 없으면 아무것도 하지 않음.
            bool hasBaseline = s_baselines.TryGetValue(__instance.GetInstanceID(), out var baseline);

            if (interrupted)
            {
                MapperFixPlugin.Log.LogInfo("[MapperTool] Firing sequence was interrupted by weapon swap — resetting stale state.");

                __instance.m_inFiringSequence = false;
                __instance.m_isMapping = false;
                __instance.m_isAiming = false;
                __instance.m_flashOn = false;
                __instance.m_flashTimer = 0f;

                if (__instance.m_lockedState)
                    __instance.SetLockedState(false);
            }

            if (hasBaseline)
            {
                int diffs = DiffAndRestore(__instance, baseline, restore: interrupted);
                if (MapperFixSettings.VerboseLogging && (interrupted || diffs > 0))
                    MapperFixPlugin.Log.LogInfo($"[MapperTool] visual state check: {diffs} difference(s), restored={interrupted}");
            }
            else if (interrupted && MapperFixSettings.VerboseLogging)
            {
                MapperFixPlugin.Log.LogInfo("[MapperTool] no visual baseline for this tool yet (it was never wielded in idle state).");
            }

            if (interrupted && MapperFixSettings.VerboseLogging)
                MapperFixPlugin.Log.LogInfo($"[MapperTool] OnUnWield (reset)  {Dump(__instance)}");
        }
        catch (System.Exception ex)
        {
            MapperFixPlugin.Log.LogWarning("[MapperTool] stale-state reset threw: " + ex);
        }
    }

    [HarmonyPatch(nameof(MapperTool.StartMapper))]
    [HarmonyPrefix]
    static void StartMapperPrefix(MapperTool __instance)
    {
        if (!MapperFixSettings.VerboseLogging || __instance == null) return;
        MapperFixPlugin.Log.LogInfo($"[MapperTool] StartMapper        {Dump(__instance)}");
    }

    [HarmonyPatch(nameof(MapperTool.StopMapper))]
    [HarmonyPrefix]
    static void StopMapperPrefix(MapperTool __instance, bool restarted)
    {
        if (!MapperFixSettings.VerboseLogging || __instance == null) return;
        MapperFixPlugin.Log.LogInfo($"[MapperTool] StopMapper(restarted={restarted}) {Dump(__instance)}");
    }
}