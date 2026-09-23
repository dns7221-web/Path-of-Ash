using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 추가 생성(2026-09-22, 보스 궁극기 "왕관 의식") — 보스 방에 왕관 의식을 꾸린다.
///
/// 메뉴: Tools → 재의 길 → 씬·세팅 → 왕관 의식 구성
///
/// 하는 일은 셋이다.
/// <list type="number">
/// <item>날아가는 유물 프리팹(<see cref="FlyingRelic"/>)을 만든다 — 바닥 그림자, 떠오르는 몸(아이콘·꼬리·빛).
/// 수정(2026-09-22) — 공격에 맞는 부품(Health·트리거 판정·Enemy 레이어)도 단다. 이미 있는 프리팹에는 빠진 것만 더한다.</item>
/// <item>보스 방의 <see cref="BossEncounter"/> 옆에 <see cref="CrownRitual"/>을 붙이고, 방 안쪽 네 귀퉁이에
/// 유물이 내려앉을 자리를 만든다.</item>
/// <item>보스 열쇠 유물 네 개, 자리, 유물 프리팹, 뽑히는 순간의 불꽃을 의식에 꽂는다.</item>
/// </list>
///
/// <b>이미 있으면 다시 만들지 않고 빈 칸만 채운다.</b> 다른 빌더와 같은 규칙이다. 자리를 손으로 옮기거나
/// 인스펙터에서 유물 크기·비행 시간을 고친 것이 다시 돌릴 때마다 날아가면 안 된다. 처음부터 다시 만들고
/// 싶으면 프리팹이나 <c>CrownRitualPoints</c>를 지우고 돌리면 된다.
///
/// <b>귀퉁이를 벽 콜라이더에서 구하는 이유.</b> 방 그림은 바뀔 수 있고(보스 방 그림이 한 번 바뀌었다),
/// 벽은 그림에 맞춰 다시 세운다. 좌표를 숫자로 박아 두면 그림이 바뀔 때 유물이 벽 속에 떨어진다.
/// 벽 안쪽 면은 <see cref="AshRoomWallBuilder"/>와 같은 계산(<c>InnerFace</c>)으로 구한다.
/// </summary>
public static class AshCrownRitualBuilder
{
    private const string RelicPrefabPath = "Assets/Project/Prefabs/VFX/FlyingRelic.prefab";
    private const string ShadowSpritePath = "Assets/Project/Art/Sprites/VFX/ground_shadow.png";
    private const string BurstPrefabPath = "Assets/Project/Prefabs/VFX/Particles/HitSparks.prefab";

    // 유물 뽑힘 클립. 이게 없으면 모션 없이 시간 기준으로만 유물이 튀어나온다(CrownRitual의 안전장치).
    private const string RelicTornClipPath = "Assets/Project/Animations/Player/Directional/RelicTorn_S.anim";

    private const string PointsRootName = "CrownRitualPoints";

    /// <summary>자리 이름. <see cref="CrownRitual"/>의 유물 순서(왼쪽 위, 오른쪽 위, 왼쪽 아래, 오른쪽 아래)와 같다.</summary>
    private static readonly string[] PointNames =
    {
        "RelicPoint_TopLeft", "RelicPoint_TopRight", "RelicPoint_BottomLeft", "RelicPoint_BottomRight",
    };

    /// <summary>
    /// 옆 벽에서 들이는 거리(유닛).
    ///
    /// 귀퉁이에 딱 붙이면 10초 안에 넷을 다 돌 수 없다. 보스 방 바닥은 약 54 x 27.5유닛이고 플레이어는
    /// 초당 14유닛을 걷는다. 이 값이면 좌우 자리 사이가 약 45유닛이라 네 곳을 걸어서만 도는 데 약 9.6초 —
    /// 대시를 섞어야 넷을 다 부술 수 있다. 기획에서 "넷을 다 부수면 보스 그로기"로 정한 보상이 잘한 판에만
    /// 나오게 하는 거리다. 숫자는 플레이하며 자리를 옮겨 맞춘다(이 도구는 이미 있는 자리를 안 옮긴다).
    /// </summary>
    private const float InsetSide = 4.5f;

    /// <summary>
    /// 위 벽에서 들이는 거리(유닛). 아래보다 큰 이유: 유물 그림이 바닥에서 1.2유닛 떠 있고 폭이 1.8유닛이라,
    /// 위 벽 가까이 두면 그림이 벽 그림 위로 올라가 방 밖에 떠 있는 것처럼 보인다.
    /// </summary>
    private const float InsetTop = 4f;

