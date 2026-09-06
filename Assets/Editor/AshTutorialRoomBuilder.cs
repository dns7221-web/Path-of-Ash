using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Game 씬에 튜토리얼 방을 구성한다.
///
/// 메뉴: Tools → 재의 길 → 튜토리얼 방 생성
///
/// 별도 씬이 아니라 방으로 만드는 이유:
/// 튜토리얼은 <b>진짜 플레이어와 진짜 HUD</b>로 가르쳐야 의미가 있다. 씬을 나누면 플레이어·
/// 체력바·스태미나·스킬바를 전부 복제해야 하고, 복제본이 원본과 조금이라도 어긋나면
/// "튜토리얼에서 배운 게 본편에서 안 통하는" 가장 나쁜 상황이 된다.
/// 방으로 두면 그 위험이 구조적으로 없고, 문을 나가면 그대로 던전이 이어진다.
///
/// 적도 상자도 두지 않는 이유:
/// 처음 조작을 배우는 자리에서 전투부터 시키면 배우기 전에 죽는다. 그리고 적이 없으면
/// 상자를 띄울 신호(전투 종료)도 없으니 상자만 남겨봐야 영영 안 나타난다.
///
/// 대신 RoomController의 startUnlocked를 켠다. 이게 없으면 문을 여는 유일한 경로
/// (전투 → 보상 → 문)가 끊겨서 <b>플레이어가 방에 갇힌다.</b>
/// </summary>
public static class AshTutorialRoomBuilder
{
    private const string TutorialRoomName = "Room_Tutorial";
    private const string CampSpritePath =
        "Assets/Project/Art/Environment/Rooms/ash-king-safe-camp-room.png";

    private const string GuideRootName = "TutorialGuide";

    /// <summary>
    /// 새로 만드는 줄의 기본 글자 크기.
    ///
    /// 이미 있는 줄에는 안 넣는다 — 사람이 인스펙터에서 키워둔 값을 되돌리면 안 된다.
    /// 처음부터 크게 나오길 원하면 이 값을 올리고 줄을 지운 뒤 도구를 다시 돌리면 된다.
    /// </summary>
    private const float DefaultGuideFontSize = 6f;

    /// <summary>안내 문구용 한글 폰트. 설정 화면과 같은 96pt 아틀라스를 쓴다.</summary>
    private const string GuideFontPath =
        "Assets/Project/Art/UI/Fonts/NeoDunggeunmoPro-Regular96.asset";


    /// <summary>
    /// 안내 문구. 방 안에서 위에서 아래로 이 순서로 놓인다.
    ///
    /// <b>키를 글자로 안 적는다.</b> 중괄호 안에 액션 이름을 적으면 실행 중에 실제 키로
    /// 바뀐다(<see cref="ControlHintLabel"/>). 플레이어가 Q를 A로 바꾸면 이 안내도 A가 된다.
    ///
    /// 예전에는 키를 글자로 적어두고 "실제 바인딩과 반드시 일치해야 한다"는 주석을 달아뒀는데,
    /// 그러고도 어긋났다 — 대시가 Shift로 옮겨간 뒤에도 안내는 "Ctrl - 대시"였고, 정작
    /// Ctrl은 기본 공격이 됐는데 공격 안내는 아예 없었다. 첫 화면에서 틀린 조작을 가르치면
    /// 플레이어는 게임이 고장났다고 판단한다. <b>사람이 지키기로 한 규칙은 지켜지지 않는다.</b>
    ///
    /// 이동만 글자로 남는 이유: 키 네 개가 한 조작(복합 바인딩)이라 자리표시자 하나로
    /// 표현할 수 없다. 그래서 이동은 리바인딩 대상에서도 빠져 있다.
    ///
    /// <b>한 줄에 두 조작을 몰지 않는다.</b> 예전에 이동과 대시를 한 줄에 합쳤더니,
    /// 키 이름이 길어졌을 때(Left Shift / Right Shift) 한 줄이 방 밖으로 넘쳤다.
    /// 인스펙터에서 줄바꿈을 넣어 고쳐도 실행하면 이 표의 문구가 덮어써서 되돌아간다.
    /// <b>줄을 나누는 일은 문구의 주인인 여기서 해야 한다.</b> 줄마다 오브젝트가 따로
    /// 생기므로 위치도 줄 단위로 맞출 수 있다.
    /// </summary>
    private static readonly string[] GuideLines =
    {
        "방향키 - 이동",
        "{Dash} - 대시",
        "{BasicAttack} - 공격",
        "{Skill1} {Skill2} {Skill3} {Skill4} - 스킬",
    };

