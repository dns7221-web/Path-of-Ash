using UnityEditor;
using UnityEngine;

/// <summary>
/// 플레이어 프리팹을 만들어내는 에디터 도구.
///
/// 메뉴: Tools → 재의 길 → 플레이어 프리팹 생성
///
/// 프리팹을 손으로 조립하지 않고 스크립트로 둔 이유는 AshProjectSetup과 같다.
/// 컴포넌트가 6개, 설정할 값이 15개쯤 되는데 그중 하나(예: Rigidbody2D의 회전 고정)를
/// 빠뜨리면 "플레이어가 벽에 부딪히면 빙글빙글 도는" 식으로 나타난다. 그런 값들이 왜 그 값인지
/// 코드와 주석으로 남는 게 인스펙터 스크린샷보다 오래간다.
///
/// 이 도구는 <see cref="AshPlayerAnimationBuilder"/>가 먼저 돌아 있어야 한다.
/// </summary>
public static class AshPlayerPrefabBuilder
{
    private const string PrefabFolder = "Assets/Project/Prefabs/Player";
    private const string PrefabPath = PrefabFolder + "/Player.prefab";
    private const string ControllerPath = "Assets/Project/Animations/Player/Player.controller";

    /// <summary>
    /// 캐릭터의 월드 키(유닛). 그림의 픽셀 키를 임포트 PPU로 나눈 값이다.
    ///
    /// 수정(Game 씬 규격 확인 시점): 콜라이더를 유닛 숫자로 박아뒀더니 PPU를 80에서 32로 바꿨을 때
    /// 그림만 2.5배 커지고 판정은 그대로 남았다. 두 값이 같은 출처에서 나오도록 계산으로 바꿨다.
    /// 이제 PPU를 바꾸면 콜라이더가 따라온다.
    /// </summary>
    private static float CharacterHeightUnits =>
        AshPlayerSpriteSheets.CharacterPixelHeight / AshSpriteImportRules.CharacterPixelsPerUnit;

    /// <summary>
    /// 콜라이더 크기. 캐릭터 키에 대한 비율로 적는다 (가로 45%, 세로 25%).
    ///
    /// 몸 전체를 감싸지 않는 이유: 이 게임은 중력이 없는 탑다운이라(AshProjectSetup이 2D 중력을
    /// 0으로 꺼뒀다) 플레이어가 벽에 닿는 지점은 "발이 딛는 바닥"이지 머리나 후드가 아니다.
    /// 몸 전체를 콜라이더로 잡으면 위쪽 벽에 머리가 걸려서 통로에 못 들어가는 일이 생긴다.
    /// </summary>
    private static Vector2 ColliderSize =>
        new Vector2(CharacterHeightUnits * 0.45f, CharacterHeightUnits * 0.25f);

    /// <summary>
    /// 콜라이더 중심의 y 오프셋. 피벗이 발밑(지면선)이라 0이면 콜라이더가 절반쯤 바닥에 묻힌다.
    /// 높이의 절반만큼 올려서 콜라이더 아랫변이 피벗에 닿게 한다.
    /// </summary>
    private static Vector2 ColliderOffset => new Vector2(0f, ColliderSize.y * 0.5f);