    /// <summary>아래 벽에서 들이는 거리(유닛).</summary>
    private const float InsetBottom = 3f;

    /// <summary>
    /// 그림자 폭(유닛). 아이콘(1.8)보다 조금 좁게 — 떠 있는 물건의 그림자는 몸보다 작아야 "떠 있다"로 읽힌다.
    /// </summary>
    private const float ShadowWidth = 1.5f;

    /// <summary>그림자의 납작한 정도. 접지 그림자 빌더(<see cref="AshGroundShadowBuilder"/>)와 같은 값이다.</summary>
    private const float ShadowHeightRatio = 0.38f;

    /// <summary>
    /// 프리팹에서 몸이 떠 있는 높이. <see cref="FlyingRelic"/>의 떠 있는 높이 기본값(1.2)과 같다.
    /// 실행 중에는 FlyingRelic이 매 프레임 다시 정하므로, 이 값은 프리팹을 열어 볼 때의 모양만 정한다.
    /// </summary>
    private const float PreviewHoverHeight = 1.2f;

    // 추가 생성(2026-09-22, 부수기) — 공격이 닿는 판정의 크기(유닛). 폭은 아이콘(1.8)과 같고, 높이는 바닥에서
    // 떠 있는 아이콘 꼭대기(떠 있는 높이 1.2 + 아이콘 반 0.9 ≈ 2.1)보다 조금 위까지 덮는다. 플레이어가 유물 옆에 서서
    // 휘둘러도, 아래에서 위로 휘둘러도 닿게 하려는 크기다.
    private const float HitBoxWidth = 1.8f;
    private const float HitBoxHeight = 2.4f;

    /// <summary>
    /// 추가 생성(2026-09-22) — 유물의 체력. 튜토리얼 허수아비와 같은 999다. 체력은 쓰지 않고 맞은 횟수만 세므로
    /// (<see cref="FlyingRelic"/> 주석), 세 대 안에 절대 0이 되지 않을 만큼이면 된다.
    /// </summary>
    private const int RelicHealth = 999;