    [MenuItem("Tools/재의 길/튜토리얼 방 생성")]
    public static void Build()
    {
        // FindObjectsInactive.Include가 필요한 이유: 기본값은 꺼진 오브젝트를 건너뛴다.
        // RoomSequence가 꺼져 있거나 꺼진 부모 밑에 있으면 "씬에 없다"는 잘못된 결론이 난다.
        var sequence = Object.FindFirstObjectByType<RoomSequenceController>(FindObjectsInactive.Include);
        if (sequence == null)
        {
            string sceneName = EditorSceneManager.GetActiveScene().name;
            Debug.LogError($"[튜토리얼] RoomSequenceController를 못 찾았다. 지금 열린 씬: '{sceneName}'\n" +
                           "Game 씬을 열고 다시 실행해라.");
            return;
        }

        var sequenceObject = new SerializedObject(sequence);
        SerializedProperty tutorialProperty = sequenceObject.FindProperty("tutorialRoom");
        if (tutorialProperty == null)
        {
            Debug.LogError("[튜토리얼] RoomSequenceController에 tutorialRoom 항목이 없다. 스크립트 재컴파일을 기다려라.");
            return;
        }

        // 이미 있으면 새로 만들지 않고 설정만 다시 적용한다.
        // 새로 만들면 손으로 맞춘 소품 배치가 날아가고, 그냥 넘어가면 설정이 영영 안 들어간다.
        if (tutorialProperty.objectReferenceValue is RoomController existing)
        {
            ApplyTutorialSettings(existing);

            // 배경도 같이 되돌린다. 예전에는 설정만 다시 넣었는데, 그러면 배경이 한 번
            // 던전 그림으로 돌아간 뒤에는 이 메뉴로 복구할 방법이 없었다.
            SwapBackground(existing);

            // 출구 위치도 같이 맞춘다. 배경만 바꾸면 문은 그림 아래인데 판정은 위에 남는다.
            LayoutTutorialRoom(existing);

            // 추가 생성 — 안내 문구도 다시 만든다.
            //
            // 예전에는 이 갈래에서 안내를 건너뛰었다. 그래서 <b>도구를 아무리 다시 돌려도
            // 안내는 손도 안 댄 상태로 남았다.</b> 조작이 바뀌어도 안내가 옛 키를 말하는
            // 상황이 여기서 나왔다. "설정을 다시 적용한다"는 이 갈래의 목적에 안내도 포함된다.
            //
            // 손으로 고친 문구가 있으면 덮어쓴다. 그게 의도다 — 안내가 실제 조작을 따라가려면
            // 문구의 주인이 하나여야 하고, 그 주인은 GuideLines다.
            AddGuideText(existing);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[튜토리얼] 이미 있는 {existing.name}에 설정·배경·안내를 다시 적용했다.", existing);
            return;
        }

        RoomController template = FindTemplateRoom(sequenceObject);
        if (template == null)
        {
            Debug.LogError("[튜토리얼] 복제할 일반 방이 없다. rooms 배열에 방을 먼저 등록해라.");
            return;
        }

        GameObject clone = Object.Instantiate(template.gameObject, template.transform.parent);
        clone.name = TutorialRoomName;
        clone.transform.SetPositionAndRotation(template.transform.position, template.transform.rotation);
        clone.transform.localScale = template.transform.localScale;
        Undo.RegisterCreatedObjectUndo(clone, "튜토리얼 방 생성");

        var room = clone.GetComponent<RoomController>();
        if (room == null)
        {
            Debug.LogError("[튜토리얼] 복제본에 RoomController가 없다.", clone);
            Undo.DestroyObjectImmediate(clone);
            return;
        }

        ApplyTutorialSettings(room);
        SwapBackground(room);
        LayoutTutorialRoom(room);
        AddGuideText(room);

        tutorialProperty.objectReferenceValue = room;
        sequenceObject.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = clone;
        Debug.Log($"[튜토리얼] {template.name}을 복제해 {TutorialRoomName}을 만들고 RoomSequence에 연결했다.\n" +
                  "판을 시작하면 이 방부터 들어가고, 문을 나가면 던전이 시작된다.");
    }