    [MenuItem("Tools/재의 길/플레이어 프리팹 생성")]
    public static void BuildPrefab()
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError(
                $"[플레이어 프리팹] 애니메이터 컨트롤러가 없다: {ControllerPath}\n" +
                "Tools → 재의 길 → 플레이어 애니메이션 생성 을 먼저 실행해라.");
            return;
        }

        // 씬에 임시로 만들어 조립한 뒤 프리팹으로 저장하고 지운다.
        // PrefabUtility는 씬 오브젝트를 받아 프리팹으로 굽는 방식이라 이 과정이 필요하다.
        var root = new GameObject("Player");

        try
        {
            root.layer = LayerMask.NameToLayer("Player");
            if (root.layer < 0)
            {
                Debug.LogError("[플레이어 프리팹] Player 레이어가 없다. " +
                               "Tools → 재의 길 → 프로젝트 세팅 적용 을 먼저 실행해라.");
                return;
            }

            SetUpRenderer(root, controller);
            SetUpPhysics(root);

            // 컴포넌트를 먼저 다 붙인 뒤에 참조를 연결한다. PlayerController가 Reset에서
            // GetComponentInChildren으로 자기 참조를 채우기 때문에, 순서가 반대면 못 찾는다.
            var stamina = root.AddComponent<PlayerStamina>();
            var health = root.AddComponent<Health>();
            var attackHitbox = CreateAttackHitbox(root);
            var playerController = root.AddComponent<PlayerController>();

            LinkReferences(playerController, root, stamina, health, attackHitbox);

            // 추가 생성 — 스킬 시스템. PlayerController 뒤에 붙인다.
            // SkillController가 [RequireComponent(typeof(PlayerController))]라 순서가 반대면
            // 유니티가 PlayerController를 하나 더 붙여버린다.
            var skillController = root.AddComponent<SkillController>();
            LinkSkillReferences(skillController, attackHitbox);

            EnsureFolder("Assets/Project/Prefabs", "Player");

            // 같은 경로로 저장하면 기존 프리팹의 GUID가 유지된다. 씬에 이미 배치해둔
            // 인스턴스가 끊기지 않는다.
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);

            if (success && prefab != null)
                Debug.Log($"[플레이어 프리팹] 생성 완료 → {PrefabPath}");
            else
                Debug.LogError($"[플레이어 프리팹] 저장 실패: {PrefabPath}");
        }
        finally
        {
            // 예외가 나도 씬에 임시 오브젝트를 남기지 않는다.
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>스프라이트 렌더러와 애니메이터를 붙인다.</summary>
    private static void SetUpRenderer(GameObject root, RuntimeAnimatorController controller)
    {
        var renderer = root.AddComponent<SpriteRenderer>();

        // Entity는 AshProjectSetup이 만들어둔 정렬 레이어다. 바닥(Floor) 위, 이펙트(VFX) 아래에
        // 그려진다.
        renderer.sortingLayerName = "Entity";

        // 씬 뷰에서 프리팹이 빈 사각형으로 보이지 않게 첫 프레임을 미리 넣어둔다.
        // 실행하면 Animator가 곧바로 덮어쓰므로 어떤 프레임이든 상관없다.
        var player = AshPlayerSpriteSheets.Player;
        var idleSprite = FindSprite(
            player.FolderPath,
            player.Sheets[0].FileName,
            player.SpriteName("idle", 0));

        if (idleSprite != null) renderer.sprite = idleSprite;

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        // 컬링 모드를 AlwaysAnimate로 두는 이유: 기본값은 화면 밖에서 애니메이션을 멈추는데,
        // 그러면 화면 밖에서 죽은 캐릭터의 사망 모션이 진행되지 않아 상태가 어긋난다.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // 스프라이트 애니메이션은 프레임을 갈아끼우는 방식이라 보간할 값이 없다.
        // ApplyRootMotion을 켜두면 Animator가 위치를 건드려서 Rigidbody2D 이동과 싸운다.
        animator.applyRootMotion = false;
    }

    /// <summary>Rigidbody2D와 콜라이더를 붙인다.</summary>
    private static void SetUpPhysics(GameObject root)
    {
        var body = root.AddComponent<Rigidbody2D>();

        // 속도를 직접 대입해 움직이지만 벽에 막혀야 하므로 Dynamic이다.
        // Kinematic이면 콜라이더가 있어도 벽을 통과한다.
        body.bodyType = RigidbodyType2D.Dynamic;

        // gravityScale은 건드리지 않는다. 2D 중력은 AshProjectSetup이 전역에서 (0,0)으로
        // 꺼뒀고, 프리팹마다 따로 끄면 그 결정이 흐려진다는 게 PlayerController 주석의 판단이다.

        // 벽에 비스듬히 부딪혔을 때 물리가 회전 토크를 주는 걸 막는다. 이게 없으면
        // 캐릭터가 벽 모서리에 닿는 순간 빙글 돈다.
        body.freezeRotation = true;

        // 물리는 초당 50번, 화면은 그보다 자주 그려진다. 보간을 켜면 그 사이를 메워
        // 이동이 부드러워진다. 카메라가 플레이어를 따라다닐 때 차이가 크다.
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        // 대시 속도가 35유닛/초라 물리 한 스텝(1/50초)에 0.7유닛을 간다. 얇은 벽이라면 이산
        // 판정으로는 뚫고 지나갈 수 있어서 연속 판정으로 둔다. 속도를 올릴수록 더 중요해진다.
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var collider = root.AddComponent<CapsuleCollider2D>();
        collider.direction = CapsuleDirection2D.Horizontal;
        collider.size = ColliderSize;
        collider.offset = ColliderOffset;
    }

    /// <summary>
    /// 추가 생성 — SkillController의 슬롯과 히트박스를 연결한다.
    ///
    /// Q만 채우고 W/E/R은 비워둔다. 빈 슬롯은 눌러도 아무 일이 없게 만들어뒀으므로,
    /// 나중에 스킬 에셋을 만들어 인스펙터에서 끼우면 그 자리에서 동작한다.
    /// </summary>
    private static void LinkSkillReferences(SkillController controller, DamageHitbox meleeHitbox)
    {
        var serialized = new SerializedObject(controller);

        var slots = serialized.FindProperty("slots");
        slots.arraySize = SkillController.SlotCount;

        // 0 = 기본 공격(Ctrl), 1 = Q 내려찍기. W/E/R은 비워둔다 —
        // 활은 투사체가, 지팡이는 장판이, 필살기는 재 게이지가 아직 없다.
        slots.GetArrayElementAtIndex(0).objectReferenceValue = CreateOrLoadSlashSkill();
        slots.GetArrayElementAtIndex(1).objectReferenceValue = CreateOrLoadGroundSlamSkill();
        slots.GetArrayElementAtIndex(2).objectReferenceValue = CreateOrLoadBowSkill();
        slots.GetArrayElementAtIndex(3).objectReferenceValue = CreateOrLoadStaffSkill();
        slots.GetArrayElementAtIndex(4).objectReferenceValue = CreateOrLoadUltimateSkill();

        serialized.FindProperty("meleeHitbox").objectReferenceValue = meleeHitbox;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 추가 생성 — Q 스킬(잿불 베기) 에셋이 없으면 만들고, 있으면 그대로 쓴다.
    ///
    /// 에셋을 도구가 만드는 이유: 프리팹의 슬롯에 끼울 대상이라, 사람이 Create 메뉴로
    /// 만드는 걸 잊으면 프리팹이 빈 슬롯으로 저장된다. 그러면 Q를 눌러도 아무 일이 없는데
    /// 에러가 안 나서 원인을 찾기 어렵다.
    ///
    /// <b>이미 있으면 덮어쓰지 않는다.</b> 밸런스를 인스펙터에서 조정했을 텐데 도구를 다시
    /// 돌릴 때마다 기본값으로 되돌아가면 조정한 값을 계속 잃는다.
    /// </summary>
    /// <summary>
    /// 추가 생성 — Q 스킬(잿불 대검 내려찍기) 에셋. 기본 공격보다 느리고 세다.
    ///
    /// 판정이 4프레임(좁은 근접)과 5프레임(넓은 전방) 두 번인 기획인데, 지금 MeleeSkillData는
    /// 판정이 하나뿐이다. 우선 모션과 입력이 도는 것까지 만들고, 전용 GroundSlamSkillData는
    /// 이펙트를 붙일 때 같이 만든다.
    /// </summary>
    /// <summary>
    /// 추가 생성 — 화살 프리팹. 없으면 만든다.
    ///
    /// 스프라이트는 슬라이스된 화살 VFX에서 찾아 넣는다. 아직 슬라이스 전이면 비운 채로
    /// 만들고 경고만 남긴다 — 판정은 정상 동작하므로 "보이지 않는 화살"이 날아간다.
    /// 나중에 슬라이스한 뒤 프리팹에서 스프라이트만 끼우면 된다.
    /// </summary>
    private static Projectile CreateOrLoadArrowPrefab()
    {
        const string path = "Assets/Project/Prefabs/VFX/EmberArrow.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<Projectile>(path);
        if (existing != null) return existing;

        EnsureFolder("Assets/Project/Prefabs", "VFX");

        var root = new GameObject("EmberArrow");
        try
        {
            root.layer = LayerMask.NameToLayer("PlayerAttack");

            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = "VFX";

            // 6프레임을 모아 재생기에 넣는다. 화살이 날아가는 동안 꼬리의 재가 흔들린다.
            var frames = new Sprite[6];
            int found = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = FindSprite("Assets/Project/Art/Sprites/VFX",
                                       "vfx_ember_arrow_flight_6frames_1536x256",
                                       $"vfx_arrow_flight_{i:00}");
                if (frames[i] != null) found++;
            }

            if (found > 0)
            {
                renderer.sprite = frames[0];

                var frameAnimator = root.AddComponent<SpriteFrameAnimator>();
                var animatorSerialized = new SerializedObject(frameAnimator);
                var framesProperty = animatorSerialized.FindProperty("frames");
                framesProperty.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++)
                    framesProperty.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

                animatorSerialized.FindProperty("fps").floatValue = 16f;
                animatorSerialized.FindProperty("loop").boolValue = true;
                animatorSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[플레이어 프리팹] 화살 스프라이트를 못 찾았다. " +
                                 "Tools → 재의 길 → VFX 스프라이트 슬라이스 를 먼저 실행해라.");
            }

            root.AddComponent<Rigidbody2D>();

            // 판정은 화살 본체에 직접 단다. 자식으로 빼면 좌우 반전 시 따로 뒤집어야 한다.
            var collider = root.AddComponent<CapsuleCollider2D>();
            collider.direction = CapsuleDirection2D.Horizontal;
            collider.size = new Vector2(1.6f, 0.5f);
            collider.isTrigger = true;

            var hitbox = root.AddComponent<DamageHitbox>();
            var hitboxSerialized = new SerializedObject(hitbox);
            hitboxSerialized.FindProperty("damage").intValue = 2;
            hitboxSerialized.FindProperty("targetLayers").intValue = 1 << LayerMask.NameToLayer("Enemy");
            hitboxSerialized.ApplyModifiedPropertiesWithoutUndo();

            var projectile = root.AddComponent<Projectile>();
            var projectileSerialized = new SerializedObject(projectile);
            projectileSerialized.FindProperty("hitbox").objectReferenceValue = hitbox;
            projectileSerialized.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success || saved == null)
            {
                Debug.LogError($"[플레이어 프리팹] 화살 프리팹 저장 실패: {path}");
                return null;
            }

            Debug.Log($"[플레이어 프리팹] 화살 프리팹을 새로 만들었다 (스프라이트 {found}프레임) → {path}");

            // 저장이 돌려준 오브젝트에서 바로 꺼낸다. LoadAssetAtPath로 다시 읽으면
            // 에셋 임포트가 아직 안 끝난 시점이라 null이 나올 수 있다.
            return saved.GetComponent<Projectile>();
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 추가 생성 — 잿불 기둥 이펙트 프리팹. 한 번 재생하고 스스로 사라진다.
    /// </summary>
    private static GameObject CreateOrLoadSpellEffectPrefab()
    {
        const string path = "Assets/Project/Prefabs/VFX/AshPillar.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        EnsureFolder("Assets/Project/Prefabs", "VFX");

        var root = new GameObject("AshPillar");
        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = "VFX";

            var frames = new Sprite[6];
            int found = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = FindSprite("Assets/Project/Art/Sprites/VFX",
                                       "vfx_ash_staff_ground_spell_6frames_1536x256",
                                       $"vfx_staff_spell_{i:00}");
                if (frames[i] != null) found++;
            }

            if (found == 0)
            {
                Debug.LogWarning("[플레이어 프리팹] 지팡이 주문 스프라이트를 못 찾았다. " +
                                 "VFX 슬라이스를 먼저 실행해라.");
            }
            else
            {
                renderer.sprite = frames[0];

                var animator = root.AddComponent<SpriteFrameAnimator>();
                var serialized = new SerializedObject(animator);
                var framesProperty = serialized.FindProperty("frames");
                framesProperty.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++)
                    framesProperty.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

                // 6프레임 / 7fps = 약 0.86초. 예고(0.5초)와 폭발이 한 클립 안에서 이어진다.
                serialized.FindProperty("fps").floatValue = 7f;
                serialized.FindProperty("loop").boolValue = false;
                serialized.FindProperty("destroyWhenFinished").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success || saved == null)
            {
                Debug.LogError($"[플레이어 프리팹] 잿불 기둥 프리팹 저장 실패: {path}");
                return null;
            }

            Debug.Log($"[플레이어 프리팹] 잿불 기둥 프리팹 생성 (스프라이트 {found}프레임) → {path}");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 추가 생성 — 이펙트 프리팹을 만든다. 한 번 재생하고 스스로 사라진다.
    ///
    /// 화살·기둥·필살기에서 같은 조립을 세 번 반복했기에 여기로 묶었다.
    /// </summary>
    private static GameObject CreateOrLoadEffectPrefab(
        string prefabName, string sheet, string spritePrefix, float fps, float scale)
    {
        string path = $"Assets/Project/Prefabs/VFX/{prefabName}.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        EnsureFolder("Assets/Project/Prefabs", "VFX");

        var root = new GameObject(prefabName);
        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = "VFX";

            var frames = new Sprite[6];
            int found = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = FindSprite("Assets/Project/Art/Sprites/VFX", sheet, $"{spritePrefix}_{i:00}");
                if (frames[i] != null) found++;
            }

            if (found == 0)
            {
                Debug.LogWarning($"[플레이어 프리팹] {spritePrefix} 스프라이트를 못 찾았다. " +
                                 "VFX 슬라이스를 먼저 실행해라.");
            }
            else
            {
                renderer.sprite = frames[0];
                root.transform.localScale = new Vector3(scale, scale, 1f);

                var animator = root.AddComponent<SpriteFrameAnimator>();
                var serialized = new SerializedObject(animator);
                var framesProperty = serialized.FindProperty("frames");
                framesProperty.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++)
                    framesProperty.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

                serialized.FindProperty("fps").floatValue = fps;
                serialized.FindProperty("loop").boolValue = false;
                serialized.FindProperty("destroyWhenFinished").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            return success ? saved : null;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>비어 있는 이펙트 참조를 다시 만들어 채운다. 채웠으면 true.</summary>
    private static bool RepairEffect(SerializedObject target, string propertyName,
        string prefabName, string sheet, string spritePrefix, float scale)
    {
        var property = target.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue != null) return false;

        property.objectReferenceValue =
            CreateOrLoadEffectPrefab(prefabName, sheet, spritePrefix, 14f, scale);
        return true;
    }

    /// <summary>
    /// 추가 생성 — Q 스킬(2단 판정 내려찍기) 에셋.
    ///
    /// 기존 Skill_Q_SwordSlam(단일 판정)을 덮어쓰지 않고 새 경로에 만든다.
    /// ScriptableObject는 타입을 바꿔 저장할 수 없어서, 같은 파일에 다른 타입을 쓰려 하면
    /// 참조가 깨진 채로 남는다. 예전 에셋은 지워도 되고 놔둬도 무해하다.
    /// </summary>
    private static SkillData CreateOrLoadGroundSlamSkill()
    {
        const string path = "Assets/Project/Data/Skills/Skill_Q_GroundSlam.asset";

        var existing = AssetDatabase.LoadAssetAtPath<SkillData>(path);
        if (existing != null)
        {
            // 다른 스킬과 같은 복구 분기. 이게 없어서 이펙트 프리팹을 지워도 안 돌아왔다.
            var repair = new SerializedObject(existing);
            bool changed = false;

            changed |= RepairEffect(repair, "nearEffect",
                "SlamImpact", "vfx_sword_slam_impact_6frames_1536x256", "vfx_slam_impact", 2f);
            changed |= RepairEffect(repair, "farEffect",
                "SlamBurst", "vfx_sword_slam_forward_burst_6frames_1536x256", "vfx_slam_burst", 3f);

            if (changed)
            {
                repair.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(existing);
                Debug.Log("[플레이어 프리팹] Q 스킬의 끊어진 이펙트 프리팹을 다시 만들어 연결했다.");
            }

            return existing;
        }

        EnsureFolder("Assets/Project/Data", "Skills");

        var skill = ScriptableObject.CreateInstance<GroundSlamSkillData>();
        var serialized = new SerializedObject(skill);
        serialized.FindProperty("displayName").stringValue = "잿불 대검 내려찍기";
        serialized.FindProperty("description").stringValue =
            "대검을 바닥에 내려찍는다. 발밑이 먼저 터지고, 이어서 앞으로 잿불이 번진다. " +
            "코앞에서 쓰면 두 번 다 맞는다.";
        serialized.FindProperty("cooldownSeconds").floatValue = 3f;
        serialized.FindProperty("motionSeconds").floatValue = 0.43f;
        serialized.FindProperty("damage").intValue = 0; // 단계별 데미지를 따로 쓴다
        serialized.FindProperty("animatorTrigger").stringValue =
            AshPlayerAnimationBuilder.ParamSwordSlam;
        serialized.FindProperty("targetLayers").intValue = 1 << LayerMask.NameToLayer("Enemy");

        serialized.FindProperty("nearEffect").objectReferenceValue = CreateOrLoadEffectPrefab(
            "SlamImpact", "vfx_sword_slam_impact_6frames_1536x256", "vfx_slam_impact", 14f, 2f);

        serialized.FindProperty("farEffect").objectReferenceValue = CreateOrLoadEffectPrefab(
            "SlamBurst", "vfx_sword_slam_forward_burst_6frames_1536x256", "vfx_slam_burst", 14f, 3f);

        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(skill, path);
        Debug.Log($"[플레이어 프리팹] Q 스킬(2단 내려찍기) 에셋을 새로 만들었다 → {path}");

        return skill;
    }

    /// <summary>추가 생성 — 왕의 잿불 폭발 이펙트 프리팹.</summary>
    private static GameObject CreateOrLoadUltimateEffectPrefab()
    {
        const string path = "Assets/Project/Prefabs/VFX/KingsEmber.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        EnsureFolder("Assets/Project/Prefabs", "VFX");

        var root = new GameObject("KingsEmber");
        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = "VFX";

            var frames = new Sprite[6];
            int found = 0;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = FindSprite("Assets/Project/Art/Sprites/VFX",
                                       "vfx_kings_ember_6frames_1536x256",
                                       $"vfx_kings_ember_{i:00}");
                if (frames[i] != null) found++;
            }

            if (found == 0)
            {
                Debug.LogWarning("[플레이어 프리팹] 왕의 잿불 스프라이트를 못 찾았다. " +
                                 "VFX 슬라이스를 먼저 실행해라.");
            }
            else
            {
                renderer.sprite = frames[0];

                // 필살기 폭발은 방 전체를 덮는 연출이라 이펙트를 크게 키운다.
                // 스프라이트 자체는 200px(약 2.5유닛)이라 그대로 쓰면 발밑 불티로 보인다.
                root.transform.localScale = new Vector3(6f, 6f, 1f);

                var animator = root.AddComponent<SpriteFrameAnimator>();
                var serialized = new SerializedObject(animator);
                var framesProperty = serialized.FindProperty("frames");
                framesProperty.arraySize = frames.Length;
                for (int i = 0; i < frames.Length; i++)
                    framesProperty.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];

                serialized.FindProperty("fps").floatValue = 8f;
                serialized.FindProperty("loop").boolValue = false;
                serialized.FindProperty("destroyWhenFinished").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success || saved == null) return null;

            Debug.Log($"[플레이어 프리팹] 왕의 잿불 프리팹 생성 (스프라이트 {found}프레임) → {path}");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 추가 생성 — R 스킬(왕의 잿불) 에셋.
    ///
    /// 장판 스킬을 그대로 쓴다. 다른 점은 <b>앞이 아니라 발밑</b>에 떨어지고 반경이
    /// 훨씬 넓다는 것뿐이라, 전용 타입을 새로 만들 이유가 없다.
    /// 이게 SkillData를 데이터 자산으로 둔 값어치다 — 같은 코드에 숫자만 다르게 준다.
    /// </summary>
    private static SkillData CreateOrLoadUltimateSkill()
    {
        const string path = "Assets/Project/Data/Skills/Skill_R_KingsEmber.asset";

        var existing = AssetDatabase.LoadAssetAtPath<SkillData>(path);
        if (existing != null)
        {
            var repair = new SerializedObject(existing);
            var effectProperty = repair.FindProperty("effectPrefab");

            if (effectProperty != null && effectProperty.objectReferenceValue == null)
            {
                effectProperty.objectReferenceValue = CreateOrLoadUltimateEffectPrefab();
                repair.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(existing);
            }

            return existing;
        }

        EnsureFolder("Assets/Project/Data", "Skills");

        var skill = ScriptableObject.CreateInstance<AreaSkillData>();
        var serialized = new SerializedObject(skill);
        serialized.FindProperty("displayName").stringValue = "왕의 잿불";
        serialized.FindProperty("description").stringValue =
            "대검을 바닥에 꽂아 잿더미의 왕의 힘을 잠깐 빌린다. 주변 모든 것이 재가 된다.";
        serialized.FindProperty("cooldownSeconds").floatValue = 20f;

        // ultimate 클립이 6프레임 / 10fps = 0.6초다.
        serialized.FindProperty("motionSeconds").floatValue = 0.6f;
        serialized.FindProperty("damage").intValue = 8;
        serialized.FindProperty("animatorTrigger").stringValue = AshPlayerAnimationBuilder.ParamUltimate;
        serialized.FindProperty("targetLayers").intValue = 1 << LayerMask.NameToLayer("Enemy");

        // 발밑에서 터진다. 방 세로가 28유닛이라 반경 14면 화면 대부분을 덮는다.
        serialized.FindProperty("forwardDistance").floatValue = 0f;
        serialized.FindProperty("radius").floatValue = 14f;

        // 검을 내리꽂는 4번 프레임(3/10 = 0.3초)에 이펙트가 깔리고 곧바로 터진다.
        // 예고 시간을 짧게 둔 이유: 필살기는 피하라고 쓰는 게 아니라 판을 뒤집는 것이다.
        serialized.FindProperty("castDelay").floatValue = 0.3f;
        serialized.FindProperty("explodeDelay").floatValue = 0.15f;

        serialized.FindProperty("effectPrefab").objectReferenceValue = CreateOrLoadUltimateEffectPrefab();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(skill, path);
        Debug.Log($"[플레이어 프리팹] R 스킬(왕의 잿불) 에셋을 새로 만들었다 → {path}");

        return skill;
    }

    /// <summary>추가 생성 — E 스킬(잿불 기둥) 에셋.</summary>
    private static SkillData CreateOrLoadStaffSkill()
    {
        const string path = "Assets/Project/Data/Skills/Skill_E_AshPillar.asset";

        var existing = AssetDatabase.LoadAssetAtPath<SkillData>(path);
        if (existing != null)
        {
            // 화살과 같은 이유로 끊어진 이펙트 참조만 다시 채운다.
            var repair = new SerializedObject(existing);
            var effectProperty = repair.FindProperty("effectPrefab");

            if (effectProperty != null && effectProperty.objectReferenceValue == null)
            {
                effectProperty.objectReferenceValue = CreateOrLoadSpellEffectPrefab();
                repair.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(existing);
            }

            return existing;
        }

        EnsureFolder("Assets/Project/Data", "Skills");

        var skill = ScriptableObject.CreateInstance<AreaSkillData>();
        var serialized = new SerializedObject(skill);
        serialized.FindProperty("displayName").stringValue = "잿불 기둥";
        serialized.FindProperty("description").stringValue =
            "앞쪽 바닥에 잿불을 심는다. 잠시 뒤 기둥이 솟아 그 자리를 덮친다.";
        serialized.FindProperty("cooldownSeconds").floatValue = 3f;
        serialized.FindProperty("motionSeconds").floatValue = 0.43f;
        serialized.FindProperty("damage").intValue = 4;
        serialized.FindProperty("animatorTrigger").stringValue = AshPlayerAnimationBuilder.ParamStaff;
        serialized.FindProperty("targetLayers").intValue = 1 << LayerMask.NameToLayer("Enemy");
        serialized.FindProperty("effectPrefab").objectReferenceValue = CreateOrLoadSpellEffectPrefab();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(skill, path);
        Debug.Log($"[플레이어 프리팹] E 스킬(잿불 기둥) 에셋을 새로 만들었다 → {path}");

        return skill;
    }

    /// <summary>추가 생성 — W 스킬(잿가루 화살) 에셋.</summary>
    private static SkillData CreateOrLoadBowSkill()
    {
        const string path = "Assets/Project/Data/Skills/Skill_W_EmberArrow.asset";

        var existing = AssetDatabase.LoadAssetAtPath<SkillData>(path);
        if (existing != null)
        {
            // 밸런스 값은 건드리지 않되, <b>끊어진 프리팹 참조만 다시 채운다.</b>
            //
            // 이 분기가 필요한 이유: 화살 프리팹을 지우고 다시 만들면 GUID가 바뀌어서
            // 스킬 에셋의 참조가 끊긴다. 그런데 에셋이 이미 있다는 이유로 아무것도 안 하면
            // "투사체 프리팹이 비어 있다" 경고만 뜨고 W가 영영 안 나간다.
            // 에셋을 지웠다 다시 만들면 조정해둔 밸런스가 날아가므로 참조만 고친다.
            var repair = new SerializedObject(existing);
            var prefabProperty = repair.FindProperty("projectilePrefab");

            if (prefabProperty != null && prefabProperty.objectReferenceValue == null)
            {
                prefabProperty.objectReferenceValue = CreateOrLoadArrowPrefab();
                repair.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(existing);
                Debug.Log("[플레이어 프리팹] W 스킬의 끊어진 화살 프리팹 참조를 다시 연결했다.");
            }

            return existing;
        }

        EnsureFolder("Assets/Project/Data", "Skills");

        var skill = ScriptableObject.CreateInstance<ProjectileSkillData>();
        var serialized = new SerializedObject(skill);
        serialized.FindProperty("displayName").stringValue = "잿가루 화살";
        serialized.FindProperty("description").stringValue =
            "재로 만든 활을 불러내 잉걸 화살을 쏜다. 붙기 전에 깎는 견제기다.";
        serialized.FindProperty("cooldownSeconds").floatValue = 1.2f;
        serialized.FindProperty("motionSeconds").floatValue = 0.43f;
        serialized.FindProperty("damage").intValue = 2;
        serialized.FindProperty("animatorTrigger").stringValue = AshPlayerAnimationBuilder.ParamBow;
        serialized.FindProperty("projectilePrefab").objectReferenceValue = CreateOrLoadArrowPrefab();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(skill, path);
        Debug.Log($"[플레이어 프리팹] W 스킬(화살) 에셋을 새로 만들었다 → {path}");

        return skill;
    }

    private static SkillData CreateOrLoadSwordSlamSkill()
    {
        const string path = "Assets/Project/Data/Skills/Skill_Q_SwordSlam.asset";

        var existing = AssetDatabase.LoadAssetAtPath<SkillData>(path);
        if (existing != null) return existing;

        EnsureFolder("Assets/Project/Data", "Skills");

        var skill = ScriptableObject.CreateInstance<MeleeSkillData>();
        var serialized = new SerializedObject(skill);
        serialized.FindProperty("displayName").stringValue = "잿불 대검 내려찍기";
        serialized.FindProperty("description").stringValue =
            "대검을 머리 위로 들어 바닥에 내려찍는다. 박힌 지점에서 앞으로 잿불이 터진다.";
        serialized.FindProperty("cooldownSeconds").floatValue = 3f;
        serialized.FindProperty("motionSeconds").floatValue = 0.43f;
        serialized.FindProperty("damage").intValue = 5;
        serialized.FindProperty("animatorTrigger").stringValue =
            AshPlayerAnimationBuilder.ParamSwordSlam;

        // 검이 바닥에 닿는 4프레임(3/14 = 0.214초)에 판정이 켜진다.
        serialized.FindProperty("hitboxDelay").floatValue = 0.214f;
        serialized.FindProperty("hitboxDuration").floatValue = 0.2f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(skill, path);
        Debug.Log($"[플레이어 프리팹] Q 스킬(내려찍기) 에셋을 새로 만들었다 → {path}");

        return skill;
    }

    private static SkillData CreateOrLoadSlashSkill()
    {
        const string folder = "Assets/Project/Data/Skills";
        const string path = folder + "/Skill_Basic_AshSlash.asset";

        var existing = AssetDatabase.LoadAssetAtPath<SkillData>(path);
        if (existing != null) return existing;

        EnsureFolder("Assets/Project/Data", "Skills");

        var skill = ScriptableObject.CreateInstance<MeleeSkillData>();

        var serialized = new SerializedObject(skill);
        serialized.FindProperty("displayName").stringValue = "잿불 베기";
        serialized.FindProperty("description").stringValue =
            "대검을 휘둘러 앞을 벤다. 맞은 적은 뒤로 밀려난다.";

        // 0.65초는 전투 리듬을 맞춘 값이다. 모션 0.43초가 끝난 뒤부터 세므로 한 주기가
        // 약 1.08초이고, 적이 맞고 물러나 있는 시간(경직 0.2 + 숨 고르기 0.7 = 0.9초)과
        // 비슷해서 "쳤다 → 물러났다 → 붙는다 → 다시 친다"가 맞물린다.
        serialized.FindProperty("cooldownSeconds").floatValue = 0.65f;

        // attack 클립이 6프레임 / 14fps = 약 0.43초다. 이 값과 어긋나면 모션이 끝났는데도
        // 못 움직이거나 그 반대가 된다.
        serialized.FindProperty("motionSeconds").floatValue = 0.43f;
        serialized.FindProperty("damage").intValue = 2;
        serialized.FindProperty("hitboxDelay").floatValue = 0.12f;
        serialized.FindProperty("hitboxDuration").floatValue = 0.18f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(skill, path);
        Debug.Log($"[플레이어 프리팹] Q 스킬 에셋을 새로 만들었다 → {path}");

        return skill;
    }

    /// <summary>추가 생성 — 플레이어 앞쪽에 검 공격용 트리거를 만든다.</summary>
    private static DamageHitbox CreateAttackHitbox(GameObject root)
    {
        var hitboxObject = new GameObject("AttackHitbox");
        hitboxObject.transform.SetParent(root.transform, false);
        hitboxObject.layer = LayerMask.NameToLayer("PlayerAttack");

        var collider = hitboxObject.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;

        // 수정(공격 판정 점검 시점): 세로 위치를 0.45 → 0.12, 높이를 0.45 → 0.55로 바꿨다.
        //
        // 이전 값은 히트박스를 스프라이트의 <b>가슴 높이</b>(y = 0.45 x 키 = 3.0유닛)에 뒀다.
        // 횡스크롤이면 맞지만 이 게임은 탑다운이라 y축이 높이가 아니라 <b>바닥 위치</b>다.
        // 적의 몸통 콜라이더는 발치(y 0 ~ 1.65)에 제대로 있는데 검 판정만 공중(1.50 ~ 4.50)에
        // 떠 있어서, 둘이 0.15유닛밖에 안 겹쳤다. 그것도 두 캐릭터의 y가 정확히 같을 때만이고,
        // 탑다운에서는 항상 위아래로 어긋나 있으므로 실제로는 검이 그냥 통과했다.
        //
        // 세로를 0.55로 넉넉히 잡은 이유: 탑다운에서 y 차이는 곧 "떨어져 있는 거리"다.
        // 판정이 얇으면 살짝 위아래로 비껴 선 적을 못 때린다.
        // 가로 사거리(0.75 x 키 = 5.0, 중심 3.2 → 끝 5.7유닛)는 그대로 둔다. 적의 돌진
        // 사거리(4.11유닛)보다 이미 길다.
        collider.size = new Vector2(CharacterHeightUnits * 0.75f, CharacterHeightUnits * 0.55f);
        collider.offset = new Vector2(CharacterHeightUnits * 0.48f, CharacterHeightUnits * 0.12f);

        var hitbox = hitboxObject.AddComponent<DamageHitbox>();
        var serialized = new SerializedObject(hitbox);
        serialized.FindProperty("damage").intValue = 1;
        serialized.FindProperty("targetLayers").intValue = 1 << LayerMask.NameToLayer("Enemy");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return hitbox;
    }

    /// <summary>
    /// PlayerController의 [SerializeField] 참조들을 연결한다.
    ///
    /// SerializedObject를 쓰는 이유: 그 필드들이 private이라 에디터 스크립트에서 직접 대입할 수
    /// 없다. public으로 열면 다른 코드가 실행 중에 바꿀 수 있게 되는데, 참조는 조립 시점에만
    /// 정해져야 하는 값이라 그게 더 나쁘다.
    /// </summary>
    private static void LinkReferences(
        PlayerController playerController, GameObject root, PlayerStamina stamina,
        Health health, DamageHitbox attackHitbox)
    {
        var serialized = new SerializedObject(playerController);

        serialized.FindProperty("animator").objectReferenceValue = root.GetComponent<Animator>();
        serialized.FindProperty("spriteRenderer").objectReferenceValue = root.GetComponent<SpriteRenderer>();
        serialized.FindProperty("stamina").objectReferenceValue = stamina;
        serialized.FindProperty("health").objectReferenceValue = health;
        serialized.FindProperty("attackHitbox").objectReferenceValue = attackHitbox;

        // runManager는 씬 오브젝트라 프리팹에 담을 수 없다. PlayerController.Awake가
        // 실행 시점에 찾는다.

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>시트에서 잘린 스프라이트 하나를 이름으로 찾는다. 없으면 null.</summary>
    private static Sprite FindSprite(string folderPath, string sheetFileName, string spriteName)
    {
        string path = $"{folderPath}/{sheetFileName}.png";

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Sprite sprite && sprite.name == spriteName)
                return sprite;
        }

        return null;
    }

    /// <summary>폴더가 없으면 만든다.</summary>
    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
