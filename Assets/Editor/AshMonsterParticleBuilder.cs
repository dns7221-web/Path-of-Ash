using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static AshPlayerParticleBuilder;
using Object = UnityEngine.Object;

/// <summary>
/// 추가 생성(2026-09-19) — 일반 몬스터(망령·사수·자폭병)의 파티클 곁들임을 만들고 연결한다.
/// 메뉴: Tools → 재의 길 → 파티클 → 몬스터 파티클 만들기
///
/// 기획 페이지(09-17)에서 사용자가 고른 10개를 옮긴다. 망령-1·2, 사수-1·2·3, 자폭병-1·2·3·4, 공통-1.
/// 양은 "화려하게"(×1.5), 적 불티는 흰 심 없이 황금에서 시작해 더 붉게 식는다(플레이어 불티와 구별).
/// 자폭병 도화선 불똥만 예외로 흰 심을 쓴다 — 곧 터질 불이라 가장 뜨거워 보여야 한다.
///
/// 조립 방식과 공통 규칙은 <see cref="AshPlayerParticleBuilder"/>와 같다(그 도우미를 그대로 쓴다):
/// 이펙트에 곁들이는 것은 자식 + <see cref="ParticleGarnish"/>, 몸에 붙어 상태에 따라 켜고 끄는 것은 몬스터 프리팹 자식,
/// 순간에 한 번 만드는 것은 독립 프리팹. <b>이미 있으면 건드리지 않는다</b> — 자식 이름과 참조 칸이 비었는지로 판단한다.
///
/// 몬스터 프리팹 빌더(적·사수·자폭병 프리팹 생성)로 프리팹을 새로 구우면 여기서 붙인 것이 빠진다. 그때는 이 메뉴를 다시 돌린다.
/// 사수 화살·자폭병 폭발 빌더는 저장한 뒤 이 도구를 스스로 불러 곁들임을 다시 넣는다.
/// </summary>
public static class AshMonsterParticleBuilder
{
    private const string VfxFolder = "Assets/Project/Prefabs/VFX";
    private const string ParticleFolder = VfxFolder + "/Particles";

    private const string WindupGatherPath = ParticleFolder + "/WraithWindupGather.prefab";
    private const string AimGatherPath = ParticleFolder + "/MarksmanAimGather.prefab";
    private const string ArrowBreakPath = ParticleFolder + "/ArrowAshBreak.prefab";
    private const string DeathAshPath = ParticleFolder + "/EnemyDeathAsh.prefab";

    private const string MarksmanArrowPath = VfxFolder + "/AshMarksmanArrow.prefab";
    private const string BlastPath = VfxFolder + "/BomberBlast.prefab";
    private const string PlayerArrowImpactPath = VfxFolder + "/EmberArrowImpact.prefab";

    private const string WraithPath = "Assets/Project/Prefabs/Enemies/AshEmberWraith.prefab";
    private const string MarksmanPath = "Assets/Project/Prefabs/Enemies/AshMarksman.prefab";
    private const string BomberPath = "Assets/Project/Prefabs/Enemies/AshBomber.prefab";

    /// <summary>양 — 사용자가 "화려하게"를 골랐다. 개수(Count)도 같은 1.5를 곱한다(AshPlayerParticleBuilder.Amount).</summary>
    private const float Amount = 1.5f;

    /// <summary>
    /// 자폭병 폭발 반경의 기본값. 폭발 뒤 잔불 원 크기에만 쓴다. 경고 링은 실행 중에 EnemyBomber가 실제 반경을 넣는다.
    /// </summary>
    private const float BlastRadius = 6f;

