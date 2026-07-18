using Gear;
using HarmonyLib;
using SNetwork;
using UnityEngine;

// 지정된 persistentID(Gear)에서 발사된 게 아니면 Scan() 자체를 건너뛰어
// 브로드캐스트(따라서 다른 사람에게 보이는 것 포함)가 아예 발생하지 않게 함.
// 이 검사는 실제 발사자의 클라이언트에서만 의미가 있음 — 원격 클라이언트는
// Scan()을 직접 실행하지 않고 브로드캐스트만 받기 때문.
//
// 동시에, 충전량은 이 시점에 MapperTool 자신의 필드에서만 정확히 읽을 수 있으므로,
// 발사 시퀀스의 첫 스텝에서 미리 계산해 짧게 "스탬프"해둠 (MapperFixPlugin에서 소모).
[HarmonyPatch(typeof(MapperDataCloudScanner), nameof(MapperDataCloudScanner.Scan))]
public class MapperDataCloudScannerGatePatch
{
    [HarmonyPrefix]
    static bool Prefix(MapperDataCloudScanner __instance, int scanDepthStep)
    {
        bool isTarget = MapperFixPlugin.IsTargetGear(__instance);

        if (isTarget && scanDepthStep == 0)
        {
            var tool = __instance.m_parentTool;

            if (tool != null && MapperFixSettings.VerboseLogging)
            {
                MapperFixPlugin.Log.LogInfo(
                    $"[ChargeFields RAW] m_currentChargeUp={tool.m_currentChargeUp} " +
                    $"m_lastFireChargeUp={tool.m_lastFireChargeUp} " +
                    $"m_chargeUpMaxTime={tool.m_chargeUpMaxTime} " +
                    $"m_minChargeUpToFire={tool.m_minChargeUpToFire} " +
                    $"ScanStepsToMaxDis={__instance.ScanStepsToMaxDis}");
            }

            if (tool != null && MapperFixSettings.EnableChargeBasedScaling)
            {
                float ratio = Mathf.Clamp01(tool.m_lastFireChargeUp);
                int localPlayerID = tool.Owner.PlayerSlotIndex;

                ChargeRatioRelay.Stash(localPlayerID, ratio);

                if (MapperFixSettings.VerboseLogging)
                    MapperFixPlugin.Log.LogInfo($"[LocalCharge] m_lastFireChargeUp={tool.m_lastFireChargeUp} ratio={ratio:F2}");
            }
        }

        return isTarget; // false 반환 시 원본 Scan()이 통째로 스킵됨
    }
}
