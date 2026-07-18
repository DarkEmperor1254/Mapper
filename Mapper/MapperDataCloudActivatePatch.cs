using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(MapperDataCloud), nameof(MapperDataCloud.Activate))]
public class MapperDataCloudActivatePatch
{
    [HarmonyPrefix]
    static void Prefix(MapperDataCloud __instance)
    {
        __instance.enabled = true;

        // 좌표계 문제 방지용 원점 고정 (시각효과는 안 쓰지만 내부 계산 안전을 위해 유지)
        __instance.transform.position = Vector3.zero;
        __instance.transform.rotation = Quaternion.identity;
        __instance.transform.localScale = Vector3.one;

        // Activate()가 CountX/CountY를 원본 값(60x60)으로 덮어쓰므로, 설정값으로 다시 재초기화
        __instance.CountX = MapperFixSettings.DummyCountX;
        __instance.CountY = MapperFixSettings.DummyCountY;

        var initMethod = AccessTools.Method(typeof(MapperDataCloud), "InitParticles");
        initMethod.Invoke(__instance, null);
    }

    [HarmonyPostfix]
    static void Postfix(MapperDataCloud __instance)
    {
        if (__instance.m_mapVisibilityTrans == null)
        {
            var cam = Camera.main;
            if (cam != null) __instance.m_mapVisibilityTrans = cam.transform;
        }
        if (__instance.m_maskMapperDataObject.value == 0)
        {
            __instance.m_maskMapperDataObject = ~0;
        }
    }
}
