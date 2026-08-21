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


    /// <summary>안내 문구. 방 안에서 위에서 아래로 이 순서로 놓인다.</summary>
    // 실제 바인딩과 반드시 일치해야 한다. PlayerController는 이동을 방향키에만 걸어뒀고
    // WASD는 없다. 튜토리얼이 없는 키를 가르치면 첫 화면에서 조작을 못 하게 된다.
    private static readonly string[] GuideLines =
    {
        "방향키 - 이동",
        "Ctrl - 대시 (회피)",
        "Q W E R - 스킬",
        "아래 문으로 나가면 던전이 시작된다",
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

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[튜토리얼] 이미 있는 {existing.name}에 설정과 배경을 다시 적용했다.", existing);
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
    /// </summary>
    private static void AddGuideText(RoomController room)
    {
        var renderer = room.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null) return;

        Bounds bounds = renderer.bounds;
        var guideRoot = new GameObject("TutorialGuide");
        guideRoot.transform.SetParent(room.transform, false);

        TMP_FontAsset font = FindFont();

        for (int i = 0; i < GuideLines.Length; i++)
        {
            var line = new GameObject($"Guide_{i}");
            line.transform.SetParent(guideRoot.transform, false);

            // 방 위쪽부터 아래로 고르게 배치한다. 걸어 내려오면서 차례로 읽히게 하려는 것이다.
            float t = (i + 1f) / (GuideLines.Length + 1f);
            float y = Mathf.Lerp(bounds.max.y * 0.55f, bounds.min.y * 0.45f, t);
            line.transform.position = new Vector3(bounds.center.x, y, 0f);

            var text = line.AddComponent<TextMeshPro>();
            text.text = GuideLines[i];
            text.fontSize = 6f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.9f, 0.85f, 0.75f, 0.85f);
            if (font != null) text.font = font;

            // 바닥 그림 위, 캐릭터 아래에 그린다. 글자가 캐릭터를 가리면 전투가 안 보인다.
            var meshRenderer = line.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sortingLayerID = renderer.sortingLayerID;
                meshRenderer.sortingOrder = renderer.sortingOrder + 1;
            }
        }
    }

    /// <summary>프로젝트에 있는 TMP 폰트를 하나 찾는다. 없으면 TMP 기본값에 맡긴다.</summary>
    private static TMP_FontAsset FindFont()
    {
        string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" });
        if (guids.Length == 0) return null;

        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
