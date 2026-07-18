using System;
using HarmonyLib;

[HarmonyPatch(typeof(MapperDataCloud), "ScanParticle")]
public class MapperDataCloudScanParticlePatch
{
    [HarmonyPrefix]
    static void Prefix(MapperDataCloud __instance, int index)
    {
        // 내부에서 죽더라도 인덱스는 무조건 앞으로 나가게 강제 (무한 반복/렉 방지)
        if (__instance.m_currentIndex <= index)
        {
            __instance.m_currentIndex = index + 1;
        }
    }

    [HarmonyFinalizer]
    static Exception Finalizer(Exception __exception)
    {
        return null; // 예외를 삼켜서 게임이 죽지 않게 함
    }
}
