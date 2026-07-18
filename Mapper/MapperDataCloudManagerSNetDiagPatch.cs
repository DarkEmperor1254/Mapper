using System;
using System.Reflection;
using HarmonyLib;

// 진짜 네트워크 전송이 SNet_BroadcastAction.Do()가 아니라 그 내부의 SNet_Packet<T>에서
// 일어날 가능성이 높아, 실제로 살아있는 객체를 리플렉션으로 뜯어봐서 확인하는 1회성 진단.
[HarmonyPatch(typeof(MapperDataCloudManager), nameof(MapperDataCloudManager.Setup))]
public class MapperDataCloudManagerSNetDiagPatch
{
    private static bool s_alreadyLogged = false;

    [HarmonyPostfix]
    static void Postfix(MapperDataCloudManager __instance)
    {
        if (s_alreadyLogged) return;
        s_alreadyLogged = true;

        try
        {
            object broadcastAction = __instance.m_spawnMapperDataCloud;
            if (broadcastAction == null)
            {
                MapperFixPlugin.Log.LogWarning("[SNetDiag] m_spawnMapperDataCloud is null at Setup time.");
                return;
            }

            var actionType = broadcastAction.GetType();
            MapperFixPlugin.Log.LogInfo("[SNetDiag] broadcastAction runtime type: " + actionType.FullName);

            // 1차 시도: m_packet을 필드로 찾기 (상속 계층 전체)
            FieldInfo packetField = null;
            var searchType = actionType;
            while (searchType != null && packetField == null)
            {
                packetField = searchType.GetField("m_packet", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                searchType = searchType.BaseType;
            }

            object packet = null;

            if (packetField != null)
            {
                packet = packetField.GetValue(broadcastAction);
                MapperFixPlugin.Log.LogInfo("[SNetDiag] found m_packet via FIELD.");
            }
            else
            {
                // 2차 시도: m_packet을 프로퍼티로 찾기 (IL2CPP interop이 private 필드를
                // 프로퍼티로 노출하는 경우가 많음)
                PropertyInfo packetProp = null;
                searchType = actionType;
                while (searchType != null && packetProp == null)
                {
                    packetProp = searchType.GetProperty("m_packet", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    searchType = searchType.BaseType;
                }

                if (packetProp != null)
                {
                    packet = packetProp.GetValue(broadcastAction);
                    MapperFixPlugin.Log.LogInfo("[SNetDiag] found m_packet via PROPERTY.");
                }
            }

            if (packet == null)
            {
                // 3차: 못 찾았으면 이 타입과 부모 타입들의 모든 필드/프로퍼티를 전부 나열
                MapperFixPlugin.Log.LogWarning("[SNetDiag] m_packet not found directly. Dumping all members instead:");

                searchType = actionType;
                while (searchType != null)
                {
                    MapperFixPlugin.Log.LogInfo($"[SNetDiag] -- members of {searchType.FullName} --");

                    foreach (var f in searchType.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        MapperFixPlugin.Log.LogInfo($"[SNetDiag] field: {f.FieldType.Name} {f.Name}");
                    }

                    foreach (var p in searchType.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        MapperFixPlugin.Log.LogInfo($"[SNetDiag] property: {p.PropertyType.Name} {p.Name}");
                    }

                    searchType = searchType.BaseType;
                }

                return;
            }

            var packetType = packet.GetType();
            MapperFixPlugin.Log.LogInfo("[SNetDiag] packet runtime type: " + packetType.FullName);

            var methods = packetType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                var paramList = m.GetParameters();
                string paramsStr = string.Join(", ", Array.ConvertAll(paramList, p => p.ParameterType.Name + " " + p.Name));
                MapperFixPlugin.Log.LogInfo($"[SNetDiag] packet method: {m.Name}({paramsStr}) -> {m.ReturnType.Name}");
            }
        }
        catch (Exception ex)
        {
            MapperFixPlugin.Log.LogError("[SNetDiag] threw: " + ex);
        }
    }
}
