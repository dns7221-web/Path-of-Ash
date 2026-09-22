using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 추가 생성 — 방의 경계 벽을 <b>방이 소유하게</b> 만든다.
///
/// 메뉴: Tools → 재의 길 → 씬·세팅 → 방 벽을 방마다 소유하게
///
/// <b>왜 필요한가.</b> Game 씬 최상위에 <c>Wall</c> 오브젝트가 하나 있었고, 이것이 방과
/// 무관하게 <b>항상 켜져 있었다.</b> 크기는 던전 방 규격이다. 그런데 보스 방은
/// <see cref="AshBossRoomBuilder"/>가 배경 스프라이트에서 계산해 <b>자기 벽을 따로</b>
/// 갖고 있고 그쪽이 더 넓다. 두 벽이 겹쳐 서면 항상 <b>좁은 쪽이 이긴다.</b>
///
/// 그 결과가 이 버그다 — 보스 방에서 걸어서는 출구를 통과할 수 없었다.
///
/// <code>
///   공유 Wall_Top 안쪽 면      y  9.60   ← 플레이어가 여기서 막힌다
///   플레이어 캡슐 위쪽 끝       y  9.60   (offset 0,0.625 / size 2.25,1.25)
///   보스 방 출구 판정 아래쪽 끝  y  9.79   ← 0.19 유닛이 모자라 영원히 안 닿는다
///   보스 방 자체 Wall_Top      y 14.28   ← 원래 여기까지 걸을 수 있어야 했다
/// </code>
///
/// 판정을 옮겨서 증상만 없앨 수도 있지만, 그러면 플레이어가 <b>문에서 4.7유닛 떨어진
/// 빈 바닥에서</b> 나가게 된다. 카메라가 방 전체를 비추고 있어서(orthographic size 15.9)
/// 그 빈 바닥과 문이 화면에 그대로 보인다. 원인은 판정 위치가 아니라 벽의 소유권이다.
///
/// <b>왜 방이 벽을 소유해야 하는가.</b> 방은 이미 통째로 켜고 꺼진다. 벽이 방 안에 있으면
/// 방을 켜는 것만으로 그 방의 경계가 함께 서고, 끄면 함께 사라진다. 켜고 끄는 코드가
/// 아예 필요 없다. 반대로 전역에 하나 두면 "방마다 달라야 하는 것"을 전역이 들고 있는
/// 셈이라, 규격이 다른 방이 하나라도 생기는 순간 조용히 어긋난다. 실제로 그렇게 됐다.
///
/// <b>왜 씬을 손으로 고치지 않고 도구로 하는가.</b> 씬 파일은 텍스트 차이를 사람이 읽을 수
/// 없어서, 손으로 만진 배치는 무엇을 왜 바꿨는지가 기록에 안 남는다. 도구로 두면 의도가
/// 코드에 남고 <see cref="AshBossRoomBuilder"/>를 다시 돌린 뒤에도 같은 상태로 되돌릴 수 있다.
///
/// <b>여러 번 실행해도 안전하다.</b> 이미 자기 벽을 가진 방은 <b>크기를 건드리지 않고</b>
/// 레이어만 맞춘다. 방마다 규격이 다른 것이 정상이고, 이 도구가 그것을 통일해 버리면
/// 보스 방을 다시 좁히는 꼴이 된다.
/// </summary>
public static class AshRoomWallBuilder
{
    /// <summary>씬 최상위에 있던 공유 벽 묶음의 이름이다.</summary>
    private const string SharedWallRootName = "Wall";

    /// <summary>벽이 있어야 할 레이어 이름. 이름으로 찾는 이유는 아래 <see cref="ResolveWallLayer"/> 참고.</summary>
    private const string WallLayerName = "Wall";

    /// <summary>
    /// 방 하나가 갖춰야 할 벽 넷의 이름이다.
    ///
    /// 이름을 기준으로 삼는 이유: <see cref="AshBossRoomBuilder"/>가 이미 같은 이름으로
    /// 벽을 만들고 있다. 두 도구가 같은 이름을 보면 서로의 결과를 알아본다.
    /// </summary>
    private static readonly string[] WallNames = { "Wall_Left", "Wall_Right", "Wall_Bottom", "Wall_Top" };