    /// <summary>rooms 배열에서 복제할 기준 방을 고른다.</summary>
    private static RoomController FindTemplateRoom(SerializedObject sequenceObject)
    {
        SerializedProperty rooms = sequenceObject.FindProperty("rooms");
        if (rooms == null) return null;

        for (int i = 0; i < rooms.arraySize; i++)
        {
            if (rooms.GetArrayElementAtIndex(i).objectReferenceValue is RoomController room)
                return room;
        }
        return null;
    }

    /// <summary>
    /// 튜토리얼 방을 "전투도 보상도 없는 방"으로 만든다.
    ///
    /// 적과 상자를 함께 빼는 이유: 처음 조작을 배우는 자리에서 전투부터 시키면 배우기 전에
    /// 죽는다. 그리고 적이 없으면 상자를 띄울 신호(전투 종료)도 없으니 상자만 남겨봐야
    /// 영영 안 나타난다. 둘은 같이 있거나 같이 없어야 한다.
    ///
    /// 대신 <c>startUnlocked</c>를 켜서 입장 순간부터 문을 열어둔다. 이게 없으면
    /// 문을 여는 유일한 경로(전투 → 보상)가 끊겨서 플레이어가 방에 갇힌다.
    ///
    /// 컴포넌트를 지우지 않고 참조만 끊는 이유: 나중에 튜토리얼에 적을 다시 넣고 싶어지면
    /// 인스펙터에서 참조를 도로 꽂기만 하면 된다. 지워버리면 다시 만들어야 한다.
    /// </summary>
    private static void ApplyTutorialSettings(RoomController room)
    {
        var roomObject = new SerializedObject(room);

        SerializedProperty encounter = roomObject.FindProperty("encounter");
        SerializedProperty reward = roomObject.FindProperty("reward");
        SerializedProperty unlocked = roomObject.FindProperty("startUnlocked");

        if (encounter != null) encounter.objectReferenceValue = null;
        if (reward != null) reward.objectReferenceValue = null;
        if (unlocked != null) unlocked.boolValue = true;

        roomObject.ApplyModifiedPropertiesWithoutUndo();

        // 참조를 끊는 것만으로는 부족하다. 스포너와 상자 오브젝트가 켜져 있으면
        // 자기 Start에서 스스로 적을 뽑고 상자를 띄운다.
        var spawner = room.GetComponentInChildren<EnemySpawner>(true);
        if (spawner != null) spawner.gameObject.SetActive(false);

        var chest = room.GetComponentInChildren<RewardChest>(true);
        if (chest != null) chest.gameObject.SetActive(false);
    }

