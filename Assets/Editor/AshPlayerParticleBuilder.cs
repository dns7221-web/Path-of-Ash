using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// 추가 생성(2026-09-17) — 플레이어 스킬의 파티클 곁들임을 만들고 연결한다.
/// 메뉴: Tools → 재의 길 → 파티클 → 플레이어 파티클 만들기
///
/// 기획 페이지(09-16)에서 사용자가 고른 14개를 그대로 옮긴다.
/// 공격-1·2, 대시-1, W-1·3, Q-1·2·3, E-1·2·3, R-1·2·3. 양은 "화려하게"라 개수에 <see cref="Amount"/>를 곱한다.
///
/// <b>만드는 방식.</b> 대부분은 이미 있는 이펙트 프리팹의 <b>자식</b>으로 넣는다. 이펙트가 생기는 자리·방향·시각을
/// 스킬 코드가 이미 정하고 있어서, 자식으로 넣으면 그 결정을 그대로 따라간다 — 스킬 코드를 거의 안 고친다.
/// 이펙트가 끝나면 <see cref="ParticleGarnish"/>가 자식을 월드로 떼어 내 여운을 남긴다.
/// 이펙트가 따로 없는 두 가지(명중 불똥, R 모으기)만 독립 프리팹으로 만든다.
///
/// <b>이미 있으면 건드리지 않는다.</b> 자식 이름으로 찾아서 없을 때만 만든다. 인스펙터에서 고친 값이
/// 메뉴를 다시 돌려도 남는다. 처음 값으로 되돌리고 싶으면 그 자식을 지우고 다시 돌린다.
///
/// <b>공통 규칙</b>(기획 페이지와 같다):
/// <list type="bullet">
/// <item>재질은 텍스처 없는 Sprites-Default — 각진 네모가 곧 재 조각이다(보스 전환 파티클과 같은 결정).</item>
/// <item>월드 공간 시뮬레이션 — 이펙트가 사라지거나 몸이 움직여도 뿌려진 불티는 그 자리에 남는다.</item>
/// <item>Scaling Mode는 Local — 부모 이펙트의 배율(0.65~4.5)이 불티 크기에 안 먹는다. 아래 크기는 전부 유닛이다.</item>
/// <item>게임 시간으로 돈다 — 히트스톱(timeScale 0) 동안 같이 멈춘다.</item>
/// <item>VFX 층에 그리고, 바닥에 남는 것만 Decal 층(캐릭터 아래)에 그린다.</item>
/// </list>
/// </summary>
public static class AshPlayerParticleBuilder
{
    private const string VfxFolder = "Assets/Project/Prefabs/VFX";
    private const string ParticleFolder = VfxFolder + "/Particles";

    private const string HitSparksPath = ParticleFolder + "/HitSparks.prefab";
    private const string UltimateGatherPath = ParticleFolder + "/UltimateGather.prefab";

    private const string SlashPath = VfxFolder + "/EmberSlash.prefab";
    private const string ArrowPath = VfxFolder + "/EmberArrow.prefab";
    private const string ArrowImpactPath = VfxFolder + "/EmberArrowImpact.prefab";
    private const string SlamImpactPath = VfxFolder + "/SlamImpact.prefab";
    private const string SlamBurstPath = VfxFolder + "/SlamBurst.prefab";
    private const string PillarPath = VfxFolder + "/AshPillar.prefab";
    private const string CrownPath = VfxFolder + "/KingsEmber.prefab";

    private const string PlayerPrefabPath = "Assets/Project/Prefabs/Player/Player.prefab";
    private const string StaffSkillPath = "Assets/Project/Data/Skills/Skill_E_AshPillar.asset";
    private const string UltimateSkillPath = "Assets/Project/Data/Skills/Skill_R_KingsEmber.asset";

    private const string DashTrailName = "DashTrail";
    private const string AttackHitboxName = "AttackHitbox";

    /// <summary>
    /// 양 — 기획 페이지의 개수·방출량에 곱한다. 사용자가 "화려하게"를 골랐다(페이지 표기 ×1.5).
    /// 은은하게는 0.6, 그림대로는 1이다. 바꾸면 <b>새로 만드는 자식에만</b> 반영된다(있는 것은 안 건드린다).
    /// </summary>
    private const float Amount = 1.5f;

    /// <summary>
    /// R 판정 경계 고리에서 불티가 날아가는 구간(수명 비율). <see cref="AreaRingParticles"/>의 같은 이름 값과 맞춘다.
    /// </summary>
    private const float RingTravelFraction = 0.35f;

    /// <summary>R 고리 불티가 태어나는 반지름. <see cref="AreaRingParticles"/>의 startRadius와 맞춘다.</summary>
    private const float RingStartRadius = 1.5f;

    /// <summary>R 고리 불티의 수명(상수). 불티마다 같아야 같은 반지름에 멈춘다.</summary>
    private const float RingLifetime = 1.15f;

    // 게임 VFX 팔레트(기획 페이지의 색 띠와 같다). 불티 하나가 수명 동안 흰 심 → 황금 → 주황 → 적갈 → 재로 식는다.
    internal static readonly Color HotWhite = new Color(1f, 1f, 0.94f);
    internal static readonly Color Core = new Color(1f, 0.949f, 0.753f);      // #FFF2C0
    internal static readonly Color Amber = new Color(1f, 0.690f, 0f);         // #FFB000
    internal static readonly Color Ember = new Color(0.941f, 0.376f, 0f);     // #F06000
    internal static readonly Color Deep = new Color(0.541f, 0.165f, 0.063f);  // #8A2A10
    internal static readonly Color Ash = new Color(0.290f, 0.275f, 0.282f);   // #4A4648
    internal static readonly Color AshLight = new Color(0.541f, 0.510f, 0.502f);
    internal static readonly Color Stone = new Color(0.361f, 0.314f, 0.306f);
    internal static readonly Color StoneDark = new Color(0.235f, 0.208f, 0.212f);
    internal static readonly Color Coal = new Color(0.133f, 0.118f, 0.122f);

    /// <summary>
    /// 이펙트 프리팹 하나에 붙일 곁들임 하나.
    /// WorldOffset은 이펙트 피벗에서의 거리(유닛)다. 부모 배율로 나눠 localPosition에 넣는다.
    /// </summary>
    private sealed class GarnishSpec
    {
        public string PrefabPath;
        public string ChildName;
        public string Label;
        public Vector2 WorldOffset;
        public Action<ParticleSystem> Build;
    }

