using UnityEditor;
using UnityEngine;

/// <summary>
/// 유물 에셋을 만들고, 씬의 보상 상자에 유물 보상을 붙이는 도구.
///
/// 메뉴: Tools → 재의 길 → 유물 생성 + 상자에 연결
///
/// 값은 전부 "판이 진행될수록 조금씩 강해진다" 정도로 잡았다. 한 번 먹고 판이 뒤집히면
/// 그 뒤로는 유물을 먹는 재미가 없어진다. 상자 회복량을 1~2로 확정한 것과 같은 기조다.
/// </summary>
public static class AshRelicBuilder
{
    private const string Folder = "Assets/Project/Data/Relics";

    private struct Definition
    {
        public string FileName;
        public string DisplayName;
        public string Description;
        public RelicData.EffectKind Effect;
        public float Amount;

        /// <summary>0이면 고정값. Amount보다 크면 그 사이에서 매번 무작위로 뽑는다.</summary>
        public float AmountMax;

        /// <summary>판에서 맡는 역할. 보통은 Normal이다.</summary>
        public RelicData.RelicRole Role;

        public Definition(string fileName, string displayName, string description,
                          RelicData.EffectKind effect, float amount, float amountMax = 0f,
                          RelicData.RelicRole role = RelicData.RelicRole.Normal)
        {
            FileName = fileName; DisplayName = displayName; Description = description;
            Effect = effect; Amount = amount; AmountMax = amountMax; Role = role;
        }
    }