    /// <summary>
    /// 전부 만든다. 독립 프리팹 → 사수 화살·폭발 이펙트 → 몬스터 프리팹 세 개 순서다.
    /// 뒤의 것이 앞에서 만든 프리팹을 참조하므로 순서를 바꾸면 안 된다.
    /// </summary>
    [MenuItem("Tools/재의 길/파티클/몬스터 파티클 만들기")]
    public static void Build()
    {
        var report = new List<string>();
        EnsureFolder(VfxFolder, "Particles");

        GameObject windup = CreateOrLoadStandalone(WindupGatherPath, "WraithWindupGather", BuildWindupGather, "망령-1 예비동작 불씨", report);
        GameObject aim = CreateOrLoadStandalone(AimGatherPath, "MarksmanAimGather", BuildAimGather, "사수-1 조준 불씨", report);
        GameObject arrowBreak = CreateOrLoadStandalone(ArrowBreakPath, "ArrowAshBreak", BuildArrowBreak, "사수-3 화살 재 부서짐", report);
        GameObject deathAsh = CreateOrLoadStandalone(DeathAshPath, "EnemyDeathAsh", BuildDeathAsh, "공통-1 사망 재 흩날림", report);

        EnsureMarksmanArrow(arrowBreak, report);
        EnsureBlast(report);
        EnsureWraith(windup, deathAsh, report);
        EnsureMarksman(aim, deathAsh, report);
        EnsureBomber(deathAsh, report);

        AssetDatabase.SaveAssets();

        string changes = report.Count == 0 ? "  (바뀐 것 없음 — 이미 다 들어 있다)" : "  - " + string.Join("\n  - ", report);
        Debug.Log("[몬스터 파티클] 완료\n" + changes + "\n\n" +
                  "유니티에서 확인할 것:\n" +
                  "  1. 자폭병 점화 때 경고 링이 폭발 반경에 깔리고 터질 때 사라지는지, 도화선 불똥이 머리에서 튀는지 — 아니면 AshBomber/FuseSparks 위치\n" +
                  "  2. 사수 조준 불씨가 활 앞(화살이 나오는 자리)에 모이는지, 화살 꼬리가 화살 그림을 따라 뜨는지\n" +
                  "  3. 망령 예비동작 불씨가 몸 앞에 모이는지, 돌진한 길에 불씨가 남는지\n" +
                  "  4. 적이 쓰러질 때 재가 흩날리는지(자폭병이 스스로 터질 때는 안 나온다)");
    }

    /// <summary>사수 화살 빌더가 프리팹을 새로 구운 뒤 부른다. 꼬리·부서짐 연결을 다시 넣는다.</summary>
    public static void EnsureMarksmanArrow()
    {
        var report = new List<string>();
        EnsureFolder(VfxFolder, "Particles");
        GameObject arrowBreak = CreateOrLoadStandalone(ArrowBreakPath, "ArrowAshBreak", BuildArrowBreak, "사수-3 화살 재 부서짐", report);
        EnsureMarksmanArrow(arrowBreak, report);
        if (report.Count > 0) Debug.Log("[몬스터 파티클] " + string.Join(", ", report));
    }

    /// <summary>자폭병 폭발 빌더가 프리팹을 새로 구운 뒤 부른다. 파편·재·잔불을 다시 넣는다.</summary>
    public static void EnsureBlast()
    {
        var report = new List<string>();
        EnsureBlast(report);
        if (report.Count > 0) Debug.Log("[몬스터 파티클] " + string.Join(", ", report));
    }