    [MenuItem("Tools/재의 길/씬·세팅/왕관 의식 구성")]
    public static void Build()
    {
        RoomController bossRoom = FindBossRoom();
        if (bossRoom == null) return;

        var encounter = bossRoom.GetComponentInChildren<BossEncounter>(true);
        if (encounter == null)
        {
            Debug.LogError($"[왕관 의식] {bossRoom.name}에 BossEncounter가 없다. " +
                           "Tools → 재의 길 → 씬·세팅 → 보스 방 생성 을 먼저 실행해라.", bossRoom);
            return;
        }

        List<RelicData> relics = FindBossKeyRelics();
        if (relics.Count != PointNames.Length)
        {
            // 멈추지는 않는다. 모자라면 빈 자리에는 유물이 안 날아갈 뿐 의식은 돈다.
            Debug.LogWarning($"[왕관 의식] 보스 열쇠 유물이 {relics.Count}개다({PointNames.Length}개를 기대했다). " +
                             "있는 만큼만 꽂는다.");
        }

        // 수정(2026-09-22) — CreateOrLoadRelicPrefab → EnsureRelicPrefab. 이미 있는 프리팹에도 부수기 부품을 더한다.
        FlyingRelic relicPrefab = EnsureRelicPrefab();
        if (relicPrefab == null) return;

        Transform[] points = EnsureLandingPoints(bossRoom, encounter.transform);
        if (points == null) return;

        CrownRitual ritual = encounter.GetComponent<CrownRitual>();
        bool addedRitual = ritual == null;
        if (addedRitual) ritual = Undo.AddComponent<CrownRitual>(encounter.gameObject);

        int filled = FillRitual(ritual, relics, points, relicPrefab);

        // 방도 의식을 직접 가리키게 한다. 비워 둬도 같은 오브젝트에서 찾지만, 인스펙터에서 연결이 보이는 편이 낫다.
        var encounterObject = new SerializedObject(encounter);
        SerializedProperty ritualProperty = encounterObject.FindProperty("crownRitual");
        if (ritualProperty != null && ritualProperty.objectReferenceValue == null)
        {
            ritualProperty.objectReferenceValue = ritual;
            encounterObject.ApplyModifiedPropertiesWithoutUndo();
            filled++;
        }

        EditorSceneManager.MarkSceneDirty(encounter.gameObject.scene);
        Selection.activeGameObject = encounter.gameObject;

        Debug.Log($"[왕관 의식] {bossRoom.name}/{encounter.name}에 왕관 의식을 " +
                  (addedRitual ? "붙였다" : "점검했다") + $" — 빈 칸 {filled}개를 채웠다.\n" +
                  $"유물: {string.Join(", ", relics.Select(relic => relic.DisplayName))}\n" +
                  $"유물 프리팹: {RelicPrefabPath}\n" +
                  "씬을 저장해라(Ctrl+S). 유물이 내려앉을 자리는 " +
                  $"{encounter.name}/{PointsRootName} 아래 네 점을 옮겨 조정한다.", encounter);

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(RelicTornClipPath) == null)
        {
            Debug.LogWarning("[왕관 의식] 유물 뽑힘 애니메이션이 아직 없다. " +
                             "Tools → 재의 길 → 애니메이션 → 8방향 플레이어 애니메이션 생성 을 실행해라. " +
                             "안 하면 플레이어가 서 있는 채로 유물만 시간 기준으로 튀어나온다.");
        }
    }

    /// <summary>방 진행 관리자가 가리키는 보스 방을 찾는다. 보스 방 빌더와 같은 길로 찾는다.</summary>
    private static RoomController FindBossRoom()
    {
        var sequence = Object.FindFirstObjectByType<RoomSequenceController>(FindObjectsInactive.Include);
        if (sequence == null)
        {
            Debug.LogError("[왕관 의식] 씬에서 RoomSequenceController를 못 찾았다. Game 씬을 열고 다시 실행해라.");
            return null;
        }

        var sequenceObject = new SerializedObject(sequence);
        if (sequenceObject.FindProperty("bossRoom").objectReferenceValue is RoomController bossRoom)
            return bossRoom;

        Debug.LogError("[왕관 의식] 보스 방이 아직 없다. Tools → 재의 길 → 씬·세팅 → 보스 방 생성 을 먼저 실행해라.");
        return null;
    }

    /// <summary>
    /// 보스 열쇠 유물을 모은다. 이름이 아니라 역할(<see cref="RelicData.RelicRole.BossKey"/>)로 고르므로
    /// 열쇠 유물을 바꾸거나 더해도 저절로 따라온다(<see cref="BossKeyDebugGrant"/>와 같은 규칙).
    /// 경로 순으로 정렬해 돌릴 때마다 같은 유물이 같은 귀퉁이로 간다.
    /// </summary>
    private static List<RelicData> FindBossKeyRelics()
    {
        return AssetDatabase.FindAssets("t:RelicData")
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, System.StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<RelicData>)
            .Where(relic => relic != null && relic.Role == RelicData.RelicRole.BossKey)
            .ToList();
    }

    /// <summary>
    /// 유물이 내려앉을 자리 넷을 만든다. 이미 있는 자리는 옮기지 않는다.
    ///
    /// 자리를 전투 오브젝트(EnemyEncounter) 아래에 두는 이유: 의식과 유물이 거기 붙는다. 방을 복제하거나
    /// 옮겨도 자리가 방과 같이 움직인다.
    /// </summary>
    /// <returns>자리 넷(왼쪽 위, 오른쪽 위, 왼쪽 아래, 오른쪽 아래). 새로 만들어야 하는데 벽이 없으면 null.</returns>
    private static Transform[] EnsureLandingPoints(RoomController room, Transform host)
    {
        Transform root = AshRoomWallBuilder.FindChild(host, PointsRootName);
        if (root == null)
        {
            var rootObject = new GameObject(PointsRootName);
            Undo.RegisterCreatedObjectUndo(rootObject, "왕관 의식 자리 생성");
            rootObject.transform.SetParent(host, false);
            root = rootObject.transform;
        }

        Vector3[] targets = null;
        var points = new Transform[PointNames.Length];

        for (int i = 0; i < PointNames.Length; i++)
        {
            Transform point = AshRoomWallBuilder.FindChild(root, PointNames[i]);
            if (point == null)
            {
                // 새로 만들 자리가 있을 때만 벽을 읽는다. 다 있으면 벽이 없어도 문제없다.
                targets ??= CornerTargets(room, root.position.z);
                if (targets == null) return null;

                var pointObject = new GameObject(PointNames[i]);
                Undo.RegisterCreatedObjectUndo(pointObject, "왕관 의식 자리 생성");
                pointObject.transform.SetParent(root, false);
                pointObject.transform.position = targets[i];

                // 빈 오브젝트는 씬 뷰에 안 보여서 옮기기 어렵다. 이름표 아이콘을 달아 둔다(에디터에만 보인다).
                if (EditorGUIUtility.IconContent("sv_label_1").image is Texture2D label)
                    EditorGUIUtility.SetIconForObject(pointObject, label);

                point = pointObject.transform;
            }

            points[i] = point;
        }

        return points;
    }

    /// <summary>
    /// 벽 안쪽 면으로 둘러싸인 바닥의 네 귀퉁이에서 안쪽으로 들인 자리를 구한다.
    /// </summary>
    /// <returns>왼쪽 위, 오른쪽 위, 왼쪽 아래, 오른쪽 아래. 벽 넷 중 하나라도 없으면 null.</returns>
    private static Vector3[] CornerTargets(RoomController room, float z)
    {
        Transform left = AshRoomWallBuilder.FindChild(room.transform, "Wall_Left");
        Transform right = AshRoomWallBuilder.FindChild(room.transform, "Wall_Right");
        Transform bottom = AshRoomWallBuilder.FindChild(room.transform, "Wall_Bottom");
        Transform top = AshRoomWallBuilder.FindChild(room.transform, "Wall_Top");
        if (left == null || right == null || bottom == null || top == null)
        {
            Debug.LogError($"[왕관 의식] {room.name}의 벽(Wall_Left·Right·Bottom·Top)을 못 찾았다. " +
                           "Tools → 재의 길 → 씬·세팅 → 방 벽을 방마다 소유하게 를 먼저 실행해라.", room);
            return null;
        }

        float innerLeft = AshRoomWallBuilder.InnerFace(left, +1);
        float innerRight = AshRoomWallBuilder.InnerFace(right, -1);
        float innerBottom = AshRoomWallBuilder.InnerFace(bottom, +1, vertical: true);
        float innerTop = AshRoomWallBuilder.InnerFace(top, -1, vertical: true);

        float xLeft = innerLeft + InsetSide;
        float xRight = innerRight - InsetSide;
        float yTop = innerTop - InsetTop;
        float yBottom = innerBottom + InsetBottom;

        Debug.Log($"[왕관 의식] 바닥 x {innerLeft:0.00}~{innerRight:0.00}, y {innerBottom:0.00}~{innerTop:0.00} → " +
                  $"자리 x {xLeft:0.00} / {xRight:0.00}, y {yTop:0.00}(위) / {yBottom:0.00}(아래)", room);

        return new[]
        {
            new Vector3(xLeft, yTop, z),
            new Vector3(xRight, yTop, z),
            new Vector3(xLeft, yBottom, z),
            new Vector3(xRight, yBottom, z),
        };
    }

    /// <summary>
    /// 의식의 빈 칸을 채운다. 이미 채워진 칸은 건드리지 않는다.
    /// </summary>
    /// <returns>채운 칸 수.</returns>
    private static int FillRitual(CrownRitual ritual, List<RelicData> relics, Transform[] points, FlyingRelic relicPrefab)
    {
        var serialized = new SerializedObject(ritual);
        int filled = 0;

        SerializedProperty relicArray = serialized.FindProperty("relics");
        SerializedProperty pointArray = serialized.FindProperty("landingPoints");
        if (relicArray.arraySize < PointNames.Length) relicArray.arraySize = PointNames.Length;
        if (pointArray.arraySize < PointNames.Length) pointArray.arraySize = PointNames.Length;

        for (int i = 0; i < PointNames.Length; i++)
        {
            SerializedProperty relic = relicArray.GetArrayElementAtIndex(i);
            if (relic.objectReferenceValue == null && i < relics.Count)
            {
                relic.objectReferenceValue = relics[i];
                filled++;
            }

            SerializedProperty point = pointArray.GetArrayElementAtIndex(i);
            if (point.objectReferenceValue == null)
            {
                point.objectReferenceValue = points[i];
                filled++;
            }
        }

        SerializedProperty prefab = serialized.FindProperty("relicPrefab");
        if (prefab.objectReferenceValue == null)
        {
            prefab.objectReferenceValue = relicPrefab;
            filled++;
        }

        // 뽑히는 순간의 불꽃은 명중 불똥을 빌려 쓴다. 오른쪽(+X)으로 튀는 프리팹이라 의식이 유물마다
        // 날아가는 쪽으로 돌려 터뜨린다. 전용 불꽃을 만들면 여기만 바꾸면 된다.
        SerializedProperty burst = serialized.FindProperty("burstPrefab");
        if (burst.objectReferenceValue == null)
        {
            var sparks = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPrefabPath);
            if (sparks != null)
            {
                burst.objectReferenceValue = sparks;
                filled++;
            }
            else
            {
                Debug.LogWarning($"[왕관 의식] 불꽃 프리팹을 못 찾았다: {BurstPrefabPath}\n" +
                                 "Tools → 재의 길 → 파티클 → 플레이어 파티클 만들기 가 만든다. 비워 두면 불꽃 없이 뽑힌다.");
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        return filled;
    }

    /// <summary>
    /// 날아가는 유물 프리팹을 만든다. 이미 있으면 그대로 쓴다(인스펙터에서 고친 크기·비행 값을 지킨다).
    ///
    /// 구성:
    /// <code>
    /// FlyingRelic (SortingGroup, FlyingRelic) — 루트는 늘 바닥 위의 자리다
    ///  ├ Shadow  바닥 그림자
    ///  └ Body    높이만큼 떠오르는 부분
    ///     ├ Trail  날아가는 동안의 꼬리(아이콘 뒤)
    ///     ├ Icon   유물 아이콘
    ///     └ Glow   바닥을 비추는 잿불 빛
    /// </code>
    /// 루트와 Body를 나눈 이유는 <see cref="FlyingRelic"/> 주석 참고 — 그림자는 바닥을 미끄러지고 그림만 솟는다.
    ///
    /// 수정(2026-09-22) — 루트에 부수기 부품(Health·트리거 판정·Enemy 레이어)을 더했다(<see cref="EnsureHitParts"/>).
    /// </summary>
    private static FlyingRelic EnsureRelicPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RelicPrefabPath) == null) return CreateRelicPrefab();

        // 추가 생성(2026-09-22) — 이미 있으면 모양과 수치는 그대로 두고 빠진 부품만 더한다. 09-21에 만든 프리팹에는
        // 부수기 부품이 없어서, 이 길이 없으면 프리팹을 지우고 다시 만들어야 한다(그러면 인스펙터에서 고친 값이 날아간다).
        GameObject root = PrefabUtility.LoadPrefabContents(RelicPrefabPath);
        try
        {
            var relic = root.GetComponent<FlyingRelic>();
            if (relic == null)
            {
                Debug.LogError($"[왕관 의식] {RelicPrefabPath}에 FlyingRelic이 없다. 그 프리팹을 지우고 다시 실행해라.");
                return null;
            }

            if (EnsureHitParts(root, relic))
            {
                PrefabUtility.SaveAsPrefabAsset(root, RelicPrefabPath);
                Debug.Log($"[왕관 의식] 유물 프리팹에 맞는 판정(Health·트리거·Enemy 레이어)을 더했다 → {RelicPrefabPath}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(RelicPrefabPath).GetComponent<FlyingRelic>();
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 유물 프리팹을 새로 만든다. 본문은 원래 CreateOrLoadRelicPrefab의 "새로 만들기" 부분이다.
    /// </summary>
    private static FlyingRelic CreateRelicPrefab()
    {
        var shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowSpritePath);
        if (shadowSprite == null)
            Debug.LogWarning($"[왕관 의식] 그림자 스프라이트를 못 찾았다: {ShadowSpritePath}. 그림자 없이 만든다.");

        // 조명을 안 받는 기본 스프라이트 재질. 아이콘은 UI에서 쓰던 그림이라 원래 색 그대로 보여야
        // 어두운 귀퉁이에서도 "저기 부술 게 있다"가 읽힌다. 주변 바닥은 Glow 빛이 밝힌다.
        var unlit = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");

        var root = new GameObject("FlyingRelic");
        try
        {
            // 정렬 그룹 — 날 때 VFX, 내려앉으면 Entity. 층은 FlyingRelic이 실행 중에 바꾼다.
            var group = root.AddComponent<SortingGroup>();
            group.sortingLayerName = "VFX";

            SpriteRenderer shadow = CreateShadow(root.transform, shadowSprite, unlit);

            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            body.localPosition = new Vector3(0f, PreviewHoverHeight, 0f);

            TrailRenderer trail = CreateTrail(body, unlit);

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(body, false);
            var icon = iconObject.AddComponent<SpriteRenderer>();
            icon.sharedMaterial = unlit;
            icon.sortingLayerName = "VFX";
            icon.sortingOrder = 1;

            CreateGlow(body);

            var relic = root.AddComponent<FlyingRelic>();
            var serialized = new SerializedObject(relic);
            serialized.FindProperty("body").objectReferenceValue = body;
            serialized.FindProperty("iconRenderer").objectReferenceValue = icon;
            serialized.FindProperty("shadowRenderer").objectReferenceValue = shadow;
            serialized.FindProperty("trail").objectReferenceValue = trail;
            serialized.FindProperty("sortingGroup").objectReferenceValue = group;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 추가 생성(2026-09-22) — 부수기 부품. 이미 있는 프리팹을 고칠 때와 같은 함수를 쓴다.
            EnsureHitParts(root, relic);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, RelicPrefabPath, out bool success);
            if (!success || saved == null)
            {
                Debug.LogError($"[왕관 의식] 유물 프리팹 저장에 실패했다: {RelicPrefabPath}");
                return null;
            }

            Debug.Log($"[왕관 의식] 날아가는 유물 프리팹을 새로 만들었다 → {RelicPrefabPath}", saved);
            return saved.GetComponent<FlyingRelic>();
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 추가 생성(2026-09-22) — 공격에 맞게 하는 부품을 빠진 것만 더한다. 이미 있는 부품과 채워진 칸은 건드리지 않는다.
    ///
    /// <b>튜토리얼 허수아비와 같은 조건이다.</b> 플레이어 공격 다섯(기본·W·E·Q·R)은 전부 Enemy 레이어에서
    /// <see cref="Health"/>를 찾아 때린다. 그래서 루트를 Enemy 레이어에 두고 Health와 판정을 단다.
    /// <list type="bullet">
    /// <item>판정은 <b>트리거</b>다. 떠 있는 물건이 발길을 막으면 어색하고, 유물 뒤(위쪽)로 지나갈 때 보이지 않는 벽에 걸린다.
    /// 트리거여도 검(트리거)·화살·범위 스킬(겹침 검사, 프로젝트 설정상 트리거도 잡힌다)이 모두 닿는다.</item>
    /// <item>판정은 루트(바닥 자리)에 둔다. 몸(Body)은 위아래로 흔들려서, 거기 두면 판정이 같이 출렁인다.</item>
    /// <item>프리팹에서는 판정을 꺼 둔다. 내려앉은 뒤에 <see cref="FlyingRelic"/>이 켠다.</item>
    /// </list>
    /// </summary>
    /// <returns>무엇이든 바꿨으면 true.</returns>
    private static bool EnsureHitParts(GameObject root, FlyingRelic relic)
    {
        bool changed = false;

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0)
        {
            Debug.LogError("[왕관 의식] Enemy 레이어가 없다. 레이어 설정을 확인해라 — 없으면 유물이 공격에 안 맞는다.");
        }
        else if (root.layer != enemyLayer)
        {
            root.layer = enemyLayer;
            changed = true;
        }

        var health = root.GetComponent<Health>();
        if (health == null)
        {
            health = root.AddComponent<Health>();
            var healthObject = new SerializedObject(health);
            healthObject.FindProperty("maxHealth").intValue = RelicHealth;
            healthObject.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }

        var hitCollider = root.GetComponent<Collider2D>();
        if (hitCollider == null)
        {
            var box = root.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.offset = new Vector2(0f, HitBoxHeight * 0.5f);
            box.size = new Vector2(HitBoxWidth, HitBoxHeight);
            box.enabled = false;
            hitCollider = box;
            changed = true;
        }

        var serialized = new SerializedObject(relic);
        changed |= FillIfEmpty(serialized, "health", health);
        changed |= FillIfEmpty(serialized, "hitCollider", hitCollider);

        // 부서지는 불꽃도 명중 불똥을 빌린다. 유물이 맞은 방향(때린 쪽 반대)으로 돌려서 터뜨린다.
        changed |= FillIfEmpty(serialized, "breakBurstPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(BurstPrefabPath));
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    /// <summary>추가 생성(2026-09-22) — 참조 칸이 비어 있을 때만 채운다. 채웠으면 true.</summary>
    private static bool FillIfEmpty(SerializedObject target, string propertyName, Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property == null || value == null || property.objectReferenceValue != null) return false;

        property.objectReferenceValue = value;
        return true;
    }

    /// <summary>
    /// 바닥 그림자. 루트(바닥 자리)에 붙어서 유물이 솟아도 바닥에 남는다.
    /// 처음에는 투명하다 — 진하기는 <see cref="FlyingRelic"/>이 날아가는 동안 서서히 올린다.
    /// </summary>
    private static SpriteRenderer CreateShadow(Transform parent, Sprite sprite, Material material)
    {
        var shadowObject = new GameObject("Shadow");
        shadowObject.transform.SetParent(parent, false);

        // 캐릭터 그림자와 같이 1픽셀 내린다(접지 그림자 빌더와 같은 값).
        shadowObject.transform.localPosition = new Vector3(0f, -0.05f, 0f);

        var shadow = shadowObject.AddComponent<SpriteRenderer>();
        shadow.sprite = sprite;
        shadow.sharedMaterial = material;
        shadow.color = new Color(0f, 0f, 0f, 0f);
        shadow.sortingLayerName = "VFX";
        shadow.sortingOrder = -1;

        if (sprite != null && sprite.bounds.size.x > 0f)
        {
            float scale = ShadowWidth / sprite.bounds.size.x;
            shadowObject.transform.localScale = new Vector3(scale, scale * ShadowHeightRatio, 1f);
        }

        return shadow;
    }

    /// <summary>
    /// 날아가는 동안 남는 꼬리. 몸(Body) 아래에 두어 포물선을 그대로 그린다.
    ///
    /// 색은 흰 심을 뺀 잿불(주황 → 짙은 불씨)이다. 뽑혀 나간 유물은 이제 보스 의식의 물건이라,
    /// 몬스터 불티와 같은 규칙(적 쪽 불티는 흰 심을 뺀다)을 따른다.
    /// 파티클 대신 TrailRenderer를 쓰는 이유: 유물 네 개가 0.8초 날 뿐이라, 끊김 없는 선 하나면 충분하고
    /// 거리마다 입자를 뿌리는 파티클보다 설정이 훨씬 적다.
    /// </summary>
    private static TrailRenderer CreateTrail(Transform body, Material material)
    {
        var trailObject = new GameObject("Trail");
        trailObject.transform.SetParent(body, false);

        var trail = trailObject.AddComponent<TrailRenderer>();
        trail.time = 0.3f;
        trail.minVertexDistance = 0.08f;
        trail.widthMultiplier = 0.9f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trail.numCapVertices = 2;

        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(AshPlayerParticleBuilder.Amber, 0f),
                new GradientColorKey(AshPlayerParticleBuilder.Ember, 0.45f),
                new GradientColorKey(AshPlayerParticleBuilder.Deep, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(0.5f, 0.5f),
                new GradientAlphaKey(0f, 1f),
            });
        trail.colorGradient = gradient;

        trail.sharedMaterial = material;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sortingLayerName = "VFX";
        trail.sortingOrder = 0;

        // 프리팹 안에서는 꺼 둔다. 켜 두면 만들어지는 순간(가슴 자리)부터 선이 그어진다.
        // 날리기 시작할 때 FlyingRelic이 궤적을 지우고 켠다.
        trail.emitting = false;

        return trail;
    }

    /// <summary>
    /// 유물 주위 바닥을 비추는 잿불 빛. 떠 있는 유물이 "살아 있는 저주"로 보이고, 방 귀퉁이가 어두워도
    /// 부술 대상의 자리가 바닥 빛으로 먼저 읽힌다. 플레이어의 HeroLight(세기 1.6, 반경 9)보다 작고 약하게 둔다 —
    /// 네 개가 겹쳐도 방 전체가 밝아지지 않게.
    /// </summary>
    private static void CreateGlow(Transform body)
    {
        var glowObject = new GameObject("Glow");
        glowObject.transform.SetParent(body, false);

        var glow = glowObject.AddComponent<Light2D>();
        glow.lightType = Light2D.LightType.Point;
        glow.color = AshPlayerParticleBuilder.Ember;
        glow.intensity = 0.9f;
        glow.pointLightInnerRadius = 0.4f;
        glow.pointLightOuterRadius = 3.2f;
        glow.falloffIntensity = 0.6f;
    }
}
