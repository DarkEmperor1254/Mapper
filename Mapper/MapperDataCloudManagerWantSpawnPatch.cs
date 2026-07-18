using HarmonyLib;

// WantSpawnMapperDataCloud가 pSpawnMapperDataCloud를 만들어 브로드캐스트하기 직전 지점.
// cloudDepth는 항상 고정값(3)으로 관찰되어 실질적으로 안 쓰이는 것으로 보이므로,
// 이 자리를 이용해 우리의 충전 비율(0~1)을 대신 실어 보냄.
// 이러면 새 네트워크 채널을 만들 필요 없이, 이미 정상 동작하는 기존 브로드캐스트를 그대로 활용함.
[HarmonyPatch(typeof(MapperDataCloudManager), nameof(MapperDataCloudManager.WantSpawnMapperDataCloud))]
public class MapperDataCloudManagerWantSpawnPatch
{
    [HarmonyPrefix]
    static void Prefix(int playerID, ref float cloudDepth)
    {
        if (ChargeRatioRelay.TryConsume(playerID, out float ratio))
        {
            cloudDepth = ratio;

            if (MapperFixSettings.VerboseLogging)
            {
                MapperFixPlugin.Log.LogInfo($"[ChargeRelay] Overwrote cloudDepth with ratio={ratio:F2}");
            }
        }
    }
}