    // ─────────────────────────────────────────────────────────────────────
    // 프리팹 조립
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 사수 화살 — 꼬리(사수-2)를 그림 자식(Visual) 밑에 붙이고, 명중·사거리 끝 이펙트(사수-3)를 연결한다.
    ///
    /// 꼬리를 Visual 밑에 두는 이유: 화살 그림은 판정보다 활 높이만큼 떠서 난다(Projectile.SetVisualLift).
    /// 루트에 붙이면 꼬리만 발치 높이로 따로 흐른다.
    /// 명중 이펙트 칸에는 원본(플레이어 화살)을 복제할 때 딸려 온 플레이어 명중 불꽃이 들어 있어서, 그것이면 바꾼다.
    /// </summary>
    private static void EnsureMarksmanArrow(GameObject arrowBreak, List<string> report)
    {
        EditPrefab(MarksmanArrowPath, root =>
        {
            bool changed = false;

            Transform visual = root.transform.Find("Visual");
            if (visual == null)
            {
                Debug.LogWarning("[몬스터 파티클] 사수 화살에 Visual 자식이 없다. Tools → 재의 길 → 프리팹 → 잿불 사수 화살 프리팹 생성 을 먼저 " +
                                 "돌려야 화살 그림이 활 높이로 뜬다. 꼬리는 일단 루트에 붙인다.");
                visual = root.transform;
            }

            // 화살 피벗이 촉 끝이라 0.8유닛 뒤(화살대 쪽)에서 뿌린다.
            changed |= EnsureChild(visual, "ArrowTrail", new Vector2(-0.8f, 0f), true, BuildMarksmanArrowTrail,
                                   "사수-2 화살 불티 꼬리 → AshMarksmanArrow/" + visual.name + "/ArrowTrail", report);

            var projectile = root.GetComponent<Projectile>();
            if (projectile != null && arrowBreak != null)
            {
                var serialized = new SerializedObject(projectile);
                changed |= ReplaceEffect(serialized, "impactEffectPrefab", arrowBreak, "사수-3 명중 → ArrowAshBreak", report);
                changed |= ReplaceEffect(serialized, "expireEffectPrefab", arrowBreak, "사수-3 사거리 끝 → ArrowAshBreak", report);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        });
    }

    /// <summary>
    /// 자폭병 폭발 — 파편·재(자폭병-3)와 폭발 뒤 잔불(자폭병-4). 폭발 그림의 자식이라 터지는 순간 같이 시작한다.
    /// 실행 중에 이 이펙트는 1.8배로 커지지만(EnemyBomber.explosionEffectScale) Scaling Mode가 Local이라 불티 크기와 원 크기는
    /// 그대로다. 위치만 배율을 받으므로 파편이 나오는 높이 0.55는 실제로 약 1유닛이 된다.
    /// </summary>
    private static void EnsureBlast(List<string> report)
    {
        EditPrefab(BlastPath, root =>
        {
            bool changed = false;
            changed |= EnsureChild(root.transform, "BlastDebris", new Vector2(0f, 0.55f), true, BuildBlastDebris,
                                   "자폭병-3 폭발 파편 → BomberBlast/BlastDebris", report);
            changed |= EnsureChild(root.transform, "BlastAsh", new Vector2(0f, 0.55f), true, BuildBlastAsh,
                                   "자폭병-3 폭발 재 → BomberBlast/BlastAsh", report);
            changed |= EnsureChild(root.transform, "BlastCinders", Vector2.zero, true, BuildBlastCinders,
                                   "자폭병-4 폭발 뒤 잔불 → BomberBlast/BlastCinders", report);
            return changed;
        });
    }

    /// <summary>망령 — 돌진 길 불씨 자식(망령-2)과 예비동작 모으기(망령-1)·사망 재(공통-1) 연결.</summary>
    private static void EnsureWraith(GameObject windup, GameObject deathAsh, List<string> report)
    {
        EditPrefab(WraithPath, root =>
        {
            bool changed = EnsureChild(root.transform, "ChargeTrail", new Vector2(0f, 0.1f), false, BuildChargeTrail,
                                       "망령-2 돌진 길 불씨 → AshEmberWraith/ChargeTrail", report);

            var wraith = root.GetComponent<EnemyWraith>();
            if (wraith == null)
            {
                Debug.LogWarning("[몬스터 파티클] AshEmberWraith에 EnemyWraith가 없다.");
                return changed;
            }

            var serialized = new SerializedObject(wraith);
            changed |= SetIfEmpty(serialized, "chargeTrail", root.transform.Find("ChargeTrail")?.GetComponent<ParticleSystem>(), "망령 Charge Trail 연결", report);
            changed |= SetIfEmpty(serialized, "windupEffectPrefab", windup, "망령 Windup Effect 연결", report);
            changed |= SetIfEmpty(serialized, "deathEffectPrefab", deathAsh, "망령 Death Effect 연결", report);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        });
    }

    /// <summary>사수 — 조준 모으기(사수-1)·사망 재(공통-1) 연결. 몸에 붙는 파티클은 없다.</summary>
    private static void EnsureMarksman(GameObject aim, GameObject deathAsh, List<string> report)
    {
        EditPrefab(MarksmanPath, root =>
        {
            var marksman = root.GetComponent<EnemyMarksman>();
            if (marksman == null)
            {
                Debug.LogWarning("[몬스터 파티클] AshMarksman에 EnemyMarksman이 없다.");
                return false;
            }

            var serialized = new SerializedObject(marksman);
            bool changed = SetIfEmpty(serialized, "aimEffectPrefab", aim, "사수 Aim Effect 연결", report);
            changed |= SetIfEmpty(serialized, "deathEffectPrefab", deathAsh, "사수 Death Effect 연결", report);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        });
    }

    /// <summary>
    /// 자폭병 — 경고 링(자폭병-1)·도화선 불똥(자폭병-2) 자식과 사망 재(공통-1) 연결.
    /// 두 자식은 평소에 멈춰 있고 EnemyBomber가 점화할 때 재생한다(Play On Awake 끔).
    /// </summary>
    private static void EnsureBomber(GameObject deathAsh, List<string> report)
    {
        EditPrefab(BomberPath, root =>
        {
            bool changed = EnsureChild(root.transform, "FuseRing", Vector2.zero, false, BuildFuseRing,
                                       "자폭병-1 점화 경고 링 → AshBomber/FuseRing", report);

            // 머리의 도화선 높이. 목업(발에서 5유닛)과 같다. 어긋나면 이 자식의 위치를 옮긴다.
            changed |= EnsureChild(root.transform, "FuseSparks", new Vector2(0f, 5f), false, BuildFuseSparks,
                                   "자폭병-2 도화선 불똥 → AshBomber/FuseSparks", report);

            var bomber = root.GetComponent<EnemyBomber>();
            if (bomber == null)
            {
                Debug.LogWarning("[몬스터 파티클] AshBomber에 EnemyBomber가 없다.");
                return changed;
            }

            var serialized = new SerializedObject(bomber);
            changed |= SetIfEmpty(serialized, "fuseRing", root.transform.Find("FuseRing")?.GetComponent<ParticleSystem>(), "자폭병 Fuse Ring 연결", report);
            changed |= SetIfEmpty(serialized, "fuseSparks", root.transform.Find("FuseSparks")?.GetComponent<ParticleSystem>(), "자폭병 Fuse Sparks 연결", report);
            changed |= SetIfEmpty(serialized, "deathEffectPrefab", deathAsh, "자폭병 Death Effect 연결", report);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        });
    }

    /// <summary>프리팹을 열어 edit이 true를 돌려주면 저장한다. 사람이 고친 값이 남도록 새로 굽지 않고 더한다.</summary>
    private static void EditPrefab(string path, Func<GameObject, bool> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogWarning($"[몬스터 파티클] 프리팹을 못 열었다: {path} — 건너뛴다.");
            return;
        }

        try
        {
            if (edit(root)) PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// 이름으로 찾아 없을 때만 파티클 자식을 만든다. worldOffset은 부모 기준 거리(유닛)라 부모 배율로 나눠 넣는다.
    /// garnish면 <see cref="ParticleGarnish"/>를 붙여 부모 이펙트가 끝날 때 떼어 낼 수 있게 한다.
    /// </summary>
    private static bool EnsureChild(Transform parent, string name, Vector2 worldOffset, bool garnish,
                                    Action<ParticleSystem> build, string label, List<string> report)
    {
        if (parent.Find(name) != null) return false;

        Vector3 scale = parent.lossyScale;
        var local = new Vector3(worldOffset.x / SafeScale(scale.x), worldOffset.y / SafeScale(scale.y), 0f);

        ParticleSystem particles = CreateChildSystem(parent, name, local);
        if (garnish) particles.gameObject.AddComponent<ParticleGarnish>();
        build(particles);

        report.Add(label);
        return true;
    }

    /// <summary>참조 칸이 비어 있을 때만 채운다. 사람이 다른 것을 꽂아 뒀으면 그대로 둔다.</summary>
    private static bool SetIfEmpty(SerializedObject target, string property, Object value, string label, List<string> report)
    {
        SerializedProperty field = target.FindProperty(property);
        if (field == null || value == null || field.objectReferenceValue != null) return false;

        field.objectReferenceValue = value;
        report.Add(label);
        return true;
    }

    /// <summary>
    /// 이펙트 칸이 비었거나 플레이어 화살의 명중 불꽃(복제 때 딸려 온 것)이면 바꾼다. 사람이 다른 것을 꽂아 뒀으면 그대로 둔다.
    /// </summary>
    private static bool ReplaceEffect(SerializedObject target, string property, GameObject value, string label, List<string> report)
    {
        SerializedProperty field = target.FindProperty(property);
        if (field == null) return false;

        var playerImpact = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerArrowImpactPath);
        Object current = field.objectReferenceValue;
        if (current != null && current != playerImpact) return false;
        if (current == value) return false;

        field.objectReferenceValue = value;
        report.Add(label);
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 곁들임 하나하나 — 수치는 기획 페이지의 목업과 같다(개수에 양을 곱한다)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>적 불티 — 흰 심 없이 황금 → 주황 → 적갈 → 재. 플레이어 불티(흰 심부터)와 한눈에 구별된다.</summary>
    private static GradientColorKey[] EnemyColors()
    {
        return Colors((Amber, 0f), (Ember, 0.3f), (Deep, 0.7f), (Ash, 1f));
    }

    /// <summary>모이는 불씨 — 가운데로 갈수록 밝아진다(적갈 → 주황 → 황금). 흰 심까지는 안 간다.</summary>
    private static GradientColorKey[] GatherColors()
    {
        return Colors((Deep, 0f), (Ember, 0.5f), (Amber, 1f));
    }

    /// <summary>
    /// 모으기 파티클 공통. 원 둘레(반지름 spawnRadius)에서 가운데로 빨려 들어 가운데에 닿는 순간 수명이 끝난다
    /// (플레이어 R-1과 같은 계산 — 수명·속도를 상수로 둬서 가운데를 지나쳐 튀어나가지 않는다).
    /// </summary>
    private static void BuildGather(GameObject root, float duration, float spawnRadius, float lifetime,
                                    float rate, float sizeMin, float sizeMax, int maxParticles)
    {
        const float endRadius = 0.2f;

        ParticleSystem particles = AddSystem(root);

        var main = particles.main;
        main.duration = duration;
        main.startLifetime = lifetime;
        main.startSpeed = -(spawnRadius - endRadius) / lifetime;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.maxParticles = maxParticles;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        emission.rateOverTime = rate;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = spawnRadius;
        shape.radiusThickness = 0f;
        shape.arc = 360f;

        SetColor(particles, GatherColors(), FadeAlpha(1f, 0.35f, 1f));
        SetRenderer(particles, "VFX", 2, stretch: 0.03f);
    }

    /// <summary>망령-1 예비동작 불씨(독립). 예비동작 0.4초 중 0.35초 동안 반지름 3.2에서 앞발로 모인다.</summary>
    private static void BuildWindupGather(GameObject root)
    {
        BuildGather(root, duration: 0.35f, spawnRadius: 3.2f, lifetime: 0.27f,
                    rate: 75f * Amount, sizeMin: 0.16f, sizeMax: 0.24f, maxParticles: 60);
    }

    /// <summary>사수-1 조준 불씨(독립). 조준 0.55초 중 0.5초 동안 반지름 1.7에서 화살촉 자리로 모인다.</summary>
    private static void BuildAimGather(GameObject root)
    {
        BuildGather(root, duration: 0.5f, spawnRadius: 1.7f, lifetime: 0.25f,
                    rate: 50f * Amount, sizeMin: 0.12f, sizeMax: 0.18f, maxParticles: 40);
    }

    /// <summary>
    /// 사수-3 화살 재 부서짐(독립). 루트가 재(수명이 긴 쪽 — Stop Action이 루트 기준이라), 자식이 뒤로 튀는 불똥이다.
    /// 화살 방향으로 돌려 놓이므로 불똥은 날아온 쪽(-X)으로 튄다.
    /// </summary>
    private static void BuildArrowBreak(GameObject root)
    {
        ParticleSystem ash = AddSystem(root);

        var main = ash.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.26f);
        main.startRotation = RandomRotation();
        main.maxParticles = 20;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(ash, 0f, 8);
        SetDrag(ash, 2.5f);
        SetColor(ash, AshColors(), FadeAlpha(0.8f, 0f, 0.55f));
        SetRenderer(ash, "VFX", 2);

        ParticleSystem sparks = CreateChildSystem(root.transform, "Sparks", Vector3.zero);

        var sparkMain = sparks.main;
        sparkMain.duration = 0.1f;
        sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
        sparkMain.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
        sparkMain.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.14f);
        sparkMain.gravityModifier = 1.5f;
        sparkMain.maxParticles = 20;

        Burst(sparks, 0f, 5);

        // 뒤쪽(-X)을 가운데로 ±60도. 원 셰이프의 호는 +X에서 반시계로 도므로 120도 돌리면 120~240도가 된다.
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.1f;
        shape.radiusThickness = 1f;
        shape.arc = 120f;
        shape.rotation = new Vector3(0f, 0f, 120f);

        SetDrag(sparks, 2f);
        SetColor(sparks, EnemyColors(), FadeAlpha(1f, 0f, 0.55f));
        SetRenderer(sparks, "VFX", 3, stretch: 0.03f);
    }