    /// <summary>
    /// 유물 10종.
    ///
    /// <b>표 순서를 바꾸지 마라.</b> 아이콘을 순번(relic_icon_00…09)으로 찾으므로, 순서를 바꾸면
    /// 전부 다른 그림을 달게 된다. 새 유물은 뒤에 붙이고 아이콘도 뒤에 이어 그리면 된다.
    ///
    /// 효과를 7가지로 나눈 이유: 체력·스태미나·데미지 셋으로 10개를 만들면 같은 걸 숫자만
    /// 바꿔 세 번씩 넣는 꼴이라, 뭘 먹었는지 기억에 안 남는다.
    ///
    /// 값은 전부 작다. 하나 먹고 판이 뒤집히면 그 뒤로 유물 먹는 재미가 없어진다 —
    /// 상자 회복량을 1~2로 확정한 것과 같은 기조다.
    /// </summary>
    private static readonly Definition[] Definitions =
    {
        new Definition("Relic_EmberShard", "잿불 조각",
            "식지 않은 잉걸 한 조각. 최대 체력이 1 늘고 그만큼 회복된다.",
            RelicData.EffectKind.MaxHealth, 1f),

        new Definition("Relic_Weight", "무게추",
            "허리에 매다는 낡은 추. 최대 스태미나가 1에서 15 사이로 늘어난다.",
            RelicData.EffectKind.MaxStamina, 1f, 15f),

        new Definition("Relic_BladeShard", "검날 조각",
            "부러진 검의 파편. 모든 스킬 데미지가 1 오른다.",
            RelicData.EffectKind.SkillDamage, 1f),

        // 여기부터 10종으로 늘리며 추가했다.
        // 효과는 그림에 맞춰 골랐다 — 모래시계가 쿨타임, 해골이 처치 보상인 식이다.
        // 그림과 효과가 어긋나면 유물 이름을 봐도 뭘 하는 물건인지 감이 안 온다.
        // 유일한 무작위 유물이다. 주사위라는 그림에만 맞는 게 아니라, 열 개가 전부 고정값이면
        // 상자를 열 때 기대할 것이 "무엇이 나오나" 하나뿐이다. 하나쯤은 먹은 뒤에도
        // 결과가 궁금한 편이 낫다. 1~7이라 최악이어도 잿불 조각(+1)만큼은 나온다.
        new Definition("Relic_AshKingDie", "왕의 주사위",
            "재의 왕이 마지막으로 굴린 주사위. 최대 체력이 1에서 7 사이로 무작위로 늘어난다.",
            RelicData.EffectKind.MaxHealth, 1f, 7f),

        new Definition("Relic_EmberLocket", "잉걸 로켓",
            "가슴에 닿아 있으면 숨이 고르게 돌아온다. 스태미나 회복이 초당 1에서 5 사이로 빨라진다.",
            RelicData.EffectKind.StaminaRegen, 1f, 5f),

        new Definition("Relic_AshenGauntlet", "잿빛 건틀릿",
            "타버린 손을 대신 쥐어준다. 모든 스킬 데미지가 1에서 2 사이로 오른다.",
            RelicData.EffectKind.SkillDamage, 1f, 2f),

        new Definition("Relic_CinderLantern", "잉걸 등불",
            "발밑을 밝혀 걸음이 거침없다. 이동 속도가 1에서 1.5 사이로 오른다.",
            RelicData.EffectKind.MoveSpeed, 1f, 1.5f),

        // 수치 단위가 퍼센트다(RelicData.EffectKind.CooldownRate 주석 참고).
        // 다른 유물과 달리 0~1 비율로 두면 하한 1이 "쿨타임 100% 감소"가 된다.
        new Definition("Relic_CinderHourglass", "잉걸 모래시계",
            "재가 아래로 떨어지는 동안 시간이 접힌다. 모든 스킬 쿨타임이 1%에서 12% 짧아진다.",
            RelicData.EffectKind.CooldownRate, 1f, 12f),

        new Definition("Relic_CrownedAshSkull", "왕관 쓴 해골",
            "쓰러진 것들의 재를 알아서 거둔다. 처치할 때 재가 1에서 4 더 모인다.",
            RelicData.EffectKind.AshPerKill, 1f, 4f),

        new Definition("Relic_BoneTalisman", "잉걸 뼈 부적",
            "매달고 있으면 몸이 덜 지친다. 최대 스태미나가 1에서 25 사이로 늘어난다.",
            RelicData.EffectKind.MaxStamina, 1f, 25f),

        // ── 보스 열쇠 3개 ──
        //
        // 수치를 안 붙인 이유: 효과가 있으면 장착 칸에 들어갈 이유가 생기는데, 칸이 세 개뿐이라
        // 열쇠 셋을 다 끼우면 <b>유물 효과가 하나도 없는 상태로</b> 보스를 만나게 된다.
        // 모으는 행위가 플레이어를 약하게 만들면 안 된다. 보관함에 있기만 하면 인정된다.
        new Definition("Relic_BrokenCrown", "부러진 왕관",
            "재의 왕이 쓰고 있던 것. 반쪽이 어디론가 사라졌다. 왕의 유품 중 하나.",
            RelicData.EffectKind.None, 0f, 0f, RelicData.RelicRole.BossKey),

        new Definition("Relic_KingsSignet", "왕의 인장",
            "무엇을 봉인했는지는 아무도 기억하지 못한다. 왕의 유품 중 하나.",
            RelicData.EffectKind.None, 0f, 0f, RelicData.RelicRole.BossKey),

        new Definition("Relic_AshKey", "재의 열쇠",
            "쥐면 손에서 부스러지는데, 다음 순간 다시 굳는다. 왕의 유품 중 하나.",
            RelicData.EffectKind.None, 0f, 0f, RelicData.RelicRole.BossKey),

        // 네 번째 열쇠.
        //
        // "재의 길"이라는 이름을 여기 안 쓴 이유: 그건 게임 제목이라 <b>최종 보상에 남겨둔다.</b>
        // 열쇠 넷 중 하나로 써버리면 진짜 마지막 보상에 붙일 이름이 없어진다.
        //
        // 네 개가 모여 하나의 이야기가 된다 — 왕관(그가 누구였는지), 인장(무엇을 봉인했는지),
        // 열쇠(어떻게 여는지), 이정표(어디로 가는지).
        new Definition("Relic_AshWaymark", "재의 이정표",
            "길이 새겨진 돌 조각. 홈을 따라 흐르는 잉걸이 한 방향만 가리킨다. 왕의 유품 중 하나.",
            RelicData.EffectKind.None, 0f, 0f, RelicData.RelicRole.BossKey),
    };

    /// <summary>
    /// 유물별 아이콘 원본 파일 이름(확장자 제외). <see cref="Definitions"/>와 순서가 1:1이다.
    ///
    /// 빈 문자열은 3칸짜리 시트에서 잘라 쓰는 것들이다(relic_icon_00~02).
    /// 나머지는 <see cref="AshRelicIconProcessor"/>가 이 표를 읽어 낱장 그림을 다듬는다.
    ///
    /// 표를 여기 둔 이유: 다듬기 도구에 따로 두면 두 표의 순서가 어긋났을 때
    /// <b>유물이 엉뚱한 그림을 달고 나오는데 에러가 안 난다.</b> 순서가 곧 아이콘 번호라
    /// 한 군데서 정해야 한다.
    /// </summary>
    public static readonly string[] IconSources =
    {
        "", "", "",
        "relic-ash-king-die",
        "relic-ember-locket",
        "relic-ashen-gauntlet",
        "relic-cinder-lantern",
        "relic-cinder-hourglass",
        "relic-crowned-ash-skull",
        "relic-ember-bone-talisman",

        // 보스 열쇠 4개. 마지막 것은 파일 이름이 relic-path-of-ash지만 유물 이름은
        // "재의 이정표"다 — 그림을 먼저 뽑고 이름을 바꿨다. 그림 내용(길이 새겨진 돌)은 그대로 맞다.
        "relic-broken-crown",
        "relic-kings-signet",
        "relic-ash-key",
        "relic-path-of-ash",
    };

