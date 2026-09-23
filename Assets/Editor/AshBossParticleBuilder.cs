using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static AshPlayerParticleBuilder;

/// <summary>
/// 추가 생성(2026-09-22, 보스 파티클 기획 1부 "왕관 의식") — 보스·떠 있는 유물 프리팹에 의식 파티클을 달고, 의식 자세 그림을 꽂는다.
///
/// 메뉴: Tools → 재의 길 → 파티클 → 보스 파티클 만들기 (프리팹만 고친다 — 씬을 열 필요 없다)
///
/// 기획 페이지 https://claude.ai/artifact/9V88qXmetQRfg4KiqoEkbW (DB decisions/boss-particles, 09-22 확정):
/// 18개 전부 넣기, 양 화려하게(개수 ×1.5 — <see cref="AshPlayerParticleBuilder.Count"/>와 같은 배율), 색 적 규칙(흰 심 없이 황금부터).
/// 이 도구는 1부(의식 동작 4 + 의식 파티클 7)만 만든다. 2부(보스 평소 7)는 다음에 여기에 더한다.
///
/// <b>이미 있으면 건드리지 않는다.</b> 자식은 이름으로 찾아 없을 때만 만들고, 참조 칸은 비어 있을 때만 채운다
/// (몬스터 파티클 도구와 같은 규칙 — 인스펙터에서 고친 값이 다시 돌려도 날아가지 않는다).
/// 주의: <c>프리팹 → 보스 프리팹 생성</c>은 보스 프리팹을 새로 구워서 이 자식들이 빠진다. 그 뒤에는 이 메뉴를 다시 돌린다.
/// </summary>
public static class AshBossParticleBuilder
{
    private const string BossPrefabPath = "Assets/Project/Prefabs/Enemies/BossAshKing.prefab";
    private const string RelicPrefabPath = "Assets/Project/Prefabs/VFX/FlyingRelic.prefab";
    private const string SheetFolder = "Assets/Project/Art/Characters/Boss/AshKing/";

    /// <summary>개수가 아닌 초당 방출량에 곱할 양 배율. Count()가 쓰는 1.5와 같다(기획 "화려하게").</summary>
    private const float RateAmount = 1.5f;

    [MenuItem("Tools/재의 길/파티클/보스 파티클 만들기")]
    public static void Build()
    {
        var report = new List<string>();
        EditPrefab(BossPrefabPath, root => EnsureBoss(root, report));
        EditPrefab(RelicPrefabPath, root => EnsureRelic(root, report));
        AssetDatabase.SaveAssets();

        Debug.Log(report.Count == 0
            ? "[보스 파티클] 이미 다 있다. 바꾼 것 없음."
            : $"[보스 파티클] {report.Count}개를 더했다:\n- " + string.Join("\n- ", report) +
              "\n플레이해서 왕관 의식(2페이즈 체력 35%)을 본다. 수치는 보스 프리팹 자식 파티클 인스펙터에서 조정한다.");
    }