    /// <summary>
    /// 출구와 입장 지점을 캠프 방 그림에 맞게 옮긴다.
    ///
    /// 왜 필요한가: 이 방은 던전 방을 복제해 만들었고, 던전 방은 <b>문이 위쪽</b>에 있다.
    /// 그런데 캠프 방 그림은 문이 <b>아래쪽</b>이다. 그대로 두면 눈에 보이는 아래 문으로
    /// 걸어가도 아무 일이 없고, 정작 위쪽 허공에 보이지 않는 출구가 남는다.
    ///
    /// 좌표를 숫자로 박지 않고 배경 경계에서 계산하는 이유:
    /// 캠프 방 그림(1920x1080)은 던전 방(1678x937)과 크기가 달라서, 던전 기준 좌표를
    /// 그대로 쓰면 어긋난다. 그림이 바뀌어도 이 도구를 다시 돌리면 맞는다.
    /// </summary>
    private static void LayoutTutorialRoom(RoomController room)
    {
        var door = room.GetComponentInChildren<RoomDoorState>(true);
        SpriteRenderer renderer = door != null
            ? door.GetComponentInChildren<SpriteRenderer>(true)
            : room.GetComponentInChildren<SpriteRenderer>(true);

        if (renderer == null || renderer.sprite == null)
        {
            Debug.LogWarning("[튜토리얼] 배경 렌더러를 못 찾아 출구를 못 옮겼다.", room);
            return;
        }

        // 그림 가장자리에서 벽 안쪽까지의 여백. 보스 방에서 실측한 값과 같은 규칙이다.
        const float insetPixels = 100f;
        const float pixelsPerUnit = 32f;
        float inset = insetPixels / pixelsPerUnit;

        Bounds bounds = renderer.bounds;
        float bottom = bounds.min.y + inset;
        float top = bounds.max.y - inset;
        float height = top - bottom;
        float centerX = bounds.center.x;

        // 출구는 아래 문 바로 앞. 플레이어는 방 가운데쯤에서 시작해 안내를 읽으며 내려온다.
        MoveChild(room, "DoorExitTrigger", new Vector2(centerX, bottom + height * 0.05f));
        MoveChild(room, "PlayerEntryPoint", new Vector2(centerX, bottom + height * 0.5f));

        Debug.Log($"[튜토리얼] 출구를 아래 문 앞으로 옮겼다 (바닥 {height:F1}유닛).", room);
    }