    /// <summary>
    /// 공유 벽에서 읽어낸 벽 하나의 규격이다. 월드 좌표로 들고 있는 이유는
    /// 이것을 다른 부모(방) 밑에 다시 세워야 하기 때문이다 — 로컬 좌표는 부모가 바뀌면 뜻이 달라진다.
    /// </summary>
    private readonly struct WallTemplate
    {
        public readonly string Name;
        public readonly Vector3 WorldCenter;
        public readonly Vector2 WorldSize;

        public WallTemplate(string name, Vector3 worldCenter, Vector2 worldSize)
        {
            Name = name;
            WorldCenter = worldCenter;
            WorldSize = worldSize;
        }
    }

    [MenuItem("Tools/재의 길/씬·세팅/방 벽을 방마다 소유하게")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();

        // 방 진행 관리자가 없으면 Game 씬이 아니다. 다른 씬에서 실행하면 아무것도 못 찾고
        // "이미 되어 있다"는 잘못된 결론이 나오므로 여기서 끊는다.
        var sequence = Object.FindFirstObjectByType<RoomSequenceController>(FindObjectsInactive.Include);
        if (sequence == null)
        {
            Debug.LogError("[방 벽] 씬에서 RoomSequenceController를 못 찾았다.\n" +
                           "Game 씬을 열고 다시 실행해라.");
            return;
        }

        int wallLayer = ResolveWallLayer();
        if (wallLayer < 0) return;

        // 꺼져 있는 방까지 포함해야 한다. 방은 한 번에 하나만 켜지므로 Include가 없으면
        // 지금 켜진 방 하나만 고쳐지고 나머지는 그대로 남는다.
        List<RoomController> rooms = Object
            .FindObjectsByType<RoomController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OrderBy(room => room.name)
            .ToList();

        if (rooms.Count == 0)
        {
            Debug.LogError("[방 벽] 씬에서 RoomController를 하나도 못 찾았다.");
            return;
        }

        GameObject sharedRoot = FindSharedWallRoot(scene);
        List<WallTemplate> templates = sharedRoot != null ? ReadTemplates(sharedRoot) : new List<WallTemplate>();

        // 공유 벽이 이미 없는 상태에서 다시 실행한 경우다. 모든 방이 자기 벽을 갖고 있으면
        // 할 일이 없는 것이고, 그렇지 않으면 되돌릴 근거(규격)가 사라진 것이라 알려야 한다.
        if (sharedRoot == null)
        {
            List<string> missing = rooms
                .Where(room => MissingWallNames(room).Count > 0)
                .Select(room => room.name)
                .ToList();

            if (missing.Count == 0)
            {
                NormalizeLayers(rooms, wallLayer);
                int realigned = rooms.Count(AlignExitToWall);
                Debug.Log("[방 벽] 이미 모든 방이 자기 벽을 갖고 있다. " +
                          $"레이어를 확인하고 출구 판정 {realigned}개를 다시 맞췄다.");
                EditorSceneManager.MarkSceneDirty(scene);
                return;
            }

            Debug.LogError($"[방 벽] 공유 '{SharedWallRootName}' 오브젝트가 없는데 벽이 없는 방이 남아 있다: " +
                           $"{string.Join(", ", missing)}\n" +
                           "규격을 읽어올 곳이 없다. 되돌린 뒤(Ctrl+Z) 다시 실행하거나 그 방의 벽을 직접 만들어라.");
            return;
        }

        if (templates.Count == 0)
        {
            Debug.LogError($"[방 벽] '{SharedWallRootName}' 아래에서 BoxCollider2D를 하나도 못 찾았다. " +
                           "구조가 바뀌었는지 확인해라.");
            return;
        }

        int created = 0;
        int kept = 0;

        foreach (RoomController room in rooms)
        {
            List<string> missing = MissingWallNames(room);

            // 자기 벽을 이미 다 갖춘 방(보스 방)은 규격이 그 방의 것이다. 크기를 건드리면 안 된다.
            if (missing.Count == 0)
            {
                kept++;
                Debug.Log($"[방 벽] {room.name} — 자기 벽을 이미 갖고 있다. 크기는 그대로 두고 레이어만 맞춘다.", room);
                continue;
            }

            foreach (string wallName in missing)
            {
                WallTemplate template = templates.FirstOrDefault(t => t.Name == wallName);
                if (string.IsNullOrEmpty(template.Name))
                {
                    Debug.LogWarning($"[방 벽] {room.name}에 넣을 '{wallName}' 규격이 공유 벽에 없다. 건너뛴다.", room);
                    continue;
                }

                CreateWall(room, template, wallLayer);
                created++;
            }
        }

        NormalizeLayers(rooms, wallLayer);

        // 방들이 자기 벽을 갖춘 뒤에야 공유 벽을 걷어낸다. 순서를 뒤집으면 규격을 잃는다.
        Undo.DestroyObjectImmediate(sharedRoot);

        // 벽을 옮겼으면 벽에 붙어 있어야 할 것도 같이 옮긴다. 아래 함수의 주석 참고.
        int aligned = rooms.Count(AlignExitToWall);

        EditorSceneManager.MarkSceneDirty(scene);

        // 결과를 숫자로 남긴다. "넓어졌다"는 말보다 걸을 수 있는 영역이 몇 유닛인지가
        // 나중에 밸런스를 볼 때 바로 쓰인다.
        foreach (RoomController room in rooms)
            LogInterior(room);

        Debug.Log($"[방 벽] 끝났다 — 벽 {created}개 생성, 자기 벽을 이미 가진 방 {kept}개는 그대로 뒀다. " +
                  $"출구 판정 {aligned}개를 벽에 맞췄다.\n" +
                  $"공유 '{SharedWallRootName}'은 지웠다. <b>씬은 자동 저장하지 않는다. 확인하고 직접 저장해라.</b>");
    }