    /// <summary>보스 프리팹 — 의식 파티클 자식 7개와 의식 자세 그림 4가지.</summary>
    private static bool EnsureBoss(GameObject root, List<string> report)
    {
        var boss = root.GetComponent<EnemyBoss>();
        if (boss == null)
        {
            Debug.LogWarning("[보스 파티클] 보스 프리팹에 EnemyBoss가 없다. 건너뛴다.");
            return false;
        }

        var so = new SerializedObject(boss);
        bool changed = false;

        // 자리는 발밑 기준(유닛, 오른쪽을 볼 때). 왼쪽을 보면 EnemyBoss.FaceChild가 좌우를 뒤집는다.
        changed |= Child(root.transform, "RitualTether", Vector2.zero, BuildTether, so, "ritualTether", "의식-1 손 뻗기 줄기", report);
        changed |= Child(root.transform, "CrownFire", new Vector2(1.25f, 5.4f), BuildCrownFire, so, "crownFire", "의식-2 왕관 점화", report);
        changed |= Child(root.transform, "AshVortex", new Vector2(0f, 0.2f), BuildVortex, so, "ashVortex", "의식-3 재 소용돌이", report);
        changed |= Child(root.transform, "RitualChestSparks", new Vector2(0f, 4.2f), BuildChestSparks, so, "ritualChestSparks", "의식-5 가슴 불꽃", report);
        changed |= Child(root.transform, "CollapseAsh", new Vector2(1.25f, 5.4f), BuildCollapseAsh, so, "collapseAsh", "의식-6 무너짐 재", report);
        changed |= Child(root.transform, "DizzyEmbers", new Vector2(0.2f, 5.6f), BuildDizzyEmbers, so, "dizzyEmbers", "의식-6 맴도는 불씨", report);
        changed |= Child(root.transform, "RitualBlast", new Vector2(0f, 0.2f), BuildBlast, so, "ritualBlast", "의식-7 충격파", report);

        // 자세 그림 — 새 그림 없이 지금 시트에서 한 장씩(이름은 보스 슬라이서가 붙인 것).
        changed |= SpriteField(so, "ritualReachSprite", "ash-king-phase2-ultimate.png", "ashking2_ultimate_03", "동작-1 손 뻗기", report);
        changed |= SpriteField(so, "ritualHoldSprite", "ash-king-phase2-slam.png", "ashking2_slam_02", "동작-2 의식 자세", report);
        changed |= SpriteField(so, "groggySprite", "ash-king-phase2-hit-death.png", "ashking2_death_00", "동작-4 무너짐", report);
        changed |= FlinchSprites(so, report);

        so.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    /// <summary>떠 있는 유물 프리팹 — 의식-4 실(끊길 때의 의식-5 불티도 이 시스템이 뿌린다).</summary>
    private static bool EnsureRelic(GameObject root, List<string> report)
    {
        var relic = root.GetComponent<FlyingRelic>();
        if (relic == null)
        {
            Debug.LogWarning("[보스 파티클] 유물 프리팹이 없거나 FlyingRelic이 없다. 씬·세팅 → 왕관 의식 구성 을 먼저 돌려라.");
            return false;
        }

        var so = new SerializedObject(relic);
        bool changed = Child(root.transform, "RelicThread", Vector2.zero, BuildThread, so, "thread", "의식-4 유물 실", report);
        so.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    // ── 파티클 하나하나 — 수치는 기획 페이지의 목업과 같다 ───────────────────────────

    /// <summary>적 불티 — 흰 심 없이 황금 → 주황 → 적갈 → 재(몬스터 파티클과 같은 색 규칙).</summary>
    private static GradientColorKey[] EnemyColors() => Colors((Amber, 0f), (Ember, 0.3f), (Deep, 0.7f), (Ash, 1f));

    /// <summary>
    /// 의식-1 줄기. 방출은 코드(EnemyBoss)가 EmitParams로 직접 한다 — 시작점(플레이어 가슴)과 끝점(보스 손)이 매 프레임 달라서
    /// 모양(shape)으로는 못 맞춘다. 여기서는 크기·색만 정하고, 늘 켜 둔 채(방출 0) 기다린다.
    /// </summary>
    private static void BuildTether(ParticleSystem ps)
    {
        CodeDriven(ps, 0.42f, 0.18f, 0.3f, 500);
        SetColor(ps, EnemyColors(), FadeAlpha(1f, 0f, 0.6f));
        SetRenderer(ps, "VFX", 3);
    }

    /// <summary>의식-2 왕관 점화. 가시 폭(2.4유닛)에서 위로 피어오른다. 방출량은 EnemyBoss가 의식 진행에 맞춰 4.5배까지 올린다.</summary>
    private static void BuildCrownFire(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
        main.startRotation = RandomRotation();
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = 10f * RateAmount;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(2.4f, 0.4f, 0f);

        Velocity(ps, new ParticleSystem.MinMaxCurve(-0.4f, 0.4f), new ParticleSystem.MinMaxCurve(1.5f, 3.2f));
        SetColor(ps, EnemyColors(), FadeAlpha(1f, 0.1f, 0.6f));
        SetRenderer(ps, "VFX", 2);
    }

    /// <summary>
    /// 의식-3 재 소용돌이. 발밑 둘레(반지름 4.5)에서 태어나 보스 둘레를 돌며 안으로 말려 들고 조금 떠오른다.
    /// 도는 운동(orbital)은 시스템 중심 기준이라 로컬 공간에서 돌린다(보스는 의식 동안 제자리다).
    /// </summary>
    private static void BuildVortex(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.34f);
        main.startRotation = RandomRotation();
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.rateOverTime = 34f * RateAmount;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 4.5f;
        shape.radiusThickness = 0.25f;
        shape.scale = new Vector3(1f, 0.45f, 1f);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.8f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(2.6f);
        velocity.radial = new ParticleSystem.MinMaxCurve(-1.2f);

        SetColor(ps, AshColors(), FadeAlpha(0.9f, 0.2f, 0.6f));
        SetRenderer(ps, "Decal", 1);
    }

    /// <summary>의식-5 가슴 불꽃(한 번 터짐). 유물이 깨질 때마다, 그로기 중 맞을 때마다 튄다.</summary>
    private static void BuildChestSparks(ParticleSystem ps)
    {
        OneShot(ps, 16, 0.25f, 0.45f, 2f, 6f, 0.18f, 0.28f, 0.3f);
        SetDrag(ps, 2f);
        SetColor(ps, EnemyColors(), FadeAlpha(1f, 0f, 0.5f));
        SetRenderer(ps, "VFX", 3);
    }

    /// <summary>의식-6 무너짐 재(한 번 터짐). 왕관 자리에서 재가 쏟아져 내린다.</summary>
    private static void BuildCollapseAsh(ParticleSystem ps)
    {
        OneShot(ps, 46, 0.8f, 1.3f, 0.5f, 2.5f, 0.2f, 0.34f, 1.2f);
        var main = ps.main;
        main.gravityModifier = 0.6f;
        SetColor(ps, AshColors(), FadeAlpha(1f, 0f, 0.6f));
        SetRenderer(ps, "VFX", 2);
    }

    /// <summary>의식-6 맴도는 불씨. 그로기 동안 머리 위를 돈다(로컬 공간 orbital).</summary>
    private static void BuildDizzyEmbers(ParticleSystem ps)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 1.2f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.24f);
        main.maxParticles = 30;

