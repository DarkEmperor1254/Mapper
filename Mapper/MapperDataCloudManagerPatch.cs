using HarmonyLib;
using Il2CppInterop.Runtime;
using SNetwork;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// ===== MapperDataCloudManager 쪽 크래시 방지용 최소 더미 =====
// (실제 시각효과는 MapperFixPlugin.SpawnXRayEffectAt이 담당하므로,
//  여기서는 게임이 죽지 않을 최소한만 만족시킴)

[HarmonyPatch(typeof(MapperDataCloudManager), nameof(MapperDataCloudManager.DoSpawnMapperDataCloud))]
public class MapperDataCloudManagerPatch
{
    [HarmonyPrefix]
    static void Prefix(MapperDataCloudManager __instance, ref pSpawnMapperDataCloud data)
    {
        if (__instance.m_mapperCloudPrefab != null && __instance.m_mapperDataScanPointPrefab != null)
            return;

        if (MapperFixPlugin.CachedCloudTemplate != null && MapperFixPlugin.CachedScanPointTemplate != null)
        {
            __instance.m_mapperCloudPrefab = MapperFixPlugin.CachedCloudTemplate;
            __instance.m_mapperDataScanPointPrefab = MapperFixPlugin.CachedScanPointTemplate;
            return;
        }

        var cloudTemplate = BuildDummyCloud();
        UnityEngine.Object.DontDestroyOnLoad(cloudTemplate);
        MapperFixPlugin.CachedCloudTemplate = cloudTemplate;

        var scanPointTemplate = BuildDummyScanPoint();
        UnityEngine.Object.DontDestroyOnLoad(scanPointTemplate);
        MapperFixPlugin.CachedScanPointTemplate = scanPointTemplate;

        __instance.m_mapperCloudPrefab = cloudTemplate;
        __instance.m_mapperDataScanPointPrefab = scanPointTemplate;
    }

    // DoSpawnMapperDataCloud는 SNet_BroadcastAction을 통해 세션의 모든 클라이언트에서
    // 각자 호출됩니다 (발사한 사람 포함). 즉 여기서 스폰하면 이 모드를 설치한
    // 모든 사람의 화면에 같은 이펙트가 나타납니다.
    //
    // 한 번 발사하면 스텝마다 여러 번 호출되므로, 같은 플레이어의 신호가 SequenceCooldown
    // 안에 연달아 오면 "같은 발사 시퀀스가 계속되는 중"으로 보고 한 번만 스폰합니다.
    // (원격 클라이언트는 Scan()을 직접 실행하지 않아 몇 번째 스텝인지 알 수 없으므로,
    //  스텝 번호 대신 시간 간격으로 판단합니다.)
    private static readonly Dictionary<int, float> s_lastSpawnTime = new Dictionary<int, float>();

    [HarmonyPostfix]
    static void Postfix(pSpawnMapperDataCloud data)
    {
        int playerID = data.playerID;
        float now = Time.time;

        if (MapperFixSettings.VerboseLogging)
        {
            MapperFixPlugin.Log.LogInfo(
                $"[pSpawnMapperDataCloud diag] shape={data.shape} playerID={data.playerID} " +
                $"scanPos={data.scanPos} dirFwd={data.dirFwd} cloudDepth={data.cloudDepth} " +
                $"corner1={data.frustrumCorner1} corner2={data.frustrumCorner2} " +
                $"corner3={data.frustrumCorner3} corner4={data.frustrumCorner4}");
        }

        if (s_lastSpawnTime.TryGetValue(playerID, out float last) &&
            now - last < MapperFixSettings.SequenceCooldown)
        {
            s_lastSpawnTime[playerID] = now; // 시퀀스가 계속되는 중 — 타임스탬프만 갱신, 스폰은 스킵
            return;
        }

        s_lastSpawnTime[playerID] = now;

        Quaternion rot = data.dirFwd != Vector3.zero
            ? Quaternion.LookRotation(data.dirFwd)
            : Quaternion.identity;

        // EnableChargeBasedScaling이 켜져있을 때만 cloudDepth를 충전 비율로 해석함
        // (그 외의 경우 cloudDepth는 원래 게임 값 그대로이므로 건드리지 않음)
        float? chargeRatio = MapperFixSettings.EnableChargeBasedScaling
            ? (float?)Mathf.Clamp01(data.cloudDepth)
            : null;

        MapperFixPlugin.SpawnXRayEffectAt(data.scanPos, rot, chargeRatio);
    }

    private static GameObject BuildDummyCloud()
    {
        var wrapper = new GameObject("DummyMapperCloud");

        var cloudComp = wrapper.AddComponent(Il2CppType.Of<MapperDataCloud>()).Cast<MapperDataCloud>();
        var ps = wrapper.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 0f;

        // 렌더러 자체를 꺼서 화면에 아예 안 보이게 함
        var rendererObj = wrapper.GetComponent(Il2CppType.Of<ParticleSystemRenderer>());
        var rendererComp = rendererObj?.Cast<ParticleSystemRenderer>();
        if (rendererComp != null)
        {
            rendererComp.enabled = false;
        }

        cloudComp.m_particleSystem = ps;
        cloudComp.CountX = MapperFixSettings.DummyCountX;
        cloudComp.CountY = MapperFixSettings.DummyCountY;
        cloudComp.ScansPerUpdate = 4;
        cloudComp.ParticleLifeTime = MapperFixSettings.DummyParticleLifeTime;
        cloudComp.ParticleLifeTimeResource = MapperFixSettings.DummyParticleLifeTime;

        wrapper.transform.position = new Vector3(0f, -10000f, 0f);
        cloudComp.enabled = false;

        return wrapper;
    }

    private static GameObject BuildDummyScanPoint()
    {
        var wrapper = new GameObject("DummyMapperScanPoint");

        var scanPointComp = wrapper.AddComponent(Il2CppType.Of<MapperDataScanPoint>()).Cast<MapperDataScanPoint>();
        var ps = wrapper.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var emission = ps.emission;
        emission.enabled = false;
        emission.rateOverTime = 0f;

        scanPointComp.m_particleSystem = ps;

        wrapper.transform.position = new Vector3(0f, -10000f, 0f);
        scanPointComp.enabled = false;

        return wrapper;
    }

    [HarmonyPatch(typeof(MapperDataCloudManager), nameof(MapperDataCloudManager.DoSpawnMapperDataCloud))]
    public class MapperDataCloudManagerStackTracePatch
    {
        [HarmonyPrefix]
        static void Prefix(pSpawnMapperDataCloud data)
        {
            if (!MapperFixSettings.VerboseLogging) return;

            var trace = new StackTrace(1, true);
            MapperFixPlugin.Log.LogInfo(
                $"[CallStack diag] DoSpawnMapperDataCloud playerID={data.playerID}, cloudDepth={data.cloudDepth}\n{trace}");
        }
    }
}
