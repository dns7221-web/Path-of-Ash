using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 씬에 URP 2D 조명을 세운다.
///
/// 메뉴: Tools → 재의 길 → 2D 조명 세팅
///
/// 왜 필요한가:
/// 프로젝트는 URP 2D 렌더러(Assets/Settings/Renderer2D.asset)를 이미 쓰고 있는데 씬에 Light2D가
/// 하나도 없었다. 벽의 횃불도 실제 광원이 아니라 그림일 뿐이다. 그 결과 방 바닥 평균 밝기가
/// 255 중 24.7까지 내려가, 캐릭터가 배경에 오려 붙인 것처럼 떠 보였다.
///
/// <b>발밑 그림자를 넣어도 보이지 않는다.</b> 검은 바닥에 검은 그림자를 얹는 셈이라 대비가
/// 생기지 않는다. 밝기를 PNG에 직접 구워 넣는 방법도 있지만, 그러면 어두운 던전이라는 분위기가
/// 영구히 사라지고 되돌릴 수도 없다. 조명으로 푸는 편이 원래 있어야 할 자리다.
///
/// 순서가 중요하다 — 머티리얼을 먼저 바꿔야 한다:
/// 지금 모든 SpriteRenderer가 유니티 내장 Unlit 머티리얼을 쓴다. Unlit 스프라이트는 Light2D를
/// <b>완전히 무시</b>하므로, 조명부터 넣으면 아무 변화가 없어 "조명이 고장 났다"고 오해하게 된다.
///
/// 빛의 세기 설계:
/// 전역 광원을 1.0으로 두면 지금 화면과 똑같은 밝기가 기준이 된다. 거기에 플레이어를 따라다니는
/// 점광원을 더해 <b>발밑만</b> 밝힌다. 곱하기 혼합이라 두 빛이 더해져 캐릭터 주변 바닥이 대략
/// 2.5배까지 올라가고, 방 구석은 어두운 채로 남는다. 분위기를 잃지 않으면서 접지가 읽힌다.
/// </summary>
public static class AshSceneLightingBuilder
{
    private const string MaterialFolder = "Assets/Project/Art/Materials";
    private const string LitMaterialPath = MaterialFolder + "/SpriteLit.mat";
    private const string LitShaderName = "Universal Render Pipeline/2D/Sprite-Lit-Default";

    private const string GlobalLightName = "GlobalLight2D";
    private const string HeroLightName = "HeroLight";
    private const string PlayerPrefabPath = "Assets/Project/Prefabs/Player/Player.prefab";

    /// <summary>전역 광원 세기. 1.0이면 지금 화면과 같은 밝기가 기준이 된다.</summary>
    private const float GlobalIntensity = 1.0f;

    /// <summary>플레이어를 따라다니는 빛의 세기. 전역 광원 위에 더해진다.</summary>
    private const float HeroIntensity = 1.6f;

    /// <summary>플레이어 빛이 닿는 거리(유닛). 방 세로가 약 29유닛이라 9면 주변만 밝힌다.</summary>
    private const float HeroOuterRadius = 9f;

