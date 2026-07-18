using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(MapperDataCloud), "Update")]
public class MapperDataCloudCleanupPatch
{
    private static Dictionary<int, float> s_deactivateStartTime = new Dictionary<int, float>();

    [HarmonyPostfix]
    static void Postfix(MapperDataCloud __instance)
    {
        int id = __instance.GetInstanceID();
        bool isDeactivating = __instance.m_state == MapperCloudState.Deactivating;

        if (isDeactivating)
        {
            if (!s_deactivateStartTime.ContainsKey(id))
            {
                s_deactivateStartTime[id] = Time.time;
            }
            else if (Time.time - s_deactivateStartTime[id] > MapperFixSettings.DeactivatingTimeout)
            {
                s_deactivateStartTime.Remove(id);
                UnityEngine.Object.Destroy(__instance.gameObject);
            }
        }
        else
        {
            s_deactivateStartTime.Remove(id);
        }
    }
}
