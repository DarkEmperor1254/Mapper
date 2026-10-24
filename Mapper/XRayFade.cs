using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

// ===== XRay 효과가 끝날 때 갑자기 사라지지 않고 서서히 사라지게 함 =====
//
// 방식:
//  - 스폰된 XRay 인스턴스의 머티리얼을 복제해서(원본 에셋을 건드리면 다른 효과에도 영향이 감)
//    소멸 직전 FadeOutDuration초 동안 머티리얼의 색/투명도 계열 속성 값을 1 → 0으로 줄임.
//  - 소멸 시각(EmitDuration + CleanupSafetyBuffer)에 페이드가 정확히 끝나도록 맞춰서
//    Destroy 시점에 이미 보이지 않게 됨 (기존 수명은 그대로).
//  - 별도 MonoBehaviour 없이 XRayRenderer.LateUpdate 후킹으로 매 프레임 갱신함.
//
// 주의: 셰이더 소스를 볼 수 없어서 어떤 속성이 투명도를 담당하는지는 이름으로 자동 탐지함.
//       VerboseLogging을 켜면 셰이더의 속성 목록과 선택된 속성이 로그에 찍히며,
//       자동 탐지가 틀리면 FadeMaterialProperty 설정으로 직접 지정할 수 있음.
public static class XRayFade
{
    private class FadeState
    {
        public Material Mat;
        public float FadeStart;
        public float FadeEnd;
        public List<string> ColorProps = new List<string>();
        public List<Color> ColorOrig = new List<Color>();
        public List<string> FloatProps = new List<string>();
        public List<float> FloatOrig = new List<float>();
        public bool Finished;
    }

    private static readonly Dictionary<int, FadeState> s_states = new Dictionary<int, FadeState>();

    private static readonly HashSet<string> ColorCandidates = new HashSet<string>
    {
        "_Color", "_TintColor", "_BaseColor", "_MainColor", "_EmissionColor"
    };

    private static readonly HashSet<string> FloatCandidates = new HashSet<string>
    {
        "_Alpha", "_Opacity", "_Intensity", "_Brightness", "_Fade"
    };

    private static bool s_loggedShader;

    // lifetime: 지금부터 인스턴스가 Destroy되기까지 남은 시간(초). 페이드는 그 끝에 맞춰 끝남.
    public static void Register(GameObject instance, float lifetime)
    {
        if (!MapperFixSettings.EnableFadeOut || instance == null) return;

        try
        {
            Purge();

            var rend = instance.GetComponentInChildren(Il2CppType.Of<XRayRenderer>())?.Cast<XRayRenderer>();
            if (rend == null || rend.material == null)
            {
                if (MapperFixSettings.VerboseLogging)
                    MapperFixPlugin.Log.LogWarning("[XRayFade] XRayRenderer or its material not found, fade skipped.");
                return;
            }

            // 공유 머티리얼을 그대로 수정하면 다른 사람의 효과/이후 효과까지 바뀌므로 복제해서 사용
            var mat = UnityEngine.Object.Instantiate(rend.material);
            rend.material = mat;

            var state = new FadeState { Mat = mat };
            float duration = Mathf.Max(0.05f, MapperFixSettings.FadeOutDuration);
            float end = Time.time + lifetime;
            state.FadeEnd = end;
            state.FadeStart = Mathf.Max(Time.time, end - duration);

            CollectProperties(mat, state);

            if (state.ColorProps.Count == 0 && state.FloatProps.Count == 0)
            {
                if (MapperFixSettings.VerboseLogging)
                    MapperFixPlugin.Log.LogWarning("[XRayFade] No fade-able material property found. Set FadeMaterialProperty manually.");
                UnityEngine.Object.Destroy(mat);
                return;
            }

            s_states[rend.GetInstanceID()] = state;
        }
        catch (Exception ex)
        {
            MapperFixPlugin.Log.LogWarning("[XRayFade] Register threw: " + ex);
        }
    }