    [MenuItem("Tools/재의 길/유물 생성 + 상자에 연결")]
    public static void Build()
    {
        var relics = CreateRelics();

        // 상자 연결과 별개로 항상 부른다. 상자에 이미 프리팹이 끼워져 있을 때만 건너뛰게 두면
        // 크기를 고쳐도 반영이 안 된다.
        var pickup = CreateOrLoadPickupPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int chests = AttachToChests(relics, pickup);

        Debug.Log($"[유물] 에셋 {relics.Length}개 확인/생성, 씬의 상자 {chests}개에 연결했다.\n" +
                  (chests == 0
                      ? "씬에 RewardChest가 없다. 방 배치를 먼저 하고 다시 실행해라."
                      : "씬을 저장해라(Ctrl+S)."));
    }

    /// <summary>유물 에셋을 만든다. 이미 있으면 그대로 쓴다(밸런스를 조정했을 수 있다).</summary>
    private static RelicData[] CreateRelics()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Project/Data", "Relics");

        var result = new RelicData[Definitions.Length];

        for (int i = 0; i < Definitions.Length; i++)
        {
            Definition definition = Definitions[i];
            string path = $"{Folder}/{definition.FileName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<RelicData>(path);
            if (existing != null)
            {
                result[i] = existing;
                Repair(existing, definition);
                continue;
            }

            var relic = ScriptableObject.CreateInstance<RelicData>();
            var serialized = new SerializedObject(relic);
            serialized.FindProperty("displayName").stringValue = definition.DisplayName;
            serialized.FindProperty("description").stringValue = definition.Description;
            serialized.FindProperty("effect").enumValueIndex = (int)definition.Effect;
            serialized.FindProperty("amount").floatValue = definition.Amount;
            serialized.FindProperty("amountMax").floatValue = definition.AmountMax;
            serialized.FindProperty("role").enumValueIndex = (int)definition.Role;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(relic, path);
            result[i] = relic;
        }

        // 아이콘은 에셋이 이미 있어도 따로 채운다. 유물 에셋을 먼저 만들고 아이콘 시트를
        // 나중에 뽑았기 때문에, 만들 때 한 번만 넣는 방식으로는 영영 비어 있게 된다.
        AssignIcons(result);

