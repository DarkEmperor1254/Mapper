using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Gear;
using HarmonyLib;
using Il2CppInterop.Runtime;
using SNetwork;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

[BepInPlugin("DarkEmperor.mapperfix", "Mapper Cloud Fix", "1.0.0")]
public class MapperFixPlugin : BasePlugin
{
    // ===== 크래시 방지용 더미 캐시 (MapperDataCloudManagerPatch에서 사용) =====
    public static GameObject CachedCloudTemplate;
    public static GameObject CachedScanPointTemplate;

    // ===== xrays.prefab 캐시 =====
    private static AssetBundle s_cachedBundle;
    private static GameObject s_cachedXraysPrefab;

    internal static ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("MapperFix");

    private FileSystemWatcher _configWatcher;
    private static DateTime s_lastReloadUtc = DateTime.MinValue;

    public override void Load()
    {
        MapperFixSettings.LoadOrCreate();
        SetupConfigHotReload();

        var harmony = new Harmony("DarkEmperor.mapperfix");

        try
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception ex)
        {
            Log.LogError("PatchAll threw: " + ex);
        }

        foreach (var method in harmony.GetPatchedMethods())
        {
            Log.LogInfo("Patched: " + method.DeclaringType + "." + method.Name);
        }

        Log.LogInfo($"Mapper cloud fix loaded. Settings: {MapperFixSettings.FilePath}");
    }

    // settings.jsonc 파일을 저장하면 게임 재시작 없이 즉시 반영되도록 파일 변경을 감시함.
    private void SetupConfigHotReload()
    {
        try
        {
            string dir = Path.GetDirectoryName(MapperFixSettings.FilePath);
            string file = Path.GetFileName(MapperFixSettings.FilePath);

            _configWatcher = new FileSystemWatcher(dir, file);
            _configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _configWatcher.Changed += OnConfigFileChanged;
            _configWatcher.EnableRaisingEvents = true;

            Log.LogInfo("Config hot-reload enabled — settings.jsonc 파일을 저장하면 즉시 반영됩니다.");
        }
        catch (Exception ex)
        {
            Log.LogWarning("Failed to set up config hot-reload: " + ex.Message);
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - s_lastReloadUtc).TotalMilliseconds < 500) return;
        s_lastReloadUtc = now;

        System.Threading.Thread.Sleep(150); // 파일 쓰기가 완전히 끝날 때까지 살짝 대기

        try
        {
            MapperFixSettings.Load();
            Log.LogInfo("settings.jsonc hot-reloaded from disk.");
        }
        catch (Exception ex)
        {
            Log.LogWarning("settings.jsonc reload failed: " + ex.Message);
        }
    }

    public static AssetBundle FindLoadedBundle(string nameContains)
    {
        var allObjects = Resources.FindObjectsOfTypeAll(Il2CppType.Of<AssetBundle>());
        for (int i = 0; i < allObjects.Length; i++)
        {
            var obj = allObjects[i];
            if (obj == null) continue;
            var bundle = obj.Cast<AssetBundle>();
            if (bundle != null && bundle.name.ToLower().Contains(nameContains.ToLower()))
                return bundle;
        }
        return null;
    }

    // ===== 특정 Gear(persistentID)에서 발사했을 때만 작동하도록 확인 =====
    public static bool IsTargetGear(MapperDataCloudScanner scanner)
    {
        try
        {
            var tool = scanner.m_parentTool;
            if (tool == null)
            {
                Log.LogWarning("IsTargetGear: m_parentTool is null, allowing by default.");
                return true;
            }

            var gearCategoryData = tool.GearCategoryData;
            if (gearCategoryData == null)
            {
                Log.LogWarning("IsTargetGear: GearCategoryData is null, allowing by default.");
                return true;
            }

            if (TryGetPersistentID(gearCategoryData, out uint id))
            {
                bool match = id == MapperFixSettings.TargetGearPersistentID;
                if (MapperFixSettings.VerboseLogging)
                {
                    Log.LogInfo($"[IsTargetGear] found persistentID={id}, target={MapperFixSettings.TargetGearPersistentID}, match={match}");
                }
                return match;
            }

            Log.LogWarning("IsTargetGear: couldn't read persistentID, allowing by default.");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarning("IsTargetGear check threw: " + ex);
            return true;
        }
    }

    private static bool TryGetPersistentID(object dataBlock, out uint persistentID)
    {
        persistentID = 0;
        if (dataBlock == null) return false;

        try
        {
            var type = dataBlock.GetType();

            var prop = type.GetProperty("persistentID")
                       ?? type.GetProperty("PersistentID")
                       ?? type.GetProperty("PersistentId");
            if (prop != null)
            {
                persistentID = Convert.ToUInt32(prop.GetValue(dataBlock));
                return true;
            }

            var field = type.GetField("persistentID")
                        ?? type.GetField("PersistentID");
            if (field != null)
            {
                persistentID = Convert.ToUInt32(field.GetValue(dataBlock));
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning("TryGetPersistentID reflection failed: " + ex.Message);
        }

        return false;
    }

    // ===== B 방식: 마퍼 발사 시 xrays.prefab을 직접 독립적으로 스폰 =====
    public static void SpawnXRayEffectAt(Vector3 pos, Quaternion rot, float? chargeRatio = null)
    {
        if (s_cachedXraysPrefab == null)
        {
            if (s_cachedBundle == null)
            {
                s_cachedBundle = FindLoadedBundle(MapperFixSettings.BundleNameFilter);
                if (s_cachedBundle == null)
                {
                    Log.LogWarning("Bundle not loaded, can't spawn XRay effect.");
                    return;
                }
            }

            s_cachedXraysPrefab = s_cachedBundle.LoadAsset(MapperFixSettings.XraysPrefabPath, Il2CppType.Of<GameObject>())?.Cast<GameObject>();
            if (s_cachedXraysPrefab == null)
            {
                Log.LogWarning("xrays.prefab not found in bundle.");
                return;
            }
        }

        var instance = UnityEngine.Object.Instantiate(s_cachedXraysPrefab, pos, rot);
        instance.name = "XRayEffect_Live";

        var lineRenderers = instance.GetComponentsInChildren(Il2CppType.Of<LineRenderer>());
        for (int i = 0; i < lineRenderers.Length; i++)
        {
            var lr = lineRenderers[i]?.Cast<LineRenderer>();
            if (lr != null) lr.enabled = false;
        }

        var xraysComp = instance.GetComponent(Il2CppType.Of<XRays>())?.Cast<XRays>();

        if (MapperFixSettings.EnableXRaysCustomization)
        {
            ApplyXRaysCustomization(instance);
        }

        // 충전 기반 사거리/범위 스케일링 — 발사자 본인 클라이언트에서만 정확하게 반영됨
        // (원격 클라이언트는 이 정보를 받을 방법이 없어 스탬프가 없으면 조용히 건너뜀)
        if (MapperFixSettings.EnableChargeBasedScaling && xraysComp != null && chargeRatio.HasValue)
        {
            float ratio = chargeRatio.Value;
            float range = Mathf.Lerp(MapperFixSettings.MinRangeAtNoCharge, MapperFixSettings.MaxRangeAtFullCharge, ratio);
            float square = Mathf.Lerp(MapperFixSettings.MinSquareAtNoCharge, MapperFixSettings.MaxSquareAtFullCharge, ratio);
            float fov = Mathf.Lerp(MapperFixSettings.MinFieldOfViewAtNoCharge, MapperFixSettings.MaxFieldOfViewAtFullCharge, ratio);

            TrySet(() => xraysComp.maxDistance = range, "XRays.maxDistance (charge scaling)");
            TrySet(() => xraysComp.square = square, "XRays.square (charge scaling)");
            TrySet(() => xraysComp.fieldOfView = fov, "XRays.fieldOfView (charge scaling)");

            if (MapperFixSettings.VerboseLogging)
            {
                Log.LogInfo($"[ChargeScaling] ratio={ratio:F2} range={range:F1} square={square:F2} fov={fov:F1}");
            }
        }

        float emitDuration = MapperFixSettings.EmitDuration;

        if (xraysComp != null)
        {
            UnityEngine.Object.Destroy(xraysComp, emitDuration);
        }

        UnityEngine.Object.Destroy(instance, emitDuration + MapperFixSettings.CleanupSafetyBuffer);

        if (MapperFixSettings.VerboseLogging)
        {
            Log.LogInfo($"Spawned xrays.prefab at pos={pos} rot={rot.eulerAngles}");
        }
    }

    private static void ApplyXRaysCustomization(GameObject instance)
    {
        var xraysComp = instance.GetComponent(Il2CppType.Of<XRays>())?.Cast<XRays>();
        if (xraysComp != null)
        {
            TrySet(() => xraysComp.scanMode = ParseEnum(MapperFixSettings.ScanMode, XRays.ScanMode.Random), "XRays.scanMode");
            TrySet(() => xraysComp.scanDirection = ParseEnum(MapperFixSettings.ScanDirection, XRays.ScanDirection.X), "XRays.scanDirection");
            TrySet(() => xraysComp.swipeSpeed = MapperFixSettings.SwipeSpeed, "XRays.swipeSpeed");
            TrySet(() => xraysComp.raysPerSecond = MapperFixSettings.RaysPerSecond, "XRays.raysPerSecond");
            TrySet(() => xraysComp.fieldOfView = MapperFixSettings.FieldOfView, "XRays.fieldOfView");
            TrySet(() => xraysComp.fieldOfViewFocused = MapperFixSettings.FieldOfViewFocused, "XRays.fieldOfViewFocused");
            TrySet(() => xraysComp.square = MapperFixSettings.Square, "XRays.square");
            TrySet(() => xraysComp.maxDistance = MapperFixSettings.MaxDistance, "XRays.maxDistance");
            TrySet(() => xraysComp.forwardStepSize = MapperFixSettings.ForwardStepSize, "XRays.forwardStepSize");
            TrySet(() => xraysComp.defaultColor = MapperFixSettings.ParseColor(MapperFixSettings.DefaultColor), "XRays.defaultColor");
            TrySet(() => xraysComp.defaultSize = MapperFixSettings.DefaultSize, "XRays.defaultSize");
            TrySet(() => xraysComp.enemyColor = MapperFixSettings.ParseColor(MapperFixSettings.EnemyColor), "XRays.enemyColor");
            TrySet(() => xraysComp.enemySize = MapperFixSettings.EnemySize, "XRays.enemySize");
            TrySet(() => xraysComp.interactionColor = MapperFixSettings.ParseColor(MapperFixSettings.InteractionColor), "XRays.interactionColor");
            TrySet(() => xraysComp.interactionSize = MapperFixSettings.InteractionSize, "XRays.interactionSize");
        }

        var rendererComp = instance.GetComponentInChildren(Il2CppType.Of<XRayRenderer>())?.Cast<XRayRenderer>();
        if (rendererComp != null)
        {
            TrySet(() => rendererComp.castShadows = MapperFixSettings.CastShadows, "XRayRenderer.castShadows");
            TrySet(() => rendererComp.updateBounds = MapperFixSettings.UpdateBounds, "XRayRenderer.updateBounds");
            TrySet(() => rendererComp.alignToView = MapperFixSettings.AlignToView, "XRayRenderer.alignToView");
            TrySet(() => rendererComp.alignToVelocity = MapperFixSettings.AlignToVelocity, "XRayRenderer.alignToVelocity");
            TrySet(() => rendererComp.range = MapperFixSettings.Range, "XRayRenderer.range");
            TrySet(() => rendererComp.mode = MapperFixSettings.RendererMode, "XRayRenderer.mode");
            TrySet(() => rendererComp.duration = MapperFixSettings.Duration, "XRayRenderer.duration");
            TrySet(() => rendererComp.timescale = MapperFixSettings.Timescale, "XRayRenderer.timescale");
            TrySet(() => rendererComp.playOnAwake = MapperFixSettings.PlayOnAwake, "XRayRenderer.playOnAwake");
            TrySet(() => rendererComp.loop = MapperFixSettings.Loop, "XRayRenderer.loop");
            TrySet(() => rendererComp.simulateInPlayer = MapperFixSettings.SimulateInPlayer, "XRayRenderer.simulateInPlayer");

            if (MapperFixSettings.InstanceCount >= 0)
            {
                TrySet(() => rendererComp.instanceCount = MapperFixSettings.InstanceCount, "XRayRenderer.instanceCount");
            }
        }
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct
    {
        return Enum.TryParse<T>(value, true, out var result) ? result : fallback;
    }

    private static void TrySet(Action setter, string fieldName)
    {
        try
        {
            setter();
        }
        catch (Exception ex)
        {
            if (MapperFixSettings.VerboseLogging)
            {
                Log.LogWarning($"Failed to set {fieldName}: {ex.Message}");
            }
        }
    }
}