    /// <summary>
    /// 벽 레이어 번호를 이름으로 찾는다.
    ///
    /// 숫자 12를 그냥 적지 않는 이유: 레이어 번호는 프로젝트 설정에 있는 값이라 나중에
    /// 순서가 바뀌면 이 도구가 <b>엉뚱한 레이어에 벽을 놓고도 아무 말을 안 한다.</b>
    /// 이름으로 찾으면 없어졌을 때 여기서 멈춘다.
    /// </summary>
    private static int ResolveWallLayer()
    {
        int layer = LayerMask.NameToLayer(WallLayerName);
        if (layer < 0)
        {
            Debug.LogError($"[방 벽] '{WallLayerName}' 레이어가 프로젝트에 없다.\n" +
                           "Tools → 재의 길 → 프로젝트 기본 설정 을 먼저 실행해라.");
        }

        return layer;
    }

    /// <summary>
    /// 씬 최상위에서 공유 벽 묶음을 찾는다.
    ///
    /// 방 안에 있는 같은 이름을 잘못 집지 않도록 <b>최상위만</b> 본다.
    /// GameObject.Find를 쓰지 않는 이유: 꺼진 오브젝트를 못 찾는다.
    /// </summary>
    private static GameObject FindSharedWallRoot(Scene scene)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == SharedWallRootName);
    }

    /// <summary>공유 벽 아래의 BoxCollider2D를 월드 기준 규격으로 읽는다.</summary>
    private static List<WallTemplate> ReadTemplates(GameObject sharedRoot)
    {
        var templates = new List<WallTemplate>();

        foreach (BoxCollider2D box in sharedRoot.GetComponentsInChildren<BoxCollider2D>(true))
        {
            Transform t = box.transform;

            // 콜라이더의 offset과 부모의 스케일까지 녹여서 월드 좌표로 만든다.
            // 지금 이 씬은 스케일이 전부 1이지만, 여기서 접어두지 않으면 나중에 스케일이
            // 붙은 방에서 벽이 조용히 어긋난다.
            Vector3 worldCenter = t.TransformPoint(box.offset);
            Vector2 worldSize = Vector2.Scale(box.size, t.lossyScale);

            templates.Add(new WallTemplate(box.name, worldCenter, worldSize));
        }

        return templates;
    }

    /// <summary>이 방에 아직 없는 벽의 이름을 고른다.</summary>
    private static List<string> MissingWallNames(RoomController room)
    {
        return WallNames.Where(name => FindChild(room.transform, name) == null).ToList();
    }

    /// <summary>공유 벽의 규격 그대로 방 안에 벽 하나를 세운다.</summary>
    private static void CreateWall(RoomController room, WallTemplate template, int wallLayer)
    {
        var created = new GameObject(template.Name);
        Undo.RegisterCreatedObjectUndo(created, "방 벽 이전");

        created.transform.SetParent(room.transform, false);
        created.layer = wallLayer;

        // z는 방의 것을 따른다. 2D라 충돌에는 영향이 없지만 씬 뷰에서 벽만 떠 있으면 헷갈린다.
        created.transform.position = new Vector3(
            template.WorldCenter.x, template.WorldCenter.y, room.transform.position.z);

        var box = Undo.AddComponent<BoxCollider2D>(created);
        box.offset = Vector2.zero;

        // 월드 크기를 방의 스케일로 나눠 되돌린다. 스케일이 1이면 그대로다.
        Vector3 scale = created.transform.lossyScale;
        box.size = new Vector2(
            Mathf.Approximately(scale.x, 0f) ? template.WorldSize.x : template.WorldSize.x / scale.x,
            Mathf.Approximately(scale.y, 0f) ? template.WorldSize.y : template.WorldSize.y / scale.y);
    }

    /// <summary>
    /// 모든 방의 벽을 Wall 레이어로 맞춘다.
    ///
    /// 보스 방 벽이 레이어 0(Default)이었다. <see cref="AshBossRoomBuilder"/>가 벽을 만들 때
    /// 방의 레이어를 그대로 물려줬는데 방 자체가 Default라 그렇게 됐다.
    /// 충돌 매트릭스에서 Player가 Default와도 부딪히게 열려 있어 <b>우연히</b> 동작했을 뿐이라,
    /// 매트릭스를 조금만 손대면 보스 방 벽이 통째로 사라진다.
    /// </summary>
    private static void NormalizeLayers(List<RoomController> rooms, int wallLayer)
    {
        foreach (RoomController room in rooms)
        {
            foreach (string wallName in WallNames)
            {
                Transform wall = FindChild(room.transform, wallName);
                if (wall == null || wall.gameObject.layer == wallLayer) continue;

                Undo.RecordObject(wall.gameObject, "방 벽 레이어");
                Debug.Log($"[방 벽] {room.name}/{wallName} 레이어 " +
                          $"{LayerMask.LayerToName(wall.gameObject.layer)} → {WallLayerName}", wall);
                wall.gameObject.layer = wallLayer;
            }
        }
    }

    /// <summary>
    /// 추가 생성 — 출구 판정을 <b>제일 가까운 벽의 안쪽 면에 붙인다.</b> 옮겼으면 true.
    ///
    /// <b>왜 벽과 같이 옮겨야 하는가.</b> 출구 판정은 문 앞에 있어야 의미가 있는데, 문은
    /// 벽에 그려져 있다. 벽을 옮기고 판정을 그대로 두면 두 가지 중 하나가 된다 —
    /// 판정이 벽 너머로 남으면 <b>영원히 안 닿고</b>(이번 버그다), 벽 안쪽에 남으면
    /// <b>문에 닿기도 전에 나가진다.</b> 보스 방이 정확히 두 번째가 될 참이었다:
    /// 공유 벽을 걷어내면 벽이 y 9.60에서 14.28로 물러나는데 판정은 9.79~11.99라,
    /// 플레이어가 문에서 4.5유닛 못 미친 빈 바닥에서 화면이 넘어간다.
    ///
    /// <b>규칙 한 줄.</b> 판정의 바깥쪽 모서리를 벽 안쪽 면에 맞춘다. 그러면 벽에 딱 붙어
    /// 선 플레이어는 반드시 판정 안에 있고, 걸어오는 플레이어는 판정 높이(2.2유닛)만큼
    /// 앞에서 걸린다 — 화면으로 문 앞이다.
    ///
    /// <b>어느 벽인지 어떻게 아는가.</b> 방마다 문의 방향이 다르다(던전·보스는 위, 튜토리얼은
    /// 아래). 인스펙터에 방향을 적어두면 채우는 것을 잊었을 때 조용히 틀리므로, 지금
    /// 판정이 <b>제일 가까이 있는 벽</b>을 그 방향으로 본다. 사람이 대충 옳은 자리에 둔 것을
    /// 정확한 자리로 당기는 것이지, 없는 정보를 지어내는 것이 아니다.
    /// </summary>
    private static bool AlignExitToWall(RoomController room)
    {
        Transform exit = FindChild(room.transform, "DoorExitTrigger");
        if (exit == null) return false;

        var box = exit.GetComponent<BoxCollider2D>();
        if (box == null) return false;

        Transform left = FindChild(room.transform, "Wall_Left");
        Transform right = FindChild(room.transform, "Wall_Right");
        Transform bottom = FindChild(room.transform, "Wall_Bottom");
        Transform top = FindChild(room.transform, "Wall_Top");
        if (left == null || right == null || bottom == null || top == null) return false;

        Vector3 center = exit.TransformPoint(box.offset);
        Vector2 size = Vector2.Scale(box.size, exit.lossyScale);

        float innerLeft = InnerFace(left, +1);
        float innerRight = InnerFace(right, -1);
        float innerBottom = InnerFace(bottom, +1, vertical: true);
        float innerTop = InnerFace(top, -1, vertical: true);

        // 판정 한가운데에서 네 벽까지의 거리 중 제일 짧은 쪽이 이 방의 출구 방향이다.
        float toTop = Mathf.Abs(innerTop - center.y);
        float toBottom = Mathf.Abs(center.y - innerBottom);
        float toLeft = Mathf.Abs(center.x - innerLeft);
        float toRight = Mathf.Abs(innerRight - center.x);
        float nearest = Mathf.Min(Mathf.Min(toTop, toBottom), Mathf.Min(toLeft, toRight));

        Vector3 target = center;
        string wallName;

        if (Mathf.Approximately(nearest, toTop)) { target.y = innerTop - size.y * 0.5f; wallName = "Wall_Top"; }
        else if (Mathf.Approximately(nearest, toBottom)) { target.y = innerBottom + size.y * 0.5f; wallName = "Wall_Bottom"; }
        else if (Mathf.Approximately(nearest, toLeft)) { target.x = innerLeft + size.x * 0.5f; wallName = "Wall_Left"; }
        else { target.x = innerRight - size.x * 0.5f; wallName = "Wall_Right"; }

        // 이미 맞아 있으면 씬을 건드리지 않는다. 다시 실행해도 변경 이력이 안 쌓인다.
        if ((target - center).sqrMagnitude < 0.0001f) return false;

        Undo.RecordObject(exit, "출구 판정 정렬");
        exit.position += target - center;

        Debug.Log($"[방 벽] {room.name}/DoorExitTrigger — {wallName} 안쪽 면에 맞췄다. " +
                  $"({center.x:0.00}, {center.y:0.00}) → ({target.x:0.00}, {target.y:0.00})", exit);
        return true;
    }

    /// <summary>
    /// 벽 안쪽 면으로 둘러싸인 영역, 곧 <b>실제로 걸을 수 있는 바닥</b>을 로그로 남긴다.
    ///
    /// 벽의 바깥 면이 아니라 안쪽 면을 쓰는 이유: 출구 판정이 닿는지는 플레이어가 갈 수 있는
    /// 끝이 어디냐로 정해진다. 이번 버그가 정확히 그 0.19유닛에서 났다.
    /// </summary>
    private static void LogInterior(RoomController room)
    {
        Transform left = FindChild(room.transform, "Wall_Left");
        Transform right = FindChild(room.transform, "Wall_Right");
        Transform bottom = FindChild(room.transform, "Wall_Bottom");
        Transform top = FindChild(room.transform, "Wall_Top");
        if (left == null || right == null || bottom == null || top == null) return;

        float innerLeft = InnerFace(left, +1);
        float innerRight = InnerFace(right, -1);
        float innerBottom = InnerFace(bottom, +1, vertical: true);
        float innerTop = InnerFace(top, -1, vertical: true);

        Debug.Log($"[방 벽] {room.name} 걸을 수 있는 바닥 — " +
                  $"x {innerLeft:0.00}~{innerRight:0.00}, y {innerBottom:0.00}~{innerTop:0.00} " +
                  $"({innerRight - innerLeft:0.0} x {innerTop - innerBottom:0.0} 유닛)", room);
    }

    /// <summary>벽 콜라이더에서 방 안쪽을 향한 면의 좌표를 낸다.</summary>
    private static float InnerFace(Transform wall, int sign, bool vertical = false)
    {
        var box = wall.GetComponent<BoxCollider2D>();
        if (box == null) return vertical ? wall.position.y : wall.position.x;

        Vector3 center = wall.TransformPoint(box.offset);
        Vector2 size = Vector2.Scale(box.size, wall.lossyScale);

        return vertical
            ? center.y + sign * size.y * 0.5f
            : center.x + sign * size.x * 0.5f;
    }

    /// <summary>이름이 같은 직속 자식을 찾는다. 꺼져 있어도 찾는다.</summary>
    private static Transform FindChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName) return child;
        }

        return null;
    }
}
