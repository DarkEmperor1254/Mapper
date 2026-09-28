using Gear;
using HarmonyLib;
using SNetwork;
using UnityEngine;

// ===== 발사 직후 무기를 바꿨다가 Mapper를 다시 들면 먹통이 되는 문제 대응 =====
//
// 가설: 발사 시퀀스(SteppedMapping 코루틴)가 도는 도중에 무기를 바꾸면 Mapper 오브젝트가
//       비활성화되면서 코루틴이 중간에 죽고, 시퀀스 끝에서 원래 되돌려야 하던 상태값
//       (m_inFiringSequence / m_isMapping / m_isAiming / m_lockedState)이 켜진 채로 남음.
//       다시 들었을 때 Update가 그 남은 상태를 보고 아무 동작도 안 할 수 있음.
//
// 이 가설은 interop DLL(메서드 본문 없음)만으로는 확정할 수 없으므로,
//  1) VerboseLogging이 켜져 있으면 OnWield/OnUnWield/StartMapper/StopMapper 시점의
//     상태값을 전부 로그로 남기고 (먹통 재현 시 어떤 값이 남아있는지 바로 확인 가능)
//  2) FixStuckAfterWeaponSwap이 켜져 있으면, 무기를 집어넣은 직후에도 "발사 중" 상태가
//     남아있을 때에 한해서만 그 상태를 정리함. 정상 상태에서는 아무것도 건드리지 않음.
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

                MapperFixPlugin.ChargeRatioSender.SendData(ratio, default(SNet_Player), SNet_ChannelType.GameOrderCritical);
                MapperFixPlugin.ChargeRatioManager[SNet.LocalPlayer] = ratio;

                if (MapperFixSettings.VerboseLogging)
                    MapperFixPlugin.Log.LogInfo($"[OptionalNet] sent ratio={ratio:F2}");
            }

            if (tool != null && MapperFixSettings.EnableChargeBasedScaling)
            {
                try
                {
                    float ratio = Mathf.Clamp01(tool.m_lastFireChargeUp);

                    MapperFixPlugin.ChargeRatioSender.SendData(ratio, default(SNet_Player), SNet_ChannelType.GameOrderCritical);
                    MapperFixPlugin.ChargeRatioManager[SNet.LocalPlayer] = ratio;

                    if (MapperFixSettings.VerboseLogging)
                        MapperFixPlugin.Log.LogInfo($"[OptionalNet] sent ratio={ratio:F2}");
                }
                catch (System.Exception ex)
                {
                    MapperFixPlugin.Log.LogWarning("[OptionalNet] send failed, continuing scan: " + ex.Message);
                }
            }
        }

        return isTarget; // false 반환 시 원본 Scan()이 통째로 스킵됨
    }
}