    private static void CollectProperties(Material mat, FadeState state)
    {
        var shader = mat.shader;
        int count = shader.GetPropertyCount();

        HashSet<string> wanted = null;
        string custom = MapperFixSettings.FadeMaterialProperty;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            wanted = new HashSet<string>();
            foreach (var part in custom.Split(','))
            {
                var name = part.Trim();
                if (name.Length > 0) wanted.Add(name);
            }
        }

        bool logAll = MapperFixSettings.VerboseLogging && !s_loggedShader;
        var all = logAll ? new System.Text.StringBuilder() : null;
        bool colorPicked = false, floatPicked = false;

        for (int i = 0; i < count; i++)
        {
            string name = shader.GetPropertyName(i);
            var type = shader.GetPropertyType(i);

            bool isColor = type == ShaderPropertyType.Color;
            bool isFloat = type == ShaderPropertyType.Float || type == ShaderPropertyType.Range;

            if (logAll) all.Append($"{name}({type}) ");
            if (!isColor && !isFloat) continue;

            bool pick;
            if (wanted != null)
                pick = wanted.Contains(name);
            else if (isColor)
                pick = !colorPicked && ColorCandidates.Contains(name);
            else
                pick = !floatPicked && FloatCandidates.Contains(name);

            if (!pick) continue;

            if (isColor)
            {
                colorPicked = true;
                state.ColorProps.Add(name);
                state.ColorOrig.Add(mat.GetColor(name));
            }
            else
            {
                floatPicked = true;
                state.FloatProps.Add(name);
                state.FloatOrig.Add(mat.GetFloat(name));
            }
        }

        if (logAll)
        {
            s_loggedShader = true;
            MapperFixPlugin.Log.LogInfo($"[XRayFade] shader='{shader.name}' properties: {all}");
            MapperFixPlugin.Log.LogInfo(
                $"[XRayFade] fading color=[{string.Join(",", state.ColorProps)}] float=[{string.Join(",", state.FloatProps)}]");
        }
    }

    internal static void Tick(XRayRenderer renderer)
    {
        if (s_states.Count == 0 || renderer == null) return;
        if (!s_states.TryGetValue(renderer.GetInstanceID(), out var st) || st.Finished) return;

        float now = Time.time;
        if (now < st.FadeStart) return;

        if (st.Mat == null)
        {
            st.Finished = true;
            return;
        }

        float span = Mathf.Max(0.0001f, st.FadeEnd - st.FadeStart);
        float t = Mathf.Clamp01((now - st.FadeStart) / span);
        float k = 1f - Mathf.SmoothStep(0f, 1f, t);

        for (int i = 0; i < st.ColorProps.Count; i++)
            st.Mat.SetColor(st.ColorProps[i], st.ColorOrig[i] * k);

        for (int i = 0; i < st.FloatProps.Count; i++)
            st.Mat.SetFloat(st.FloatProps[i], st.FloatOrig[i] * k);

        if (t >= 1f) st.Finished = true;
    }

    // 이미 소멸된 인스턴스의 상태/복제 머티리얼 정리
    private static void Purge()
    {
        if (s_states.Count == 0) return;

        float now = Time.time;
        List<int> dead = null;
        foreach (var kv in s_states)
        {
            if (now > kv.Value.FadeEnd + 2f)
            {
                if (dead == null) dead = new List<int>();
                dead.Add(kv.Key);
            }
        }

        if (dead == null) return;
        foreach (int id in dead)
        {
            var mat = s_states[id].Mat;
            if (mat != null) UnityEngine.Object.Destroy(mat);
            s_states.Remove(id);
        }
    }
}

[HarmonyPatch(typeof(XRayRenderer), nameof(XRayRenderer.LateUpdate))]
public class XRayFadePatch
{
    [HarmonyPostfix]
    static void Postfix(XRayRenderer __instance)
    {
        XRayFade.Tick(__instance);
    }
}