    [MenuItem("Tools/재의 길/2D 조명 세팅")]
    public static void Build()
    {
        Material lit = EnsureLitMaterial();
        if (lit == null) return;

        int sceneCount = ApplyMaterialToScene(lit);
        int prefabCount = ApplyMaterialToPrefabs(lit);
        bool addedGlobal = EnsureGlobalLight();
        bool addedHero = EnsureHeroLight();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"[2D 조명] 씬 렌더러 {sceneCount}개, 프리팹 렌더러 {prefabCount}개를 Lit 머티리얼로 바꿨다.\n" +
                  $"전역 광원 {(addedGlobal ? "생성" : "이미 있음")}, 플레이어 광원 {(addedHero ? "생성" : "이미 있음")}.\n" +
                  "씬을 저장해라. 밝기는 GlobalLight2D와 Player의 HeroLight 인스펙터에서 조절한다.");
    }

    /// <summary>
    /// Lit 스프라이트 머티리얼을 만든다.
    ///
    /// URP 패키지 안의 기본 머티리얼을 직접 참조하지 않고 프로젝트에 하나 만드는 이유:
    /// 패키지 내부 경로는 버전이 오르면 바뀔 수 있고, 우리 쪽에서 값을 조정할 수도 없다.
    /// </summary>
    private static Material EnsureLitMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(LitMaterialPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find(LitShaderName);
        if (shader == null)
        {
            Debug.LogError($"[2D 조명] 셰이더를 못 찾았다: {LitShaderName}\n" +
                           "URP 2D 렌더러가 활성화돼 있는지 확인해라.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Project/Art", "Materials");

        var material = new Material(shader) { name = "SpriteLit" };
        AssetDatabase.CreateAsset(material, LitMaterialPath);
        Debug.Log($"[2D 조명] Lit 머티리얼을 만들었다: {LitMaterialPath}");
        return material;
    }

    /// <summary>열린 씬의 모든 SpriteRenderer를 Lit 머티리얼로 바꾼다.</summary>
    private static int ApplyMaterialToScene(Material lit)
    {
        int count = 0;
        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (renderer.sharedMaterial == lit) continue;

            Undo.RecordObject(renderer, "2D 조명 세팅");
            renderer.sharedMaterial = lit;
            EditorUtility.SetDirty(renderer);
            count++;
        }
        return count;
    }

    /// <summary>
    /// 프리팹 쪽 SpriteRenderer도 바꾼다.
    ///
    /// 씬만 고치면 안 되는 이유: 보스와 유물은 런타임에 프리팹에서 새로 만들어진다.
    /// 프리팹이 Unlit이면 그렇게 태어난 오브젝트만 빛을 안 받아 혼자 떠 보인다.
    ///
    /// VFX 폴더를 건너뛰는 이유: 잿불·폭발 같은 이펙트는 <b>스스로 빛나는 것</b>이라
    /// 조명을 받으면 안 된다. Lit으로 바꾸면 어두운 구석에서 이펙트까지 같이 어두워져,
    /// 정작 터졌을 때 안 보이는 사고가 난다. 발광체는 Unlit으로 남겨두는 것이 맞다.
    /// </summary>
    private static int ApplyMaterialToPrefabs(Material lit)
    {
        int count = 0;
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Project/Prefabs" });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/VFX/"))
            {
                Debug.Log($"[2D 조명] 발광 이펙트라 Unlit으로 남긴다 — {System.IO.Path.GetFileName(path)}");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            try
            {
                bool changed = false;
                foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.sharedMaterial == lit) continue;
                    renderer.sharedMaterial = lit;
                    changed = true;
                    count++;
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return count;
    }

    /// <summary>씬 전체를 비추는 전역 광원을 세운다. 이게 없으면 Lit 스프라이트가 새까맣게 나온다.</summary>
    private static bool EnsureGlobalLight()
    {
        foreach (Light2D light in Object.FindObjectsByType<Light2D>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.lightType == Light2D.LightType.Global) return false;
        }

        var lightObject = new GameObject(GlobalLightName);
        Undo.RegisterCreatedObjectUndo(lightObject, "2D 조명 세팅");

        var global = lightObject.AddComponent<Light2D>();
        global.lightType = Light2D.LightType.Global;
        global.intensity = GlobalIntensity;
        // 아주 살짝 푸른 기를 준다. 완전한 흰빛보다 돌과 재의 차가움이 산다.
        global.color = new Color(0.86f, 0.89f, 1f);
        return true;
    }

    /// <summary>
    /// 플레이어 프리팹에 따라다니는 점광원을 붙인다.
    ///
    /// 전역 광원만 올리면 방 전체가 같이 밝아져서 던전이 밋밋해진다. 플레이어 주변만 밝히면
    /// 발밑 그림자가 읽히면서도 구석은 어두운 채로 남아, 오히려 공간감이 생긴다.
    /// </summary>
    private static bool EnsureHeroLight()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            Debug.LogWarning($"[2D 조명] 플레이어 프리팹을 못 열었다: {PlayerPrefabPath}");
            return false;
        }

        try
        {
            if (root.transform.Find(HeroLightName) != null) return false;

            var lightObject = new GameObject(HeroLightName);
            lightObject.transform.SetParent(root.transform, false);
            // 발밑이 아니라 몸통 높이에서 비춰야 바닥에 둥근 빛 웅덩이가 생긴다.
            lightObject.transform.localPosition = new Vector3(0f, 2f, 0f);

            var hero = lightObject.AddComponent<Light2D>();
            hero.lightType = Light2D.LightType.Point;
            hero.intensity = HeroIntensity;
            // 잿불 색. 이 게임의 유일한 따뜻한 색이 주황이라 거기 맞춘다.
            hero.color = new Color(1f, 0.86f, 0.66f);
            hero.pointLightInnerRadius = 1.5f;
            hero.pointLightOuterRadius = HeroOuterRadius;
            hero.falloffIntensity = 0.6f;

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
