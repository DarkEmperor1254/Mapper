using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using MTFO.API;
using UnityEngine;

/// <summary>
/// BepInEx의 .cfg(toml) 대신, BepInEx/config/custom/mapperfix/settings.jsonc 파일로
/// 설정을 직접 관리합니다. 표준 JSON 파서는 주석(//)을 못 읽으므로, 우리가 만든
/// 파일 형식에 맞춘 아주 단순한 파서/라이터를 직접 구현했습니다.
/// (임의의 외부 JSON을 파싱하는 용도가 아니라, 이 플러그인이 직접 쓰고 읽는 파일 전용입니다.)
/// </summary>
public static class MapperFixSettings
{
    public static float EmitDuration = 2f;
    public static float CleanupSafetyBuffer = 5f;
    public static float DeactivatingTimeout = 3f;
    public static int DummyCountX = 4;
    public static int DummyCountY = 4;
    public static float DummyParticleLifeTime = 0.5f;
    public static string BundleNameFilter = "xrays";
    public static string XraysPrefabPath = "Assets/XRays/XRays.prefab";
    public static bool VerboseLogging = false;
    public static uint TargetGearPersistentID = 10;
    public static float SequenceCooldown = 1.5f;

    public static bool EnableXRaysCustomization = false;

    // --- 충전량 기반 사거리/범위 스케일링 (cloudDepth 기준, 모두에게 동일하게 보임) ---
    public static bool EnableChargeBasedScaling = false;
    public static float MinRangeAtNoCharge = 10f;
    public static float MaxRangeAtFullCharge = 30f;
    public static float MinSquareAtNoCharge = 0.3f;
    public static float MaxSquareAtFullCharge = 1f;
    public static float MinFieldOfViewAtNoCharge = 20f;
    public static float MaxFieldOfViewAtFullCharge = 60f;

    // --- 충전량 기반 탄약 비용 스케일링은 나중에 별도로 추가 예정 ---

    // --- XRays.cs ---
    public static string ScanMode = "Random";           // Random / SwipeLoop / SwipePingPong
    public static string ScanDirection = "X";           // X / Y
    public static float SwipeSpeed = 4f;
    public static int RaysPerSecond = 10000;
    public static float FieldOfView = 55f;
    public static float FieldOfViewFocused = 15f;
    public static float Square = 0.2f;
    public static float MaxDistance = 20f;
    public static float ForwardStepSize = 3f;
    public static string DefaultColor = "0,1,0.972549,0.16";  // "R,G,B,A"
    public static float DefaultSize = 1f;
    public static string EnemyColor = "1,0,0,0.501";
    public static float EnemySize = 1f;
    public static string InteractionColor = "1,0.490196,0,0.078";
    public static float InteractionSize = 1f;

    // --- XRayRenderer.cs ---
    public static bool CastShadows = false;
    public static bool UpdateBounds = false;
    public static bool AlignToView = true;
    public static bool AlignToVelocity = false;
    public static float Range = 50f;
    public static int RendererMode = 0;
    public static float Duration = 5f;
    public static float Timescale = 1f;
    public static bool PlayOnAwake = false;
    public static bool Loop = false;
    public static bool SimulateInPlayer = true;
    public static int InstanceCount = -1;

    public static string FilePath { get; private set; }

    /// <summary>파일이 있으면 읽어서 반영, 없으면 기본값으로 새로 생성.</summary>
    public static void LoadOrCreate()
    {
        string dir;

        if (MTFOPathAPI.HasCustomPath)
        {
            dir = Path.Combine(MTFOPathAPI.CustomPath, "mapper");
            MapperFixPlugin.Log.LogInfo("Using MTFO custom path: " + MTFOPathAPI.CustomPath);
        }
        else
        {
            // MTFO가 아직 커스텀 경로를 안 잡아준 경우(런다운 미로드 등) 안전하게 대체
            dir = Path.Combine(Paths.ConfigPath, "custom", "mapper");
            MapperFixPlugin.Log.LogWarning("MTFOPathAPI.HasCustomPath is false. Falling back to: " + dir);
        }

        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "mappersettings.jsonc");