        return result;
    }

    /// <summary>
    /// 이미 있는 에셋을 지금 표에 맞춘다.
    ///
    /// <b>수치와 구조를 다르게 다룬다.</b>
    ///
    /// 수치(amount / amountMax)는 인스펙터에서 밸런스를 손봤을 수 있으니 함부로 안 덮는다.
    /// 다만 amountMax는 나중에 생긴 필드라, 그 전에 만들어진 에셋은 0인 채로 남아 무작위
    /// 유물이 영영 고정값으로 나온다. 0일 때만 채우므로 손으로 정한 범위는 그대로 둔다.
    /// 이때 하한(amount)도 같이 맞춘다 — 예전 고정값이 하한으로 남으면 범위가 어긋난다.
    ///
    /// 반면 역할(role)과 이름·설명은 <b>항상</b> 표에 맞춘다. 이건 밸런스가 아니라 구조라
    /// 인스펙터에서 손댈 값이 아니고, 어긋나면 열쇠가 아닌 유물이 열쇠로 세어지는 식으로
    /// 조용히 틀린다. 실제로 "재의 길"을 클리어 유물에서 열쇠로 바꿀 때 이게 필요했다.
    /// </summary>
    private static void Repair(RelicData relic, Definition definition)
    {
        var serialized = new SerializedObject(relic);
        bool changed = false;

        var role = serialized.FindProperty("role");
        if (role != null && role.enumValueIndex != (int)definition.Role)
        {
            role.enumValueIndex = (int)definition.Role;
            changed = true;
        }

        var name = serialized.FindProperty("displayName");
        if (name != null && name.stringValue != definition.DisplayName)
        {
            name.stringValue = definition.DisplayName;
            changed = true;
        }

        var description = serialized.FindProperty("description");
        if (description != null && description.stringValue != definition.Description)
        {
            description.stringValue = definition.Description;
            changed = true;
        }

        var max = serialized.FindProperty("amountMax");
        if (definition.AmountMax > 0f && max != null && max.floatValue <= 0f)
        {
            max.floatValue = definition.AmountMax;
            serialized.FindProperty("amount").floatValue = definition.Amount;
            changed = true;

            Debug.Log($"[유물] {definition.DisplayName}에 무작위 범위를 넣었다 " +
                      $"({definition.Amount}~{definition.AmountMax}).");
        }

        if (!changed) return;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(relic);
    }

    /// <summary>표 순서대로 유물 아이콘을 채운다. 이미 있으면 건드리지 않는다.</summary>
    private static void AssignIcons(RelicData[] relics)
    {
        var icons = CollectIcons();
        int missing = 0;

        for (int i = 0; i < relics.Length; i++)
        {
            if (relics[i] == null) continue;

            var serialized = new SerializedObject(relics[i]);
            var iconProperty = serialized.FindProperty("icon");
            if (iconProperty == null || iconProperty.objectReferenceValue != null) continue;

            if (!icons.TryGetValue($"relic_icon_{i:00}", out var found))
            {
                missing++;
                continue;
            }

            iconProperty.objectReferenceValue = found;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(relics[i]);
        }

        // 하나씩 경고하지 않고 모아서 한 줄로 알린다. 아이콘 시트를 아직 안 자른 상태면
        // 열 줄이 똑같이 쏟아져서 정작 다른 로그가 안 보인다.
        if (missing > 0)
        {
            Debug.LogWarning($"[유물] 아이콘 {missing}개를 못 찾았다(찾은 것 {icons.Count}개). " +
                             "원본 시트 정규화 → VFX 스프라이트 슬라이스 순으로 먼저 실행해라.");
        }
    }

    /// <summary>
    /// UI 폴더에서 relic_icon_NN 스프라이트를 전부 모은다.
    ///
    /// 시트 경로를 박아두지 않고 훑는 이유: 시트 파일 이름에 칸 수가 들어간다
    /// (relic_icons_<b>3frames</b>_768x256). 아이콘을 10개로 늘리면 파일 이름이 바뀌는데,
    /// 경로가 박혀 있으면 그때마다 이 도구를 같이 고쳐야 하고, 안 고치면 조용히 0개가 된다.
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, Sprite> CollectIcons()
    {
        var result = new System.Collections.Generic.Dictionary<string, Sprite>();

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Project/Art/UI" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Sprite sprite && sprite.name.StartsWith("relic_icon_"))
                    result[sprite.name] = sprite;
            }
        }

        return result;
    }

    /// <summary>
    /// 상자에서 튀어나올 픽업 프리팹을 만든다.
    ///
    /// 그림을 자식(Visual)에 둔 이유: 튀어 오르는 높이를 자식의 로컬 y로만 흉내 내기 위해서다.
    /// 루트를 올리면 콜라이더까지 같이 떠서, 공중에 뜬 물건을 밟는 판정이 어긋난다.
    /// </summary>
    /// <summary>
    /// 그림 크기(배율). 아이콘 한 칸이 256px이고 UI 폴더는 PPU 100이라 2.56유닛이 원본 크기다.
    /// 0.6은 눈에 안 띄게 작아서 1.0으로 올렸다 — 바닥에 떨어진 물건은 상자보다 확실히
    /// 작으면서도 "저기 뭔가 떨어졌다"가 한눈에 보여야 한다.
    /// </summary>
    private const float VisualScale = 1.0f;

    /// <summary>줍는 판정 반지름. 그림이 커진 만큼 같이 넓혔다.</summary>
    private const float PickupRadius = 1.3f;

    private static RelicPickup CreateOrLoadPickupPrefab()
    {
        const string path = "Assets/Project/Prefabs/Items/RelicPickup.prefab";

        // 이미 있으면 크기만 다시 맞춘다.
        //
        // 통째로 새로 만들지 않는 이유: 튀는 거리·높이 같은 값을 인스펙터에서 손봤을 수 있다.
        // 그렇다고 있으면 그냥 넘어가게 두면, 크기를 고쳐도 프리팹이 이미 있어서 영영 반영이
        // 안 된다(실제로 그래서 0.6인 채로 남아 있었다). 크기는 이 도구가 정하는 값이니 덮는다.
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            ApplySize(existing);
            return existing.GetComponent<RelicPickup>();
        }

        if (!AssetDatabase.IsValidFolder("Assets/Project/Prefabs/Items"))
            AssetDatabase.CreateFolder("Assets/Project/Prefabs", "Items");

        var root = new GameObject("RelicPickup");
        try
        {
            // Pickup 레이어는 충돌 매트릭스에서 Player하고만 켜져 있다. 적은 안 밟는다.
            root.layer = LayerMask.NameToLayer("Pickup");

            var collider = root.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = PickupRadius;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(VisualScale, VisualScale, 1f);

            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = "Entity";

            var pickup = root.AddComponent<RelicPickup>();
            var serialized = new SerializedObject(pickup);
            serialized.FindProperty("visual").objectReferenceValue = visual.transform;
            serialized.FindProperty("spriteRenderer").objectReferenceValue = renderer;
            serialized.FindProperty("pickupCollider").objectReferenceValue = collider;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success || saved == null)
            {
                Debug.LogError($"[유물] 픽업 프리팹 저장 실패: {path}");
                return null;
            }

            Debug.Log($"[유물] 픽업 프리팹을 새로 만들었다 → {path}");
            return saved.GetComponent<RelicPickup>();
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>배열 프로퍼티를 그 역할에 해당하는 유물로만 채운다.</summary>
    private static void Fill(SerializedProperty array, RelicData[] relics, RelicData.RelicRole role)
    {
        if (array == null) return;

        var matched = new System.Collections.Generic.List<RelicData>();
        foreach (RelicData relic in relics)
            if (relic != null && relic.Role == role) matched.Add(relic);

        array.arraySize = matched.Count;
        for (int i = 0; i < matched.Count; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = matched[i];
    }

    /// <summary>이미 있는 픽업 프리팹의 그림 크기와 줍는 반지름만 지금 값으로 맞춘다.</summary>
    private static void ApplySize(GameObject prefab)
    {
        bool changed = false;

        var visual = prefab.transform.Find("Visual");
        if (visual != null && !Mathf.Approximately(visual.localScale.x, VisualScale))
        {
            visual.localScale = new Vector3(VisualScale, VisualScale, 1f);
            changed = true;
        }

        var collider = prefab.GetComponent<CircleCollider2D>();
        if (collider != null && !Mathf.Approximately(collider.radius, PickupRadius))
        {
            collider.radius = PickupRadius;
            changed = true;
        }

        if (!changed) return;

        PrefabUtility.SavePrefabAsset(prefab);
        Debug.Log($"[유물] 픽업 크기를 {VisualScale}배로 맞췄다(줍는 반지름 {PickupRadius}).");
    }

    /// <summary>
    /// 씬의 모든 보상 상자에 유물 보상을 붙인다.
    ///
    /// 상자가 방 진행 도구로 씬에 직접 만들어지므로 프리팹이 아니라 씬 오브젝트를 찾는다.
    /// 이미 붙어 있으면 추첨 목록만 채운다 — 확률 같은 값을 손으로 조정했을 수 있다.
    /// </summary>
    private static int AttachToChests(RelicData[] relics, RelicPickup pickup)
    {
        // FindObjectsInactive.Include가 반드시 있어야 한다.
        //
        // 기본값은 비활성 오브젝트를 건너뛴다. 보상 상자는 방을 클리어하기 전까지 꺼져 있어서,
        // 기본값으로 찾으면 씬에 멀쩡히 있는데도 "상자가 없다"고 나온다.
        // 에러가 아니라 0개로 조용히 넘어가는 종류라 원인을 찾기 어렵다.
        var chests = Object.FindObjectsByType<RewardChest>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int touched = 0;

        foreach (var chest in chests)
        {
            var reward = chest.GetComponent<ChestRelicReward>();
            if (reward == null) reward = Undo.AddComponent<ChestRelicReward>(chest.gameObject);

            var serialized = new SerializedObject(reward);

            // 상자가 주는 것과 안 주는 것을 여기서 가른다.
            // 클리어 유물은 어느 쪽에도 안 들어간다 — 보스만 준다. 상자에서 나오면
            // 보스를 만나기도 전에 판이 끝난다.
            Fill(serialized.FindProperty("pool"), relics, RelicData.RelicRole.Normal);
            Fill(serialized.FindProperty("keyPool"), relics, RelicData.RelicRole.BossKey);

            // 픽업 프리팹이 비어 있으면 채운다. 손으로 다른 걸 끼웠으면 두고.
            var pickupProperty = serialized.FindProperty("pickupPrefab");
            if (pickupProperty != null && pickupProperty.objectReferenceValue == null)
                pickupProperty.objectReferenceValue = pickup;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(reward);
            touched++;
        }

        if (touched > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        return touched;
    }
}