    /// <summary>이름으로 찾은 자식을 옮긴다. 없으면 조용히 넘어간다.</summary>
    private static void MoveChild(RoomController room, string childName, Vector2 position)
    {
        foreach (Transform candidate in room.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name != childName) continue;

            Undo.RecordObject(candidate, "튜토리얼 방 배치");
            candidate.position = new Vector3(position.x, position.y, candidate.position.z);
            return;
        }
    }

    /// <summary>
    /// 배경을 안전 캠프 그림으로 바꾼다.
    ///
    /// SpriteRenderer가 아니라 <see cref="RoomDoorState"/>의 슬롯을 바꾸는 이유:
    /// 그 컴포넌트가 방 상태에 따라 렌더러를 매번 다시 칠한다. 렌더러만 바꾸면
    /// 실행하는 순간 던전 방 그림으로 되돌아간다.
    /// </summary>
    private static void SwapBackground(RoomController room)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(CampSpritePath);
        var door = room.GetComponentInChildren<RoomDoorState>(true);

        if (sprite == null || door == null)
        {
            Debug.LogWarning($"[튜토리얼] 캠프 배경을 못 바꿨다. 던전 방 그림 그대로 둔다.\n{CampSpritePath}", room);
            return;
        }

        var doorObject = new SerializedObject(door);
        foreach (string slot in new[] { "closedRoom", "openRoom", "brokenRoom" })
        {
            SerializedProperty property = doorObject.FindProperty(slot);
            if (property != null) property.objectReferenceValue = sprite;
        }
        doorObject.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 바닥에 안내 문구를 놓는다.
    ///
    /// 별도 UI 캔버스를 만들지 않고 월드 텍스트를 쓰는 이유:
    /// 캔버스는 화면에 고정돼서 던전에 들어가도 따라온다. 방에 놓인 글자는 그 방을 나가면
    /// 자연스럽게 사라지고, 방을 껐다 켜는 기존 구조에 그대로 얹힌다.
    ///
    /// <b>이미 있는 줄은 만들지 않고 문구만 다시 넣는다.</b>
    ///
    /// 예전에는 통째로 지우고 새로 만들었다. 그러면 도구를 돌릴 때마다 <b>손으로 맞춰둔
    /// 글자 크기와 위치가 전부 날아간다.</b> 방 바닥의 글자는 배경 그림과 캐릭터 사이에
    /// 놓이는 것이라 계산으로는 못 맞추고 사람이 눈으로 맞춰야 하는데, 그렇게 맞춘 값을
    /// 도구가 되돌리면 조정할 때마다 다시 맞춰야 한다. 소품 배치 도구와 같은 판단이다.
    ///
    /// 그래서 이 함수가 <b>매번 책임지는 것은 문구 하나뿐</b>이다. 크기·위치·색·정렬은
    /// 새로 만들 때만 기본값을 넣고, 그 뒤로는 사람이 정한 값을 존중한다.
    /// </summary>
    private static void AddGuideText(RoomController room)
    {
        var renderer = room.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null) return;

        Bounds bounds = renderer.bounds;

        // 뿌리는 있으면 그대로 쓴다. 지우면 그 밑의 줄들이 같이 사라진다.
        Transform guideRoot = room.transform.Find(GuideRootName);
        if (guideRoot == null)
        {
            var created = new GameObject(GuideRootName);
            created.transform.SetParent(room.transform, false);
            guideRoot = created.transform;
        }

        TMP_FontAsset font = FindFont();

        // 키 문구는 줄마다 같은 값을 쓰므로 루프 밖에서 한 번만 만든다.
        // 안에서 만들면 줄 수만큼 액션 맵을 들여다보게 되고, 그때마다 경고가 반복해서 찍힌다.
        for (int i = 0; i < GuideLines.Length; i++)
        {
            string lineName = $"Guide_{i}";

            // Transform을 들고 다니지 않고 GameObject로 받는다.
            //
            // 예전에는 Transform 변수를 만들어 여러 단계를 거친 뒤 GetComponent를 불렀는데,
            // 그 사이에 대상이 사라지면 <b>MissingReferenceException</b>이 났다. 유니티에서
            // 파괴된 오브젝트는 <c>== null</c>로 걸러지므로, 쓰기 직전에 한 번 더 확인하면
            // 예외 대신 "이 줄을 건너뛴다"는 안전한 결과가 된다.
            GameObject lineObject = FindChild(guideRoot, lineName);

            // 새로 만드는 줄에만 기본 배치와 모양을 넣는다.
            if (lineObject == null)
            {
                lineObject = new GameObject(lineName);
                lineObject.transform.SetParent(guideRoot, false);

                // 방 위쪽부터 아래로 고르게 배치한다. 걸어 내려오면서 차례로 읽히게 하려는 것이다.
                float t = (i + 1f) / (GuideLines.Length + 1f);
                float y = Mathf.Lerp(bounds.max.y * 0.55f, bounds.min.y * 0.45f, t);
                lineObject.transform.position = new Vector3(bounds.center.x, y, 0f);

                var newText = lineObject.AddComponent<TextMeshPro>();
                newText.fontSize = DefaultGuideFontSize;
                newText.alignment = TextAlignmentOptions.Center;
                newText.color = new Color(0.9f, 0.85f, 0.75f, 0.85f);

                // 바닥 그림 위, 캐릭터 아래에 그린다. 글자가 캐릭터를 가리면 전투가 안 보인다.
                var meshRenderer = lineObject.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.sortingLayerID = renderer.sortingLayerID;
                    meshRenderer.sortingOrder = renderer.sortingOrder + 1;
                }
            }

            // 만들거나 찾은 직후에 한 번 확인한다. 여기서 걸리면 예외 대신 경고로 끝난다.
            if (lineObject == null)
            {
                Debug.LogWarning($"[튜토리얼] {lineName}을 만들지 못했다. 이 줄은 건너뛴다.");
                continue;
            }

            var text = lineObject.GetComponent<TextMeshPro>();
            if (text == null) text = lineObject.AddComponent<TextMeshPro>();

            // 폰트만은 이미 있는 줄에도 다시 넣는다. 한글이 없는 폰트가 걸려 있으면
            // 글자가 두부(□)로 나오는데, 그건 사람이 고른 값이 아니라 옛 도구의 실수다.
            if (font != null) text.font = font;

            // 자리표시자를 실제 키로 바꿔주는 컴포넌트. 실행 중 키가 바뀌면 스스로 다시 쓴다.
            var hint = lineObject.GetComponent<ControlHintLabel>();
            if (hint == null) hint = lineObject.AddComponent<ControlHintLabel>();

            if (hint != null) hint.SetTemplate(GuideLines[i]);

            // 에디터에서도 보이도록 지금 한 번 채워둔다 — 방 배치를 눈으로 맞춰야 한다.
            text.text = ControlHintLabel.Fill(GuideLines[i]);

            // 인스펙터를 거치지 않고 필드를 바꿨으므로 저장 대상이라고 알려준다.
            // 이게 없으면 씬을 저장해도 template이 빈 채로 남을 수 있다.
            if (hint != null) EditorUtility.SetDirty(hint);
            EditorUtility.SetDirty(text);
        }

        RemoveExtraGuideLines(guideRoot);
    }

    /// <summary>
    /// 자식을 이름으로 찾는다. 파괴된 것은 없는 것으로 본다.
    ///
    /// <see cref="Transform.Find"/>를 그대로 쓰지 않는 이유: 반환값을 여러 단계 뒤에
    /// 쓰다 보면 그사이에 사라진 대상을 만지게 될 수 있다. 여기서 GameObject로 바꿔
    /// 돌려주면, 유니티의 null 검사가 파괴된 것을 걸러낸다.
    /// </summary>
    private static GameObject FindChild(Transform parent, string name)
    {
        if (parent == null) return null;

        Transform found = parent.Find(name);
        return found == null ? null : found.gameObject;
    }

    /// <summary>
    /// 문구 수보다 많이 남아 있는 줄을 치운다.
    ///
    /// 그냥 지우지 않고 무엇을 지웠는지 로그로 남기는 이유: 손으로 추가한 줄이 섞여 있을 수
    /// 있다. 조용히 사라지면 "내가 쓴 안내가 어디 갔지"를 추적할 방법이 없다.
    /// </summary>
    private static void RemoveExtraGuideLines(Transform guideRoot)
    {
        for (int i = GuideLines.Length; ; i++)
        {
            Transform extra = guideRoot.Find($"Guide_{i}");
            if (extra == null) break;

            var text = extra.GetComponent<TextMeshPro>();
            string had = text != null ? text.text : string.Empty;

            Debug.LogWarning($"[튜토리얼] 남는 안내 줄 Guide_{i}을 치웠다. 적혀 있던 문구: [{had}] " +
                             "이 줄이 필요하면 AshTutorialRoomBuilder.GuideLines에 추가해라.");

            Object.DestroyImmediate(extra.gameObject);
        }
    }

    /// <summary>
    /// 안내 문구에 쓸 한글 폰트를 찾는다.
    ///
    /// <b>경로로 못 박는 이유.</b> 예전에는 프로젝트의 TMP 폰트를 검색해서 <b>첫 번째 것</b>을
    /// 썼다. 그 순서는 우리가 정하는 것이 아니라 GUID 순이라, TMP가 기본으로 넣어주는
    /// LiberationSans가 걸리면 <b>한글이 전부 두부(□)로 나온다.</b> 폰트에 없는 글자는
    /// 에러도 경고도 없이 네모로 그려져서, 코드만 보면 멀쩡하고 화면에서만 깨진다.
    ///
    /// 96pt 아틀라스를 쓰는 것은 설정 화면과 같은 이유다 — 방 바닥에 크게 놓이는 글자라
    /// 작은 아틀라스를 늘리면 픽셀이 뭉개진다.
    /// </summary>
    private static TMP_FontAsset FindFont()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GuideFontPath);
        if (font != null) return font;

        // 폰트 하나 때문에 방 전체가 안 만들어지는 편이 더 나쁘다. 경고만 남기고 진행한다.
        Debug.LogWarning("[튜토리얼] 한글 폰트를 못 찾았다. 안내 글자가 깨질 수 있다: " + GuideFontPath);
        return null;
    }
}