    /// <summary>
    /// 곁들임 목록. 순서는 기획 페이지의 순서(기본 공격 → W → Q → E → R)다.
    /// 대시-1과 공격-2, R-1은 이펙트 자식이 아니라 아래 따로 만든다.
    /// </summary>
    private static readonly GarnishSpec[] Garnishes =
    {
        // 공격-1 — 참격 궤적의 바깥 호(오른쪽으로 볼록한 초승달)에서 흩날린다.
        // 초승달이 피벗 기준 가로 ±1.2, 세로 ±2.0으로 그려져 있어서, 반지름 2.1인 원의 중심을 1유닛 뒤에 두면 호가 겹친다.
        new GarnishSpec { PrefabPath = SlashPath, ChildName = "SlashEmbers", Label = "공격-1 휘두름 불티",
                          WorldOffset = new Vector2(-1f, 0f), Build = BuildSlashEmbers },

        // W-1 — 화살 피벗이 촉 끝이라 1유닛 뒤(화살대 쪽)에서 뿌린다.
        new GarnishSpec { PrefabPath = ArrowPath, ChildName = "ArrowTrail", Label = "W-1 화살 불티 꼬리",
                          WorldOffset = new Vector2(-1f, 0f), Build = BuildArrowTrail },

        // W-3 — 명중 불꽃 자리(촉 끝)에서 튀고, 잔불은 바닥으로 떨어진다.
        new GarnishSpec { PrefabPath = ArrowImpactPath, ChildName = "ImpactSparks", Label = "W-3 명중 불똥",
                          WorldOffset = Vector2.zero, Build = BuildImpactSparks },
        new GarnishSpec { PrefabPath = ArrowImpactPath, ChildName = "ImpactDrips", Label = "W-3 떨어지는 잔불",
                          WorldOffset = Vector2.zero, Build = BuildImpactDrips },

        // Q-1·2 — 1단 충격 지점(바닥 피벗).
        new GarnishSpec { PrefabPath = SlamImpactPath, ChildName = "SlamDebris", Label = "Q-1 돌 파편",
                          WorldOffset = Vector2.zero, Build = BuildSlamDebris },
        new GarnishSpec { PrefabPath = SlamImpactPath, ChildName = "SlamDust", Label = "Q-2 바닥 재 먼지",
                          WorldOffset = Vector2.zero, Build = BuildSlamDust },

        // Q-3 — 2단 폭발의 균열선(바닥 피벗, 가로 ±7.4로 그려져 있다).
        new GarnishSpec { PrefabPath = SlamBurstPath, ChildName = "SlamRisingEmbers", Label = "Q-3 번지는 불티",
                          WorldOffset = Vector2.zero, Build = BuildSlamRisingEmbers },

        // E-1·3 — 고리 그림의 중심이 피벗보다 약 0.9유닛 위에 있다(AreaSkillData.areaCenterOffset과 같은 값).
        new GarnishSpec { PrefabPath = PillarPath, ChildName = "PillarOmen", Label = "E-1 예고 재 상승",
                          WorldOffset = new Vector2(0f, 0.9f), Build = BuildPillarOmen },
        new GarnishSpec { PrefabPath = PillarPath, ChildName = "PillarBurst", Label = "E-2 폭발 불티 기둥",
                          WorldOffset = Vector2.zero, Build = BuildPillarBurst },
        new GarnishSpec { PrefabPath = PillarPath, ChildName = "PillarCinders", Label = "E-3 바닥 잔불",
                          WorldOffset = new Vector2(0f, 0.9f), Build = BuildPillarCinders },

        // R-2·3 — 왕관 그림은 가운데 피벗이고 시전자 발밑에 놓인다. 판정 중심과 같은 자리다.
        new GarnishSpec { PrefabPath = CrownPath, ChildName = "CrownRing", Label = "R-2 판정 경계 불티 고리",
                          WorldOffset = Vector2.zero, Build = BuildCrownRing },
        new GarnishSpec { PrefabPath = CrownPath, ChildName = "CrownAshRain", Label = "R-3 재 비",
                          WorldOffset = Vector2.zero, Build = BuildCrownAshRain },
    };

    // ─────────────────────────────────────────────────────────────────────
    // 메뉴와 외부에서 부르는 함수
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 전부 만든다. 독립 프리팹 → 이펙트 자식 → 플레이어 프리팹 → 스킬 에셋 순서다.
    /// 뒤의 것이 앞에서 만든 프리팹을 참조하므로 순서를 바꾸면 안 된다.
    /// </summary>
    [MenuItem("Tools/재의 길/파티클/플레이어 파티클 만들기")]
    public static void Build()
    {
        var report = new List<string>();

        EnsureFolder(VfxFolder, "Particles");

        GameObject sparks = CreateOrLoadStandalone(HitSparksPath, "HitSparks", BuildHitSparks, "공격-2 명중 불똥", report);
        GameObject gather = CreateOrLoadStandalone(UltimateGatherPath, "UltimateGather", BuildUltimateGather, "R-1 모으기", report);

        var paths = new List<string>();
        foreach (GarnishSpec spec in Garnishes)
        {
            if (!paths.Contains(spec.PrefabPath)) paths.Add(spec.PrefabPath);
        }
        foreach (string path in paths) EnsureGarnish(path, report);

        EnsurePlayer(sparks, report);
        EnsureSkills(gather, report);

        AssetDatabase.SaveAssets();

        string changes = report.Count == 0 ? "  (바뀐 것 없음 — 이미 다 들어 있다)" : "  - " + string.Join("\n  - ", report);
        Debug.Log("[플레이어 파티클] 완료\n" + changes + "\n\n" +
                  "유니티에서 확인할 것(눈으로 맞추는 값):\n" +
                  "  1. 기본 공격으로 적을 쳐서 불똥이 몸통 높이에서 튀는지 — 아니면 Player/AttackHitbox의 HitSparkSpawner → Spark Height\n" +
                  "  2. Q를 좌우로 써서 번지는 불티가 몸에서 먼 쪽으로 번지는지, 균열 길이만큼만 솟는지 — SlamBurst/SlamRisingEmbers의 Shape(Edge) Radius·Speed\n" +
                  "  3. R 불티 고리가 판정 경계(반경 14)에서 멈추는지 — 너무 멀거나 가까우면 KingsEmber/CrownRing의 Velocity over Lifetime → Speed Modifier\n" +
                  "  4. E 고리 밖(예전 반경 12 안)의 적이 이제 안 맞는지");
    }

    /// <summary>
    /// 프리팹 하나에 정해진 곁들임이 빠져 있으면 채운다. 이펙트 빌더가 프리팹을 새로 구운 뒤 부른다
    /// (예: <see cref="AshEmberSlashVfxBuilder"/> — 복제 원본의 곁들임을 걷어낸 뒤 자기 것을 다시 넣는다).
    /// </summary>
    public static void EnsureGarnish(string prefabPath)
    {
        var report = new List<string>();
        EnsureGarnish(prefabPath, report);
        if (report.Count > 0) Debug.Log("[플레이어 파티클] " + string.Join(", ", report));
    }