        if (File.Exists(FilePath))
        {
            Load();
        }
        else
        {
            Save();
        }
    }

    /// <summary>디스크에서 다시 읽어와서 현재 값들을 갱신 (핫리로드용).</summary>
    public static void Load()
    {
        if (FilePath == null || !File.Exists(FilePath)) return;

        var values = ParseJsonc(File.ReadAllText(FilePath));

        EmitDuration = GetFloat(values, nameof(EmitDuration), EmitDuration);
        CleanupSafetyBuffer = GetFloat(values, nameof(CleanupSafetyBuffer), CleanupSafetyBuffer);
        DeactivatingTimeout = GetFloat(values, nameof(DeactivatingTimeout), DeactivatingTimeout);
        DummyCountX = GetInt(values, nameof(DummyCountX), DummyCountX);
        DummyCountY = GetInt(values, nameof(DummyCountY), DummyCountY);
        DummyParticleLifeTime = GetFloat(values, nameof(DummyParticleLifeTime), DummyParticleLifeTime);
        BundleNameFilter = GetString(values, nameof(BundleNameFilter), BundleNameFilter);
        XraysPrefabPath = GetString(values, nameof(XraysPrefabPath), XraysPrefabPath);
        VerboseLogging = GetBool(values, nameof(VerboseLogging), VerboseLogging);
        TargetGearPersistentID = (uint)GetInt(values, nameof(TargetGearPersistentID), (int)TargetGearPersistentID);
        SequenceCooldown = GetFloat(values, nameof(SequenceCooldown), SequenceCooldown);

        EnableXRaysCustomization = GetBool(values, nameof(EnableXRaysCustomization), EnableXRaysCustomization);

        EnableChargeBasedScaling = GetBool(values, nameof(EnableChargeBasedScaling), EnableChargeBasedScaling);
        MinRangeAtNoCharge = GetFloat(values, nameof(MinRangeAtNoCharge), MinRangeAtNoCharge);
        MaxRangeAtFullCharge = GetFloat(values, nameof(MaxRangeAtFullCharge), MaxRangeAtFullCharge);
        MinSquareAtNoCharge = GetFloat(values, nameof(MinSquareAtNoCharge), MinSquareAtNoCharge);
        MaxSquareAtFullCharge = GetFloat(values, nameof(MaxSquareAtFullCharge), MaxSquareAtFullCharge);
        MinFieldOfViewAtNoCharge = GetFloat(values, nameof(MinFieldOfViewAtNoCharge), MinFieldOfViewAtNoCharge);
        MaxFieldOfViewAtFullCharge = GetFloat(values, nameof(MaxFieldOfViewAtFullCharge), MaxFieldOfViewAtFullCharge);


        ScanMode = GetString(values, nameof(ScanMode), ScanMode);
        ScanDirection = GetString(values, nameof(ScanDirection), ScanDirection);
        SwipeSpeed = GetFloat(values, nameof(SwipeSpeed), SwipeSpeed);
        RaysPerSecond = GetInt(values, nameof(RaysPerSecond), RaysPerSecond);
        FieldOfView = GetFloat(values, nameof(FieldOfView), FieldOfView);
        FieldOfViewFocused = GetFloat(values, nameof(FieldOfViewFocused), FieldOfViewFocused);
        Square = GetFloat(values, nameof(Square), Square);
        MaxDistance = GetFloat(values, nameof(MaxDistance), MaxDistance);
        ForwardStepSize = GetFloat(values, nameof(ForwardStepSize), ForwardStepSize);
        DefaultColor = GetString(values, nameof(DefaultColor), DefaultColor);
        DefaultSize = GetFloat(values, nameof(DefaultSize), DefaultSize);
        EnemyColor = GetString(values, nameof(EnemyColor), EnemyColor);
        EnemySize = GetFloat(values, nameof(EnemySize), EnemySize);
        InteractionColor = GetString(values, nameof(InteractionColor), InteractionColor);
        InteractionSize = GetFloat(values, nameof(InteractionSize), InteractionSize);

        CastShadows = GetBool(values, nameof(CastShadows), CastShadows);
        UpdateBounds = GetBool(values, nameof(UpdateBounds), UpdateBounds);
        AlignToView = GetBool(values, nameof(AlignToView), AlignToView);
        AlignToVelocity = GetBool(values, nameof(AlignToVelocity), AlignToVelocity);
        Range = GetFloat(values, nameof(Range), Range);
        RendererMode = GetInt(values, nameof(RendererMode), RendererMode);
        Duration = GetFloat(values, nameof(Duration), Duration);
        Timescale = GetFloat(values, nameof(Timescale), Timescale);
        PlayOnAwake = GetBool(values, nameof(PlayOnAwake), PlayOnAwake);
        Loop = GetBool(values, nameof(Loop), Loop);
        SimulateInPlayer = GetBool(values, nameof(SimulateInPlayer), SimulateInPlayer);
        InstanceCount = GetInt(values, nameof(InstanceCount), InstanceCount);
    }

    /// <summary>현재 값들을 주석과 함께 settings.jsonc로 저장.</summary>
    public static void Save()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");

        AppendSectionHeader(sb, "Identity");
        AppendField(sb, "TargetGearPersistentID", (int)TargetGearPersistentID, "Works only when fired from the Gear with this GearCategoryDataBlock persistentID (default 10 = Mapper)");

        AppendSectionHeader(sb, "Advanced");
        AppendField(sb, "BundleNameFilter", BundleNameFilter, "Search keyword for the name of the AssetBundle containing xrays.prefab");
        AppendField(sb, "XraysPrefabPath", XraysPrefabPath, "The exact path to xrays.prefab in the bundle");

        AppendSectionHeader(sb, "Effect");
        AppendField(sb, "EmitDuration", EmitDuration, "Time (seconds) during which the xrays effect continues to shoot new particles when the mapper is fired");
        AppendField(sb, "CleanupSafetyBuffer", CleanupSafetyBuffer, "Additional waiting time (in seconds) after EmitDuration until the object is completely destroyed");
        AppendField(sb, "SequenceCooldown", SequenceCooldown, "If the same player signal arrives again within this time (seconds), it is considered the same firing sequence and does not respawn.");

        AppendSectionHeader(sb, "ChargeBasedScaling");
        AppendField(sb, "EnableChargeBasedScaling", EnableChargeBasedScaling, "Whether to adjust range/area proportional to charge amount (number of scan steps). Accurately reflected only on the shooter's own screen (remote players may see the default value).");
        AppendField(sb, "MinRangeAtNoCharge", MinRangeAtNoCharge, "Range (maxDistance) when charge is 0%");
        AppendField(sb, "MaxRangeAtFullCharge", MaxRangeAtFullCharge, "Range (maxDistance) when charged to 100%");
        AppendField(sb, "MinSquareAtNoCharge", MinSquareAtNoCharge, "Scan range ratio at 0% charge (square, 0~1)");
        AppendField(sb, "MaxSquareAtFullCharge", MaxSquareAtFullCharge, "Scan range ratio at 100% charge (square, 0~1)");
        AppendField(sb, "MinFieldOfViewAtNoCharge", MinFieldOfViewAtNoCharge, "Scan field of view (fieldOfView, 0~90) when charge is 0%");
        AppendField(sb, "MaxFieldOfViewAtFullCharge", MaxFieldOfViewAtFullCharge, "Scan field of view (fieldOfView, 0~90) when charge is 0%");

        AppendSectionHeader(sb, "XRaysCustomization");
        AppendField(sb, "EnableXRaysCustomization", EnableXRaysCustomization, "It must be true for the XRays/XRayRenderer settings below to be actually applied (if false, the original prefab is used).");

        AppendSectionHeader(sb, "XRays");
        AppendField(sb, "ScanMode", ScanMode, "ScanMode: Random / SwipeLoop / SwipePingPong");
        AppendField(sb, "ScanDirection", ScanDirection, "ScanDirection: X / Y");
        AppendField(sb, "SwipeSpeed", SwipeSpeed, "SwipeSpeed");
        AppendField(sb, "RaysPerSecond", RaysPerSecond, "Number of rays fired per second");
        AppendField(sb, "FieldOfView", FieldOfView, "Scan field of view (0~90)");
        AppendField(sb, "FieldOfViewFocused", FieldOfViewFocused, "Narrowing field of view when aiming (0~90)");
        AppendField(sb, "Square", Square, "Scan range aspect ratio correction (0~1)");
        AppendField(sb, "MaxDistance", MaxDistance, "Maximum scan distance");
        AppendField(sb, "ForwardStepSize", ForwardStepSize, "Forward scan step interval");
        AppendField(sb, "DefaultColor", DefaultColor, "General surface particle color \"R,G,B,A\" (0~1)");
        AppendField(sb, "DefaultSize", DefaultSize, "General surface particle size");
        AppendField(sb, "EnemyColor", EnemyColor, "Enemy indicator particle color \"R,G,B,A\"");
        AppendField(sb, "EnemySize", EnemySize, "Enemy display particle size");
        AppendField(sb, "InteractionColor", InteractionColor, "Interaction object display color \"R,G,B,A\"");
        AppendField(sb, "InteractionSize", InteractionSize, "Interaction object display size");

        AppendSectionHeader(sb, "XRayRenderer");
        AppendField(sb, "CastShadows", CastShadows, "Whether or not to have particle shadows");
        AppendField(sb, "UpdateBounds", UpdateBounds, "Whether to update rendering bounds every frame");
        AppendField(sb, "AlignToView", AlignToView, "Whether the billboard is always aligned facing the camera");
        AppendField(sb, "AlignToVelocity", AlignToVelocity, "Whether to draw by stretching in the direction of movement");
        AppendField(sb, "Range", Range, "Maximum rendering range");
        AppendField(sb, "RendererMode", RendererMode, "Internal rendering mode value (default recommended)");
        AppendField(sb, "Duration", Duration, "Total effect duration (seconds)");
        AppendField(sb, "Timescale", Timescale, "Effect playback speed scale");
        AppendField(sb, "PlayOnAwake", PlayOnAwake, "Whether to autoplay immediately after creation");
        AppendField(sb, "Loop", Loop, "Loop");
        AppendField(sb, "SimulateInPlayer", SimulateInPlayer, "Whether to simulate based on the player's perspective");
        AppendField(sb, "InstanceCount", InstanceCount, "Force number of instances (-1 means not applied)");

        AppendSectionHeader(sb, "Performance");
        AppendField(sb, "DeactivatingTimeout", DeactivatingTimeout, "Wait time (seconds) until forced cleanup when MapperDataCloud dummy is stuck in Deactivating state");
        AppendField(sb, "DummyCountX", DummyCountX, "Horizontal grid density of crash prevention dummies (lower values ​​reduce lag)");
        AppendField(sb, "DummyCountY", DummyCountY, "Vertical grid density of crash prevention dummies (lower values ​​reduce lag)");
        AppendField(sb, "DummyParticleLifeTime", DummyParticleLifeTime, "Lifespan (seconds) of crash prevention dummy particles");

        AppendSectionHeader(sb, "Debug");
        AppendField(sb, "VerboseLogging", VerboseLogging, "If true, output detailed logs related to spawn/cleanup", isLast: true);

        sb.AppendLine("}");

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllText(FilePath, sb.ToString());
    }

    private static void AppendSectionHeader(StringBuilder sb, string sectionName)
    {
        sb.AppendLine();
        sb.AppendLine($"  // ===================== [{sectionName}] =====================");
    }

    public static Color ParseColor(string value)
    {
        try
        {
            var parts = value.Split(',');
            if (parts.Length >= 3)
            {
                float r = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                float g = float.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);
                float b = float.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
                float a = parts.Length >= 4 ? float.Parse(parts[3].Trim(), CultureInfo.InvariantCulture) : 1f;
                return new Color(r, g, b, a);
            }
        }
        catch
        {
            // 아래 기본값으로 폴백
        }
        return Color.white;
    }

    // ================= 내부 JSONC 파싱/작성 헬퍼 =================

    private static void AppendField(StringBuilder sb, string key, object value, string comment, bool isLast = false)
    {
        string valueText;
        string typeName;

        if (value is bool b)
        {
            valueText = b ? "true" : "false";
            typeName = "Boolean";
        }
        else if (value is string s)
        {
            valueText = "\"" + s.Replace("\"", "\\\"") + "\"";
            typeName = "String";
        }
        else if (value is float f)
        {
            valueText = f.ToString(CultureInfo.InvariantCulture);
            typeName = "Single";
        }
        else
        {
            valueText = Convert.ToString(value, CultureInfo.InvariantCulture);
            typeName = "Int32";
        }

        sb.AppendLine($"  // {comment}");
        sb.AppendLine($"  // Setting type: {typeName}");
        sb.AppendLine($"  // Default value: {valueText.Trim('\"')}");
        sb.AppendLine($"  \"{key}\": {valueText}{(isLast ? "" : ",")}");
    }

    private static readonly Regex LinePattern = new Regex(
        "^\\s*\"(?<key>\\w+)\"\\s*:\\s*(?<value>\"(?:[^\"\\\\]|\\\\.)*\"|true|false|-?\\d+(?:\\.\\d+)?)\\s*,?\\s*$",
        RegexOptions.Compiled);

    private static Dictionary<string, string> ParseJsonc(string text)
    {
        var result = new Dictionary<string, string>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line == "{" || line == "}") continue;

            var match = LinePattern.Match(line);
            if (!match.Success) continue;

            string key = match.Groups["key"].Value;
            string value = match.Groups["value"].Value;

            if (value.StartsWith("\"") && value.EndsWith("\""))
            {
                value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"");
            }

            result[key] = value;
        }

        return result;
    }

    private static string GetString(Dictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var v) ? v : fallback;

    private static bool GetBool(Dictionary<string, string> values, string key, bool fallback)
        => values.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;

    private static int GetInt(Dictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    private static float GetFloat(Dictionary<string, string> values, string key, float fallback)
        => values.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
}