    /// <summary>
    /// 공통-1 사망 재 흩날림(독립). 사망 모션(0.5초)이 무너지기 시작할 때(0.15초)부터 약 1초 동안 몸 둘레에서 재가 떠올라
    /// 한쪽으로 흩날린다. 다섯 개 중 하나는 불씨다. 시체는 1.1초 뒤 풀로 돌아가지만 이 이펙트는 월드에 따로 남는다.
    /// </summary>
    private static void BuildDeathAsh(GameObject root)
    {
        ParticleSystem particles = AddSystem(root);

        var main = particles.main;
        main.duration = 0.95f;
        main.startDelay = 0.15f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
        main.startRotation = RandomRotation();
        main.startColor = PickOne(AshLight, 0.8f, new Color(1f, 0.55f, 0.18f));
        main.maxParticles = 200;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        emission.rateOverTime = 75f * Amount;

        // 몸 둘레(가로 4.4 × 높이 2) — 몸 콜라이더가 발치에 있어서 그림 높이로 올린다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(4.4f, 2f, 0f);
        shape.position = new Vector3(0f, 1.2f, 0f);

        SetLinearVelocity(particles, ParticleSystemSimulationSpace.World,
                          new Vector2(0.4f, 0.8f), new Vector2(1.6f, 2.2f));

        SetColor(particles, Colors((Color.white, 0f), (new Color(0.6f, 0.6f, 0.6f), 1f)), FadeAlpha(0.9f, 0.15f, 0.5f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>망령-2 돌진 길 불씨(망령 자식). 평소에는 방출이 꺼져 있고 EnemyWraith가 돌진 동안만 켠다. 거리 기준 방출.</summary>
    private static void BuildChargeTrail(ParticleSystem particles)
    {
        var main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
        main.startRotation = RandomRotation();
        main.maxParticles = 100;
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;

        var emission = particles.emission;
        emission.rateOverDistance = 1.2f * Amount;
        emission.enabled = false;

        // 망령 몸 폭(콜라이더 4)의 절반 남짓으로 흩는다.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(2f, 0.8f, 0f);

        SetColor(particles, EnemyColors(), FlickerAlpha(0f, 0.4f));
        SetRenderer(particles, "Decal", 0);
    }

    /// <summary>사수-2 화살 불티 꼬리(사수 화살의 Visual 자식). 플레이어 W-1보다 가늘고 붉다. 화살이 사라지면 떼어진다.</summary>
    private static void BuildMarksmanArrowTrail(ParticleSystem particles)
    {
        var main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.15f);
        main.startRotation = RandomRotation();
        main.gravityModifier = 0.2f;
        main.maxParticles = 150;
        main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        emission.rateOverDistance = 4f * Amount;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.3f, 0.2f, 0f);

        SetLinearVelocity(particles, ParticleSystemSimulationSpace.Local,
                          new Vector2(-1f, -0.3f), new Vector2(-0.2f, 0.4f));

        SetColor(particles, EnemyColors(), FlickerAlpha(0f, 0.55f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>
    /// 점화 파티클 공통 — 평소에 멈춰 있다가 EnemyBomber가 점화할 때 재생한다. 방출량이 시스템 길이(= 점화 시간, 실행 중에
    /// EnemyBomber가 맞춘다) 동안 1/4에서 최대까지 늘어난다. 터질 때가 가까울수록 촘촘해지는 것이 예고다.
    /// </summary>
    private static void SetFuseTiming(ParticleSystem particles, float maxRate)
    {
        var main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.667f;

        var emission = particles.emission;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(maxRate, Curve((0f, 0.25f), (1f, 1f)));
    }

    /// <summary>
    /// 자폭병-1 점화 경고 링(자폭병 자식). 폭발 반경 원의 둘레에 불씨가 깔린다. 반지름은 실행 중에 EnemyBomber가
    /// explosionRadius로 넣는다 — 여기 6은 편집기에서 볼 때의 값이다. 바닥 표시라 캐릭터 아래(Decal)에 그린다.
    /// </summary>
    private static void BuildFuseRing(ParticleSystem particles)
    {
        SetFuseTiming(particles, 200f * Amount);

        var main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.22f);
        main.startRotation = RandomRotation();
        main.maxParticles = 200;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = BlastRadius;
        shape.radiusThickness = 0f;
        shape.arc = 360f;

        SetColor(particles, EnemyColors(), FlickerAlpha(0.2f, 0.6f));
        SetRenderer(particles, "Decal", 1);
    }

    /// <summary>
    /// 자폭병-2 도화선 불똥(자폭병 자식, 머리 높이). 위쪽 100도로 튀어 떨어진다. 곧 터질 불이라 적 불티 중 유일하게 흰 심부터다.
    /// </summary>
    private static void BuildFuseSparks(ParticleSystem particles)
    {
        SetFuseTiming(particles, 100f * Amount);

        var main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.15f);
        main.gravityModifier = 1.43f;
        main.maxParticles = 80;

        // 위(90도)를 가운데로 ±50도 — 호는 +X에서 반시계로 도므로 40도 돌리면 40~140도.
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f;
        shape.radiusThickness = 1f;
        shape.arc = 100f;
        shape.rotation = new Vector3(0f, 0f, 40f);

        SetDrag(particles, 1.5f);
        SetColor(particles, EmberColors(), FadeAlpha(1f, 0f, 0.55f));
        SetRenderer(particles, "VFX", 2, stretch: 0.03f);
    }

    /// <summary>자폭병-3 폭발 파편(BomberBlast 자식). 돌 조각이 사방으로 반경 근처까지 날아가 식는다.</summary>
    private static void BuildBlastDebris(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 13f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startRotation = RandomRotation();
        main.maxParticles = 40;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(particles, 0f, 22);

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.5f;
        shape.radiusThickness = 1f;
        shape.arc = 360f;

        // 속도 8~13에 저항 2.2면 반경 6 안팎에서 멈춘다(목업과 같은 값).
        SetDrag(particles, 2.2f);
        SetColor(particles, StoneColors(), FadeAlpha(1f, 0f, 0.7f));
        SetRenderer(particles, "VFX", 1);
    }

    /// <summary>자폭병-3 폭발 재(BomberBlast 자식). 반투명 재가 파편보다 느리게 퍼진다. 가장 뒤에 그린다.</summary>
    private static void BuildBlastAsh(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        main.startRotation = RandomRotation();
        main.maxParticles = 50;
        main.stopAction = ParticleSystemStopAction.Destroy;

        Burst(particles, 0f, 26);

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.5f;
        shape.radiusThickness = 1f;
        shape.arc = 360f;

        SetDrag(particles, 2.5f);
        SetColor(particles, AshColors(), FadeAlpha(0.7f, 0f, 0.35f));
        SetRenderer(particles, "VFX", 0);
    }

    /// <summary>
    /// 자폭병-4 폭발 뒤 잔불(BomberBlast 자식). 터지고 0.25초 뒤부터 0.5초 동안 반경 안 바닥에 불씨가 남는다.
    /// 폭발 그림(0.5초)이 끝나도 ParticleGarnish가 떼어 내므로 끝까지 나온다.
    /// </summary>
    private static void BuildBlastCinders(ParticleSystem particles)
    {
        var main = particles.main;
        main.duration = 0.5f;
        main.startDelay = 0.25f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.28f);
        main.startRotation = RandomRotation();
        main.maxParticles = 80;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        emission.rateOverTime = 50f * Amount;

        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = BlastRadius;
        shape.radiusThickness = 1f;
        shape.arc = 360f;

        SetColor(particles, EnemyColors(), FlickerAlpha(0.1f, 0.4f));
        SetRenderer(particles, "Decal", 1);
    }
}