    /// <summary>
    /// 복제본에서 곁들임 자식을 걷어낸다. 다른 이펙트를 복제해서 새 이펙트를 만드는 빌더가
    /// Instantiate 직후에 부른다 — 안 부르면 KingsEmber를 복제한 자폭병 폭발에 R의 재 비가 딸려 간다.
    /// </summary>
    public static void StripGarnish(GameObject instance)
    {
        if (instance == null) return;

        foreach (ParticleGarnish garnish in instance.GetComponentsInChildren<ParticleGarnish>(true))
            Object.DestroyImmediate(garnish.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 프리팹 조립
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 이펙트 프리팹을 열어 빠진 곁들임 자식만 더하고 저장한다.
    ///
    /// LoadPrefabContents로 여는 이유: 프리팹을 새로 굽지 않고 <b>있는 것에 더해야</b> 그림·재생 설정과
    /// 사람이 고친 값이 그대로 남는다(<see cref="AshSceneLightingBuilder"/>의 HeroLight와 같은 방식).
    /// </summary>
    internal static void EnsureGarnish(string prefabPath, List<string> report)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogWarning($"[플레이어 파티클] 이펙트 프리팹을 못 열었다: {prefabPath} — 이 이펙트의 곁들임은 건너뛴다.");
            return;
        }

        try
        {
            bool changed = false;
            Vector3 parentScale = root.transform.localScale;

            foreach (GarnishSpec spec in Garnishes)
            {
                if (spec.PrefabPath != prefabPath) continue;
                if (root.transform.Find(spec.ChildName) != null) continue;

                // 부모 배율로 나눠야 월드에서 정한 거리(유닛)만큼 떨어진다. 위치는 계층 배율을 받기 때문이다.
                var localPosition = new Vector3(
                    spec.WorldOffset.x / SafeScale(parentScale.x),
                    spec.WorldOffset.y / SafeScale(parentScale.y),
                    0f);

                ParticleSystem particles = CreateChildSystem(root.transform, spec.ChildName, localPosition);
                particles.gameObject.AddComponent<ParticleGarnish>();
                spec.Build(particles);

                report.Add($"{spec.Label} → {System.IO.Path.GetFileNameWithoutExtension(prefabPath)}/{spec.ChildName}");
                changed = true;
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 이펙트 자식이 아닌 독립 파티클 프리팹. 있으면 그대로 쓰고, 없으면 만든다.
    /// 루트 오브젝트가 곧 파티클 시스템이고, 다 끝나면 Stop Action(Destroy)으로 스스로 지워진다.
    /// </summary>
    internal static GameObject CreateOrLoadStandalone(
        string path, string name, Action<GameObject> build, string label, List<string> report)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        var root = new GameObject(name);
        try
        {
            build(root);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            if (!ok || saved == null)
            {
                Debug.LogError($"[플레이어 파티클] 프리팹 저장에 실패했다: {path}");
                return null;
            }

            report.Add($"{label} → {path}");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 플레이어 프리팹에 대시 바닥 불씨(자식)와 명중 불똥 발생기(검 판정)를 넣고 연결한다.
    ///
    /// 플레이어 프리팹 빌더(<see cref="AshPlayerPrefabBuilder"/>)는 프리팹을 처음부터 새로 굽는다.
    /// 그걸 다시 돌리면 HeroLight·대시 자국 연결과 함께 이것도 빠지므로, 그때는 이 메뉴를 한 번 더 돌린다.
    /// </summary>
    internal static void EnsurePlayer(GameObject sparkPrefab, List<string> report)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (root == null)
        {
            Debug.LogWarning($"[플레이어 파티클] 플레이어 프리팹을 못 열었다: {PlayerPrefabPath}");
            return;
        }

        try
        {
            bool changed = false;

            // 대시-1 — 발밑에 둔다. 불씨는 월드 공간이라 몸이 지나간 바닥에 남는다.
            Transform trail = root.transform.Find(DashTrailName);
            if (trail == null)
            {
                ParticleSystem particles = CreateChildSystem(root.transform, DashTrailName, new Vector3(0f, 0.1f, 0f));
                BuildDashTrail(particles);
                trail = particles.transform;
                report.Add("대시-1 발자국 잔불 → Player/" + DashTrailName);
                changed = true;
            }

            var controller = root.GetComponent<PlayerController>();
            if (controller != null)
            {
                var serialized = new SerializedObject(controller);
                SerializedProperty property = serialized.FindProperty("dashTrail");
                if (property != null && property.objectReferenceValue == null)
                {
                    property.objectReferenceValue = trail.GetComponent<ParticleSystem>();
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    report.Add("PlayerController.Dash Trail 연결");
                    changed = true;
                }
            }
            else
            {
                Debug.LogWarning("[플레이어 파티클] 플레이어 프리팹에 PlayerController가 없다. 대시 불씨가 안 켜진다.");
            }

            // 공격-2 — 검 판정에 붙여 명중 알림을 듣는다.
            Transform hitbox = root.transform.Find(AttackHitboxName);
            if (hitbox != null && hitbox.GetComponent<DamageHitbox>() != null)
            {
                var spawner = hitbox.GetComponent<HitSparkSpawner>();
                if (spawner == null)
                {
                    spawner = hitbox.gameObject.AddComponent<HitSparkSpawner>();
                    report.Add("공격-2 명중 불똥 발생기 → Player/" + AttackHitboxName);
                    changed = true;
                }

                var serialized = new SerializedObject(spawner);
                SerializedProperty property = serialized.FindProperty("sparkPrefab");
                if (property.objectReferenceValue == null && sparkPrefab != null)
                {
                    property.objectReferenceValue = sparkPrefab;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }
            else
            {
                Debug.LogWarning($"[플레이어 파티클] Player/{AttackHitboxName}(DamageHitbox)를 못 찾았다. 명중 불똥이 안 나온다.");
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 스킬 에셋 두 개를 고친다.
    ///
    /// E — 사용자 결정 "판정을 그림에 맞춘다". 반경 12 → 4.5, 세로 0.66, 판정 중심 0.9 위로.
    /// 4.5는 그림 고리의 가로 반경(약 4.7)보다 살짝 작다. 판정이 몸 콜라이더와의 <b>겹침</b>이라
    /// 고리에 몸이 걸친 적은 맞으므로, 그림보다 조금 작게 둬야 "고리 밖인데 맞았다"가 안 생긴다.
    /// <b>반경이 옛 값(12)일 때만 바꾼다</b> — 이미 바꿨거나 사람이 다른 값을 넣었으면 그대로 둔다.
    ///
    /// R — 시전 이펙트(모으기)가 비어 있으면 넣는다.
    /// </summary>
    internal static void EnsureSkills(GameObject gatherPrefab, List<string> report)
    {
        var staff = AssetDatabase.LoadAssetAtPath<AreaSkillData>(StaffSkillPath);
        if (staff != null)
        {
            var serialized = new SerializedObject(staff);
            SerializedProperty radius = serialized.FindProperty("radius");
            if (radius != null && Mathf.Approximately(radius.floatValue, 12f))
            {
                radius.floatValue = 4.5f;
                serialized.FindProperty("verticalScale").floatValue = 0.66f;
                serialized.FindProperty("areaCenterOffset").vector2Value = new Vector2(0f, 0.9f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(staff);
                report.Add("E 판정을 그림에 맞춤 — 반경 12 → 4.5, 세로 0.66, 중심 +0.9");
            }
        }
        else
        {
            Debug.LogWarning($"[플레이어 파티클] E 스킬 에셋을 못 찾았다: {StaffSkillPath}");
        }

        var ultimate = AssetDatabase.LoadAssetAtPath<AreaSkillData>(UltimateSkillPath);
        if (ultimate != null)
        {
            var serialized = new SerializedObject(ultimate);
            SerializedProperty cast = serialized.FindProperty("castEffectPrefab");
            if (cast != null && cast.objectReferenceValue == null && gatherPrefab != null)
            {
                cast.objectReferenceValue = gatherPrefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(ultimate);
                report.Add("R 시전 이펙트 연결 — UltimateGather");
            }
        }
        else
        {
            Debug.LogWarning($"[플레이어 파티클] R 스킬 에셋을 못 찾았다: {UltimateSkillPath}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 곁들임 하나하나 — 수치는 기획 페이지의 목업과 같다(개수에만 Amount를 곱한다)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 공격-1 휘두름 불티. 참격 그림(0.3초)이 사라진 뒤에도 잠깐 남아 휘두른 자리를 보여준다.
    /// 부모(EmberSlash)가 바라보는 방향으로 돌아 있으므로 호도 같이 돈다.
    /// </summary>
    internal static void BuildSlashEmbers(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.2f;
        // 참격 첫 프레임(가는 선)에서는 아직 안 뿌린다. 호가 다 펼쳐진 뒤부터다.
        main.startDelay = 0.05f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.22f);
        main.startRotation = RandomRotation();
        main.gravityModifier = 0.7f;
        main.maxParticles = 60;
        main.stopAction = ParticleSystemStopAction.Destroy;

        // 0.2초 동안 10개(목업) × 양.
        var emission = particles.emission;
        emission.rateOverTime = 50f * Amount;

        // 반지름 2.1인 원의 오른쪽 140도 — 초승달의 바깥 호다. 호가 +X를 가운데로 두게 -70도 돌린다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 2.1f;
        shape.radiusThickness = 0.15f;
        shape.arc = 140f;
        shape.rotation = new Vector3(0f, 0f, -70f);

        SetDrag(particles, 1.8f);
        SetColor(particles, EmberColors(), FlickerAlpha(0f, 0.55f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>
    /// 공격-2 명중 불똥(독립 프리팹). 루트가 재 가루, 자식이 불똥이다.
    ///
    /// 재 가루를 루트에 두는 이유: Stop Action(Destroy)은 <b>루트 시스템이 끝날 때</b> 오브젝트를 지운다.
    /// 수명이 긴 쪽(재 0.9초)이 루트여야 짧은 불똥(0.42초)과 같이 온전히 끝난다. 반대로 두면 재가 도중에 잘린다.
    /// 오른쪽(+X)으로 튀게 만들고, <see cref="HitSparkSpawner"/>가 공격 방향으로 돌려 놓는다.
    /// </summary>
    internal static void BuildHitSparks(GameObject root)
    {
        ParticleSystem ash = AddSystem(root);

        var main = ash.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.32f);
        main.startRotation = RandomRotation();
        main.gravityModifier = 0.15f;
        main.maxParticles = 20;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(ash, 0f, 4);
        ForwardCone(ash, 0.3f);
        SetDrag(ash, 1.5f);
        SetColor(ash, AshColors(), FadeAlpha(1f, 0f, 0.55f));
        SetRenderer(ash, "VFX", 2);

        ParticleSystem sparks = CreateChildSystem(root.transform, "Sparks", Vector3.zero);

        var sparkMain = sparks.main;
        sparkMain.duration = 0.1f;
        sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.42f);
        sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(7f, 15f);
        sparkMain.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.17f);
        // 떨어지는 불똥. 중력 22유닛/초²(목업 값) = 물리 중력 9.81 × 2.2.
        sparkMain.gravityModifier = 2.2f;
        sparkMain.maxParticles = 40;

        Burst(sparks, 0f, 14);
        ForwardCone(sparks, 0.1f);
        SetDrag(sparks, 3.5f);
        SetColor(sparks, HotColors(), FadeAlpha(1f, 0f, 0.55f));
        // 속도 방향으로 늘여 긁힌 불똥처럼 보이게 한다(0.035초 동안 움직이는 거리만큼).
        SetRenderer(sparks, "VFX", 3, stretch: 0.035f);
    }

    /// <summary>
    /// 대시-1 발자국 잔불(플레이어 자식). 평소에는 방출이 꺼져 있고 PlayerController가 대시 동안만 켠다.
    /// 몸이 움직인 거리만큼 뿌리므로(Rate over Distance) 대시 속도를 바꿔도 불씨 간격이 같다.
    /// </summary>
    internal static void BuildDashTrail(ParticleSystem particles)
    {
        var main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
        main.startRotation = RandomRotation();
        main.maxParticles = 200;

        // 몸에 붙어 다니며 거리를 재는 방출기라 Transform 이동으로 속도를 구하게 한다.
        // 플레이어의 Rigidbody2D를 따라가게 두면 설정에 따라 거리 계산이 어긋날 수 있다.
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;

        var emission = particles.emission;
        // 1유닛에 2개(목업) × 양 → 대시 한 번(11유닛)에 약 33개.
        emission.rateOverDistance = 2f * Amount;
        emission.enabled = false;

        // 발 너비만큼 흩는다. Box는 +Z로 뿜으므로 시작 속도 0(위 CreateChildSystem 기본값)이라 제자리에 깔린다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.9f, 0.7f, 0f);

        SetColor(particles, EmberColors(), FlickerAlpha(0f, 0.4f));
        // 바닥에 깔린 불씨라 캐릭터보다 아래(Decal)에 그린다. 몸을 덮으면 발밑이 아니라 몸이 타는 것처럼 보인다.
        SetRenderer(particles, "Decal", 0);
    }

    /// <summary>
    /// W-1 화살 불티 꼬리(EmberArrow 자식). 화살이 초당 34유닛이라 한 프레임에 1유닛 넘게 움직여서,
    /// 그림만으로 끊겨 보이는 길을 거리 기준 방출로 잇는다. 화살이 사라지면 <see cref="ParticleGarnish"/>가
    /// 떼어 내고 방출을 멈춘다.
    /// </summary>
    internal static void BuildArrowTrail(ParticleSystem particles)
    {
        var main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.38f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.19f);
        main.startRotation = RandomRotation();
        main.gravityModifier = 0.3f;
        main.maxParticles = 200;
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        // 1유닛에 5개(목업: 한 프레임 7개 ÷ 1.36유닛) × 양.
        emission.rateOverDistance = 5f * Amount;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.3f, 0.24f, 0f);

        // 화살 진행 반대쪽(-X)으로 조금 흘리고 위아래로 살짝 흩는다. 화살 방향으로 돌아 있으므로 Local 공간이다.
        SetLinearVelocity(particles, ParticleSystemSimulationSpace.Local,
                          new Vector2(-1.5f, -0.4f), new Vector2(-0.3f, 0.5f));

        SetColor(particles, EmberColors(), FlickerAlpha(0f, 0.55f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>W-3 명중 불똥(EmberArrowImpact 자식). 명중 불꽃 둘레로 사방에 튄다.</summary>
    internal static void BuildImpactSparks(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.16f);
        main.gravityModifier = 2f;
        main.maxParticles = 40;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(particles, 0f, 12);

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f;
        shape.radiusThickness = 1f;
        shape.arc = 360f;

        SetDrag(particles, 3f);
        SetColor(particles, HotColors(), FadeAlpha(1f, 0f, 0.55f));
        SetRenderer(particles, "VFX", 2, stretch: 0.035f);
    }

    /// <summary>
    /// W-3 떨어지는 잔불(EmberArrowImpact 자식). 화살 높이(1.2유닛)에서 바닥까지 떨어져 1초 가까이 깜빡인다.
    ///
    /// "떨어져서 멈춘다"를 중력과 충돌 없이 Velocity over Lifetime 곡선 하나로 만든다. 수명 35%까지 점점 빨리
    /// 떨어지다가 거기서 속도가 0이 된다. 충돌 평면을 쓰면 모든 잔불이 같은 가로줄에 내려앉아 어색하다.
    /// 월드 공간이라 화살이 어느 방향으로 날아왔든 떨어지는 쪽은 화면 아래다.
    /// </summary>
    internal static void BuildImpactDrips(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.22f);
        main.maxParticles = 20;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(particles, 0f, 5);

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.2f, 0.2f, 0f);

        // 떨어지는 거리 = 속도 곡선의 넓이 × 수명 = k × 0.175 × 수명. 수명 0.95초에서 k 5~9.6이면 0.8~1.6유닛.
        AnimationCurve fastest = Curve((0f, 0f), (0.35f, -1f), (0.36f, 0f), (1f, 0f));
        AnimationCurve slowest = Curve((0f, 0f), (0.35f, -0.52f), (0.36f, 0f), (1f, 0f));
        SetCurveVelocity(particles, 9.6f, fastest, slowest);

        SetColor(particles, EmberColors(), FlickerAlpha(0f, 0.6f));
        SetRenderer(particles, "VFX", 2);
    }

    /// <summary>
    /// Q-1 돌 파편(SlamImpact 자식). 튀어 올랐다가 떨어져 <b>자기가 튀어나온 높이</b>에서 멈춘다.
    ///
    /// 세로 속도 곡선이 수명 45%까지 +v에서 -v로 곧게 내려가면 그 구간의 넓이가 0이라 제자리 높이로 돌아온다.
    /// 튀어나온 자리가 방출 상자 안에서 위아래로 다르므로 내려앉는 높이도 제각각이다 — 탑다운 바닥의 깊이로 읽힌다.
    /// 가로 속도는 같은 지점에서 0이 되어 내려앉은 돌이 미끄러지지 않는다.
    /// </summary>
    internal static void BuildSlamDebris(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.startRotation = RandomRotation();
        main.maxParticles = 40;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(particles, 0f, 14);

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(3f, 0.8f, 0f);
        shape.position = new Vector3(0f, 0.4f, 0f);

        // 세로: 튀는 속도 8~20(최고 높이 약 0.9~2.1유닛). 가로: -5~5.
        var vol = particles.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.x = new ParticleSystem.MinMaxCurve(5f,
            Curve((0f, -1f), (0.45f, -1f), (0.46f, 0f), (1f, 0f)),
            Curve((0f, 1f), (0.45f, 1f), (0.46f, 0f), (1f, 0f)));
        vol.y = new ParticleSystem.MinMaxCurve(20f,
            Curve((0f, 0.4f), (0.45f, -0.4f), (0.46f, 0f), (1f, 0f)),
            Curve((0f, 1f), (0.45f, -1f), (0.46f, 0f), (1f, 0f)));
        vol.z = new ParticleSystem.MinMaxCurve(1f, ZeroCurve(), ZeroCurve());

        // 돌은 식지 않는다. 숯빛으로 어두워지다가 끝에서만 사라진다.
        SetColor(particles, StoneColors(), FadeAlpha(1f, 0f, 0.75f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>
    /// Q-2 바닥 재 먼지(SlamImpact 자식). 충격 지점에서 바닥을 따라 바깥으로 번진다.
    /// 방출 상자가 가로로 넓어서 중심에서 멀어지는 속도(Radial)가 곧 좌우로 퍼지는 속도다.
    /// </summary>
    internal static void BuildSlamDust(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        main.startRotation = RandomRotation();
        main.maxParticles = 40;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(particles, 0f, 20);

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(5f, 1.2f, 0f);

        // 바깥으로 3~8에서 시작해 수명 60%에 멈춘다(목업의 공기 저항 대신 곧은 감속).
        var vol = particles.velocityOverLifetime;
        vol.enabled = true;
        vol.x = new ParticleSystem.MinMaxCurve(1f, ZeroCurve(), ZeroCurve());
        vol.y = new ParticleSystem.MinMaxCurve(1f, ZeroCurve(), ZeroCurve());
        vol.z = new ParticleSystem.MinMaxCurve(1f, ZeroCurve(), ZeroCurve());
        vol.radial = new ParticleSystem.MinMaxCurve(8f,
            Curve((0f, 0.375f), (0.6f, 0f), (1f, 0f)),
            Curve((0f, 1f), (0.6f, 0f), (1f, 0f)));

        // 먼지는 반투명(65%)이다. 불투명하면 바닥 무늬를 덮는 얼룩이 된다.
        SetColor(particles, AshColors(), FadeAlpha(0.65f, 0f, 0.3f));
        // 바닥을 기는 먼지라 캐릭터 아래에 그린다.
        SetRenderer(particles, "Decal", 1);
    }

    /// <summary>
    /// Q-3 번지는 불티(SlamBurst 자식). 2단 폭발이 앞으로 퍼지는 동안 균열선을 따라 몸에서 먼 쪽으로
    /// 차례로 솟는다. 차례로 나오게 하는 일은 Shape(Edge)의 Loop 모드가 한다 — 방출 위치가 선을 따라 움직인다.
    /// 왼쪽으로 칠 때의 방향과 위·아래로 칠 때의 길이는 <see cref="ParticleGarnish.MatchSprite"/>가 맞춘다.
    /// </summary>
    internal static void BuildSlamRisingEmbers(ParticleSystem particles)
    {
        const float sweepSeconds = 0.225f; // 목업: 0.025초 간격 9번

        var main = particles.main;
        main.duration = sweepSeconds;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.26f, 0.4f);
        main.startRotation = RandomRotation();
        main.gravityModifier = 1f;
        main.maxParticles = 80;
        main.stopAction = ParticleSystemStopAction.Destroy;

        // 9번 × 3개(목업) × 양을 0.225초에 고르게 나눈다.
        var emission = particles.emission;
        emission.rateOverTime = 27f * Amount / sweepSeconds;

        // Edge는 로컬 X를 따라 놓인 선이고 +Y(위)로 뿜는다. 반지름 6.5 = 균열 그림(±7.4)보다 조금 안쪽.
        // Loop 모드의 속도는 초당 왕복 수라, 1 ÷ 0.225면 방출이 끝날 때 선 끝에 닿는다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.SingleSidedEdge;
        shape.radius = 6.5f;
        shape.radiusMode = ParticleSystemShapeMultiModeValue.Loop;
        shape.radiusSpeed = 1f / sweepSeconds;
        shape.randomPositionAmount = 0.4f;

        SetDrag(particles, 0.8f);
        SetColor(particles, EmberColors(), FlickerAlpha(0f, 0.55f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>
    /// E-1 예고 재 상승(AshPillar 자식). 고리가 깔리고 터지기까지 0.5초 동안 고리 안에서 재와 불씨가 떠오른다.
    /// 이 0.5초가 E의 핵심(적이 올 자리를 읽는 플레이)이라 "곧 터진다"를 더 세게 보여준다.
    /// </summary>
    internal static void BuildPillarOmen(ParticleSystem particles)
    {
        var main = particles.main;
        // 폭발(AreaSkillData.explodeDelay 0.5)까지만 뿌린다.
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
        main.startRotation = RandomRotation();
        main.maxParticles = 120;
        main.stopAction = ParticleSystemStopAction.Destroy;

        // 불씨 45%, 재 55%. 고정(Fixed) 그라디언트에서 무작위로 고르면 둘 중 하나가 나온다(섞인 흙빛이 안 생긴다).
        main.startColor = PickOne(new Color(1f, 0.62f, 0.25f), 0.45f, AshLight);

        var emission = particles.emission;
        emission.rateOverTime = 75f * Amount;

        // 고리 모양(가로 4.4 × 세로 2.8)의 타원 안을 채운다.
        FilledEllipse(particles, new Vector2(4.4f, 2.8f));

        SetLinearVelocity(particles, ParticleSystemSimulationSpace.World,
                          new Vector2(-0.3f, 1.2f), new Vector2(0.3f, 3f));

        // 떠오르며 어두워진다(불씨는 식고 재는 옅어진다).
        SetColor(particles, Colors((Color.white, 0f), (new Color(0.55f, 0.55f, 0.55f), 1f)),
                 FadeAlpha(1f, 0.15f, 0.55f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>
    /// E-2 폭발 불티 기둥(AshPillar 자식). 판정이 들어가는 순간(0.5초)에 기둥을 따라 위로 치솟는다.
    /// 원뿔 부피에서 위로 쏘면 기둥 높이 전체에서 불티가 나온다.
    /// </summary>
    internal static void BuildPillarBurst(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.2f;
        main.startDelay = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(12f, 22f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.26f, 0.38f);
        main.gravityModifier = 1.63f;
        main.maxParticles = 60;
        main.stopAction = ParticleSystemStopAction.Destroy;

        // 0.1초에 걸쳐 세 번(목업: 0.1초에 24개) × 양. 한 번에 다 뿜으면 흰 덩어리로 뭉쳐 보였다.
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, Count(8)),
            new ParticleSystem.Burst(0.05f, Count(8)),
            new ParticleSystem.Burst(0.1f, Count(8)),
        });

        // 원뿔은 로컬 +Z로 뿜으므로 X로 -90도 눕혀 +Y(위)로 세운다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.ConeVolume;
        shape.angle = 8f;
        shape.radius = 1.4f;
        shape.radiusThickness = 1f;
        shape.length = 4.5f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        shape.position = new Vector3(0f, 0.5f, 0f);

        SetDrag(particles, 0.6f);
        SetColor(particles, EmberColors(), FadeAlpha(1f, 0f, 0.55f));
        SetRenderer(particles, "VFX", 2, stretch: 0.03f);
    }

    /// <summary>
    /// E-3 바닥 잔불(AshPillar 자식). 폭발 뒤(0.8초부터 0.5초 동안) 고리 안에 불씨가 남는다.
    /// 이펙트 그림은 0.86초에 끝나지만 <see cref="ParticleGarnish"/>가 떼어 내므로 끝까지 나온다.
    /// </summary>
    internal static void BuildPillarCinders(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.5f;
        main.startDelay = 0.8f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
        main.startRotation = RandomRotation();
        main.maxParticles = 80;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        emission.rateOverTime = 50f * Amount;

        FilledEllipse(particles, new Vector2(4f, 2.4f));

        SetColor(particles, EmberColors(), FlickerAlpha(0.1f, 0.4f));
        // 바닥에 남는 불씨라 캐릭터 아래에 그린다.
        SetRenderer(particles, "Decal", 1);
    }

    /// <summary>
    /// R-1 모으기(독립 프리팹). 시전하는 0.5초 동안 몸 둘레(반지름 5.5)의 불티가 몸 가운데로 빨려 든다.
    /// 수명과 속도를 상수로 맞춰 모든 불티가 <b>정확히 가운데에 닿는 순간</b> 사라진다 — 가운데를 지나쳐
    /// 반대편으로 튀어나가면 모으는 게 아니라 흩어지는 것처럼 보인다.
    /// </summary>
    internal static void BuildUltimateGather(GameObject root)
    {
        const float lifetime = 0.4f;
        const float spawnRadius = 5.5f;
        const float endRadius = 0.4f;

        ParticleSystem particles = AddSystem(root);

        var main = particles.main;
        main.duration = 0.45f;
        main.startLifetime = lifetime;
        // 음수 속도 = 원의 가장자리에서 가운데로.
        main.startSpeed = -(spawnRadius - endRadius) / lifetime;
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
        main.maxParticles = 100;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        emission.rateOverTime = 100f * Amount;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = spawnRadius;
        shape.radiusThickness = 0f;
        shape.arc = 360f;

        // 가운데로 갈수록 밝아진다(적갈 → 황금 → 흰 심). 나타날 때만 다듬는다.
        SetColor(particles, Colors((Deep, 0f), (Amber, 0.5f), (Core, 1f)), FadeAlpha(1f, 0.3f, 1f));
        SetRenderer(particles, "VFX", 2, stretch: 0.03f);
    }

    /// <summary>
    /// R-2 판정 경계 불티 고리(KingsEmber 자식). 판정 순간(시전 0.5 + 0.15초)에 고르게 뿜어져 판정 반경에서 멈춘다.
    /// 시작 속도는 <see cref="AreaRingParticles"/>가 스킬의 반경으로 다시 계산한다. 여기 값은 반경 14 기준이다.
    /// </summary>
    internal static void BuildCrownRing(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.2f;
        // 이펙트는 castDelay에 생기고 판정은 그 0.15초 뒤(explodeDelay)다.
        main.startDelay = 0.15f;
        main.startLifetime = RingLifetime;
        main.startSpeed = (14f - RingStartRadius) / (RingLifetime * RingTravelFraction * 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.6f);
        main.maxParticles = 200;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(particles, 0f, 96);

        // 원 둘레에 고르게(Burst Spread) 놓는다. 무작위면 고리에 구멍이 생긴다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = RingStartRadius;
        shape.radiusThickness = 0f;
        shape.arc = 360f;
        shape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;

        // 수명 35%까지 속도를 곧게 줄여 멈춘다. 이 모듈의 모든 곡선을 같은 모드(Curve)로 맞춘다.
        var vol = particles.velocityOverLifetime;
        vol.enabled = true;
        vol.x = new ParticleSystem.MinMaxCurve(1f, ZeroCurve());
        vol.y = new ParticleSystem.MinMaxCurve(1f, ZeroCurve());
        vol.z = new ParticleSystem.MinMaxCurve(1f, ZeroCurve());
        vol.speedModifier = new ParticleSystem.MinMaxCurve(1f,
            Curve((0f, 1f), (RingTravelFraction, 0f), (1f, 0f)));

        var ring = particles.gameObject.AddComponent<AreaRingParticles>();
        var serialized = new SerializedObject(ring);
        serialized.FindProperty("startRadius").floatValue = RingStartRadius;
        serialized.FindProperty("travelFraction").floatValue = RingTravelFraction;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // 경계에 선 뒤 깜빡이며 꺼진다.
        var alpha = new[]
        {
            new GradientAlphaKey(1f, 0f),
            new GradientAlphaKey(1f, 0.5f),
            new GradientAlphaKey(0.45f, 0.6f),
            new GradientAlphaKey(1f, 0.7f),
            new GradientAlphaKey(0.4f, 0.8f),
            new GradientAlphaKey(0.8f, 0.88f),
            new GradientAlphaKey(0f, 1f),
        };
        SetColor(particles, HotColors(), alpha);
        SetRenderer(particles, "VFX", 2, stretch: 0.02f);
    }

    /// <summary>
    /// R-3 재 비(KingsEmber 자식). 폭발 뒤 1.25초 동안 화면 전체에 재가 천천히 내린다. 네 개 중 하나는 불씨다.
    /// 방출 상자(60 × 34)는 화면(1080p에서 약 56.5 × 31.8유닛)보다 조금 크다. 카메라가 플레이어를 따라가므로
    /// 시전자 발밑을 가운데로 두면 화면이 덮인다.
    /// </summary>
    internal static void BuildCrownAshRain(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 1.25f;
        main.startDelay = 0.25f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.55f);
        main.startRotation = RandomRotation();
        main.maxParticles = 500;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.startColor = PickOne(AshLight, 0.75f, new Color(1f, 0.62f, 0.25f));

        var emission = particles.emission;
        emission.rateOverTime = 125f * Amount;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(60f, 34f, 0f);

        SetLinearVelocity(particles, ParticleSystemSimulationSpace.World,
                          new Vector2(-0.3f, -2.2f), new Vector2(0.3f, -1.2f));

        // 허공에서 갑자기 생기지 않게 나타날 때 다듬고, 내리면서 어두워진다.
        SetColor(particles, Colors((Color.white, 0f), (new Color(0.6f, 0.6f, 0.6f), 1f)),
                 FadeAlpha(1f, 0.25f, 0.6f));
        // 화면 전체를 덮으므로 다른 불티(1~3)보다 뒤에 그린다.
        SetRenderer(particles, "VFX", 0);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 공통 조립 도구
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 자식 오브젝트를 만들고 파티클 시스템을 기본값으로 붙인다.
    /// 회전은 넣지 않는다 — <see cref="ParticleGarnish.MatchSprite"/>가 회전 없이 만들어졌다고 보고 좌우 반전을 넣는다.
    /// </summary>
    internal static ParticleSystem CreateChildSystem(Transform parent, string name, Vector3 localPosition)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return AddSystem(child);
    }

    /// <summary>
    /// 파티클 시스템을 붙이고 이 게임의 공통 규칙으로 기본값을 맞춘다.
    ///
    /// 유니티가 새 시스템에 넣는 기본값(5초 반복, 초당 10개, 속도 5, 원뿔)은 전부 덮는다. 곁들임마다 필요한 것만
    /// 다시 넣으므로, 여기서 안 덮으면 "속도를 안 적은 곁들임이 유니티 기본 속도 5로 날아가는" 일이 생긴다.
    /// </summary>
    internal static ParticleSystem AddSystem(GameObject target)
    {
        var particles = target.AddComponent<ParticleSystem>();

        // 재생 중에는 duration을 못 바꾼다. 편집 중에 붙인 시스템이 미리보기로 돌고 있을 수 있어서 먼저 세운다.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = particles.main;
        main.loop = false;
        main.playOnAwake = true;
        main.duration = 0.1f;
        main.startDelay = 0f;
        main.startLifetime = 0.5f;
        main.startSpeed = 0f;
        main.startSize = 0.2f;
        main.startRotation = 0f;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.useUnscaledTime = false;
        main.stopAction = ParticleSystemStopAction.None;

        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f;
        shape.radiusThickness = 1f;
        shape.arc = 360f;

        return particles;
    }

    /// <summary>시작 시각 time에 count × 양만큼 한 번에 뿜는다. 상시 방출은 끈다.</summary>
    internal static void Burst(ParticleSystem particles, float time, int count)
    {
        var emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(time, Count(count)) });
    }

    /// <summary>목업의 개수에 양을 곱해 반올림한다.</summary>
    internal static short Count(int count)
    {
        return (short)Mathf.Max(1, Mathf.RoundToInt(count * Amount));
    }

    /// <summary>
    /// 오른쪽(+X)을 가운데로 ±65도 부채꼴로 뿜는다. 목업의 명중 불똥 퍼짐과 같다.
    /// 원(Circle) 셰이프의 호는 +X에서 시작해 반시계로 도므로 -65도 돌려 가운데를 +X에 맞춘다.
    /// </summary>
    internal static void ForwardCone(ParticleSystem particles, float radius)
    {
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 1f;
        shape.arc = 130f;
        shape.rotation = new Vector3(0f, 0f, -65f);
    }

    /// <summary>
    /// 원 셰이프를 셰이프 모듈의 배율로 눌러 타원 안을 채운다. 반지름 1을 두고 배율에 가로·세로 반경을 넣는다.
    /// 오브젝트 배율로 누르지 않는 이유: Scaling Mode가 Local이라 오브젝트 배율은 불티 크기까지 누른다.
    /// </summary>
    internal static void FilledEllipse(ParticleSystem particles, Vector2 radii)
    {
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;
        shape.radiusThickness = 1f;
        shape.arc = 360f;
        shape.scale = new Vector3(radii.x, radii.y, 1f);
    }

    /// <summary>
    /// 공기 저항. Limit Velocity 모듈의 Drag만 쓰고 속도 상한은 사실상 끈다(1000).
    /// 목업의 "매 프레임 속도 × (1 - drag × dt)"와 같은 성격의 감속이다.
    /// </summary>
    internal static void SetDrag(ParticleSystem particles, float drag)
    {
        var limit = particles.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1000f;
        limit.dampen = 0f;
        limit.drag = drag;
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    /// <summary>
    /// 수명 내내 일정한 추가 속도를 두 값 사이에서 무작위로 준다. 세 축이 같은 모드(두 상수)여야 모듈이 동작한다.
    /// </summary>
    internal static void SetLinearVelocity(ParticleSystem particles, ParticleSystemSimulationSpace space,
                                          Vector2 min, Vector2 max)
    {
        var vol = particles.velocityOverLifetime;
        vol.enabled = true;
        vol.space = space;
        vol.x = new ParticleSystem.MinMaxCurve(min.x, max.x);
        vol.y = new ParticleSystem.MinMaxCurve(min.y, max.y);
        vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    /// <summary>세로 속도만 두 곡선 사이에서 무작위로 준다(월드 공간). 가로·깊이는 0 곡선으로 모드를 맞춘다.</summary>
    internal static void SetCurveVelocity(ParticleSystem particles, float multiplier,
                                         AnimationCurve maxY, AnimationCurve minY)
    {
        var vol = particles.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.World;
        vol.x = new ParticleSystem.MinMaxCurve(1f, ZeroCurve(), ZeroCurve());
        vol.y = new ParticleSystem.MinMaxCurve(multiplier, minY, maxY);
        vol.z = new ParticleSystem.MinMaxCurve(1f, ZeroCurve(), ZeroCurve());
    }

    /// <summary>수명에 걸친 색과 투명도. 시작 색(흰색)에 곱해진다.</summary>
    internal static void SetColor(ParticleSystem particles, GradientColorKey[] colors, GradientAlphaKey[] alphas)
    {
        var gradient = new Gradient();
        gradient.SetKeys(colors, alphas);

        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    /// <summary>
    /// 렌더러. 보스 전환 파티클과 같은 재료(텍스처 없는 Sprites-Default = 각진 네모)다.
    /// sharedMaterial을 쓰는 이유: 편집 중에 material을 건드리면 재질 사본이 생겨 프리팹에 섞여 저장된다.
    /// </summary>
    /// <param name="stretch">0보다 크면 속도 방향으로 늘인다(그 초만큼 움직인 거리가 꼬리 길이).</param>
    internal static void SetRenderer(ParticleSystem particles, string sortingLayer, int sortingOrder, float stretch = 0f)
    {
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        renderer.sortingLayerName = sortingLayer;
        renderer.sortingOrder = sortingOrder;

        if (stretch > 0f)
        {
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = stretch;
            renderer.lengthScale = 1f;
        }
        else
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }
    }

    /// <summary>
    /// 두 색 중 하나를 무작위로 고르는 시작 색. firstShare는 첫 색이 나올 비율이다.
    /// 두 색 사이 무작위(Random Between Two Colors)는 중간색이 섞여 나와서 쓰지 않는다.
    /// </summary>
    internal static ParticleSystem.MinMaxGradient PickOne(Color first, float firstShare, Color second)
    {
        var gradient = new Gradient { mode = GradientMode.Fixed };
        gradient.SetKeys(
            new[] { new GradientColorKey(first, firstShare), new GradientColorKey(second, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
    }

    /// <summary>0~360도 무작위 회전(라디안). 각진 조각이 같은 방향으로 깔리면 격자처럼 보인다.</summary>
    internal static ParticleSystem.MinMaxCurve RandomRotation()
    {
        return new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
    }

    internal static GradientColorKey[] EmberColors()
    {
        return Colors((Core, 0f), (Amber, 0.15f), (Ember, 0.45f), (Deep, 0.75f), (Ash, 1f));
    }

    internal static GradientColorKey[] HotColors()
    {
        return Colors((HotWhite, 0f), (Core, 0.25f), (Amber, 0.55f), (Ember, 0.85f), (Deep, 1f));
    }

    internal static GradientColorKey[] AshColors()
    {
        return Colors((AshLight, 0f), (Ash, 1f));
    }

    internal static GradientColorKey[] StoneColors()
    {
        return Colors((Stone, 0f), (StoneDark, 0.6f), (Coal, 1f));
    }

    internal static GradientColorKey[] Colors(params (Color color, float time)[] keys)
    {
        var result = new GradientColorKey[keys.Length];
        for (int i = 0; i < keys.Length; i++) result[i] = new GradientColorKey(keys[i].color, keys[i].time);
        return result;
    }

    /// <summary>
    /// 투명도: fadeIn까지 0에서 peak로 오르고, fadeStart부터 끝까지 0으로 내려간다. fadeIn이 0이면 처음부터 peak다.
    /// </summary>
    internal static GradientAlphaKey[] FadeAlpha(float peak, float fadeIn, float fadeStart)
    {
        var keys = new List<GradientAlphaKey>(4);
        keys.Add(new GradientAlphaKey(fadeIn > 0f ? 0f : peak, 0f));
        if (fadeIn > 0f) keys.Add(new GradientAlphaKey(peak, fadeIn));

        // fadeStart가 1이면 끝까지 불투명하다(R 모으기 — 가운데에 닿는 순간 수명이 끝난다).
        // 같은 시각(1)에 키를 두 개 두면 어느 쪽이 이기는지 보장되지 않아서 그때는 끝 키를 하나만 둔다.
        if (fadeStart >= 1f)
        {
            keys.Add(new GradientAlphaKey(peak, 1f));
            return keys.ToArray();
        }

        keys.Add(new GradientAlphaKey(peak, Mathf.Max(fadeIn, fadeStart)));
        keys.Add(new GradientAlphaKey(0f, 1f));
        return keys.ToArray();
    }

    /// <summary>
    /// 깜빡이는 투명도. 불티마다 태어난 시각과 수명이 달라서, 같은 곡선이어도 화면에서는 제각각 깜빡인다.
    /// 알파 키는 최대 8개라 깜빡임을 두세 번으로 줄였다.
    /// </summary>
    internal static GradientAlphaKey[] FlickerAlpha(float fadeIn, float fadeStart)
    {
        var keys = new List<GradientAlphaKey>(8);
        keys.Add(new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f));
        if (fadeIn > 0f) keys.Add(new GradientAlphaKey(1f, fadeIn));

        // 페이드가 시작되기 전까지 두 번 흐려졌다 돌아온다.
        float span = Mathf.Max(0.05f, fadeStart - fadeIn);
        keys.Add(new GradientAlphaKey(0.55f, fadeIn + span * 0.3f));
        keys.Add(new GradientAlphaKey(1f, fadeIn + span * 0.55f));
        keys.Add(new GradientAlphaKey(0.6f, fadeIn + span * 0.8f));
        keys.Add(new GradientAlphaKey(1f, fadeStart));
        keys.Add(new GradientAlphaKey(0f, 1f));
        return keys.ToArray();
    }

    /// <summary>곧은(선형) 곡선. 파티클 곡선은 -1~1로 두고 곱할 값(multiplier)을 따로 준다.</summary>
    internal static AnimationCurve Curve(params (float time, float value)[] keys)
    {
        var curve = new AnimationCurve();
        foreach (var (time, value) in keys) curve.AddKey(new Keyframe(time, value));

        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
        }

        return curve;
    }

    internal static AnimationCurve ZeroCurve()
    {
        return Curve((0f, 0f), (1f, 0f));
    }

    internal static float SafeScale(float value)
    {
        return Mathf.Abs(value) < 0.0001f ? 1f : value;
    }

    /// <summary>폴더가 없으면 만든다.</summary>
    internal static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