        var emission = ps.emission;
        emission.rateOverTime = 3f * RateAmount;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1.2f;
        shape.radiusThickness = 0f;
        shape.scale = new Vector3(1f, 0.35f, 1f);

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f);
        velocity.orbitalZ = new ParticleSystem.MinMaxCurve(3.2f);

        SetColor(ps, EnemyColors(), FlickerAlpha(0.1f, 0.7f));
        SetRenderer(ps, "VFX", 3);
    }

    /// <summary>의식-7 충격파(한 번 터짐). 발밑 둘레에서 바깥으로 빠르게 퍼져 방 끝(반지름 약 20)에서 멈춘다.</summary>
    private static void BuildBlast(ParticleSystem ps)
    {
        OneShot(ps, 140, 0.45f, 0.6f, 34f, 40f, 0.25f, 0.4f, 1f);
        var shape = ps.shape;
        shape.radiusThickness = 0f;
        shape.scale = new Vector3(1f, 0.5f, 1f);
        var main = ps.main;
        main.maxParticles = 400;
        SetDrag(ps, 1.5f);
        SetColor(ps, EnemyColors(), FadeAlpha(1f, 0f, 0.5f));
        SetRenderer(ps, "VFX", 3);
    }

    /// <summary>의식-4 유물 실. 방출은 FlyingRelic이 EmitParams로 직접 한다(유물 → 보스 가슴, 1.2초).</summary>
    private static void BuildThread(ParticleSystem ps)
    {
        CodeDriven(ps, 1.2f, 0.14f, 0.22f, 300);
        SetColor(ps, EnemyColors(), FadeAlpha(1f, 0.05f, 0.75f));
        SetRenderer(ps, "VFX", 2);
    }

    // ── 도우미 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 코드가 Emit으로 뿌리는 시스템. 늘 켜 둔 채(방출 0) 기다린다 — 멈춰 있는 시스템에 Emit하면 입자가 안 움직인다.
    /// </summary>
    private static void CodeDriven(ParticleSystem ps, float lifetime, float sizeMin, float sizeMax, int maxParticles)
    {
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 1f;
        main.startLifetime = lifetime;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = RandomRotation();
        main.maxParticles = maxParticles;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.enabled = false;
    }

    /// <summary>한 번 터지는 시스템. 개수에는 양 배율이 붙는다(Burst → Count).</summary>
    private static void OneShot(ParticleSystem ps, int count, float lifeMin, float lifeMax, float speedMin, float speedMax,
                                float sizeMin, float sizeMax, float radius)
    {
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = RandomRotation();
        main.maxParticles = Mathf.Max(100, Count(count) * 2);

        Burst(ps, 0f, count);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius;
        shape.radiusThickness = 1f;
    }

    /// <summary>월드 공간 직선 속도(x·y). z는 0 — 세 축을 같은 모드로 맞춰야 유니티가 받아 준다.</summary>
    private static void Velocity(ParticleSystem ps, ParticleSystem.MinMaxCurve x, ParticleSystem.MinMaxCurve y)
    {
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = x;
        velocity.y = y;
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
    }

    /// <summary>
    /// 이름으로 찾아 없을 때만 파티클 자식을 만들고, 참조 칸이 비어 있으면 채운다. worldOffset은 부모 배율로 나눠 넣는다.
    /// </summary>
    private static bool Child(Transform parent, string name, Vector2 worldOffset, Action<ParticleSystem> build,
                              SerializedObject owner, string property, string label, List<string> report)
    {
        bool changed = false;
        Transform existing = parent.Find(name);
        ParticleSystem ps = existing != null ? existing.GetComponent<ParticleSystem>() : null;

        if (existing == null)
        {
            Vector3 scale = parent.lossyScale;
            var local = new Vector3(worldOffset.x / SafeScale(scale.x), worldOffset.y / SafeScale(scale.y), 0f);
            ps = CreateChildSystem(parent, name, local);
            build(ps);
            report.Add(label);
            changed = true;
        }

        SerializedProperty field = owner.FindProperty(property);
        if (field != null && field.objectReferenceValue == null && ps != null)
        {
            field.objectReferenceValue = ps;
            changed = true;
        }

        return changed;
    }

    /// <summary>시트에서 이름이 같은 스프라이트를 찾아 비어 있는 칸에 넣는다.</summary>
    private static bool SpriteField(SerializedObject owner, string property, string sheet, string spriteName, string label, List<string> report)
    {
        SerializedProperty field = owner.FindProperty(property);
        if (field == null || field.objectReferenceValue != null) return false;

        Sprite sprite = FindSprite(sheet, spriteName);
        if (sprite == null)
        {
            Debug.LogWarning($"[보스 파티클] {sheet}에서 {spriteName}을 못 찾았다 — {label}이 빠진다.");
            return false;
        }

        field.objectReferenceValue = sprite;
        report.Add(label);
        return true;
    }

    /// <summary>동작-3 움찔 — 피격 두 장. 비어 있을 때만 채운다.</summary>
    private static bool FlinchSprites(SerializedObject owner, List<string> report)
    {
        SerializedProperty field = owner.FindProperty("ritualFlinchSprites");
        if (field == null || field.arraySize > 0) return false;

        Sprite[] sprites = { FindSprite("ash-king-phase2-hit-death.png", "ashking2_hit_00"), FindSprite("ash-king-phase2-hit-death.png", "ashking2_hit_01") };
        if (sprites.Any(s => s == null))
        {
            Debug.LogWarning("[보스 파티클] 피격 장을 못 찾았다 — 동작-3 움찔이 빠진다.");
            return false;
        }

        field.arraySize = sprites.Length;
        for (int i = 0; i < sprites.Length; i++) field.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        report.Add("동작-3 움찔");
        return true;
    }

    private static Sprite FindSprite(string sheet, string spriteName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(SheetFolder + sheet).OfType<Sprite>().FirstOrDefault(s => s.name == spriteName);
    }

    private static void EditPrefab(string path, Func<GameObject, bool> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogWarning($"[보스 파티클] 프리팹을 못 열었다: {path} — 건너뛴다.");
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
}
