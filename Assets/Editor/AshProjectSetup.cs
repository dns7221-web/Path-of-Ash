using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 프로젝트 초기 세팅(레이어 / 2D 충돌 매트릭스 / 씬 3개 / 빌드 설정 / 카메라 규격)을
/// 한 번에 적용하는 에디터 도구.
///
/// Project Settings 창에서 손으로 클릭하지 않고 스크립트로 둔 이유:
/// 1) 무엇을 왜 그렇게 설정했는지가 코드로 남는다. 반년 뒤에 "이 레이어 왜 있지"를 안 묻게 된다.
/// 2) 여러 번 실행해도 결과가 같다(멱등). 설정이 꼬였을 때 되돌리는 수단이 된다.
/// 3) 충돌 매트릭스는 조합이 수십 개라 손으로 찍으면 반드시 하나를 빠뜨린다. 그 하나가
///    "가끔 플레이어가 자기 공격에 맞는" 식의 재현하기 어려운 버그가 된다.
///
/// 메뉴: Tools → 재의 길 → 프로젝트 세팅 적용
/// </summary>
public static class AshProjectSetup
{
    // ── 규격 상수 ──────────────────────────────────────────────────────────

    /// <summary>
    /// PPU(Pixels Per Unit) 32 = 32픽셀 타일 하나가 월드 1유닛.
    ///
    /// 이 값을 지금 못 박는 이유: 나중에 스프라이트를 얹는 시점에 PPU가 바뀌면 콜라이더 크기,
    /// 이동 속도, 카메라 줌, 넉백 거리를 전부 다시 잡아야 한다. 도형으로 프로토타입을 만드는
    /// 지금부터 같은 자로 재야 3주차에 아트를 얹을 때 숫자를 안 건드린다.
    ///
    /// 수정(아트 확정 시점): 16 → 32로 올렸다.
    ///
    /// 캐릭터 디자인이 후드 그림자, 붉은 눈, 검날의 잉걸 균열까지 읽혀야 하는데 16 PPU 기준
    /// 캐릭터(2유닛 = 32픽셀)로는 눈이 1픽셀이 되어 전부 뭉개진다. 이 디자인이 읽히는 최소
    /// 크기가 64픽셀이라 PPU를 두 배로 올렸다.
    ///
    /// <b>월드 유닛 규격은 하나도 바뀌지 않는다</b>는 게 이 변경의 핵심이다.
    /// <see cref="CameraOrthographicSize"/> 5.625 그대로, 방 13x9유닛 그대로, 충돌 매트릭스
    /// 그대로다. 바뀌는 건 "1유닛을 몇 픽셀로 그리느냐" 하나뿐이라, 위 주석이 경고한
    /// 콜라이더·이동속도·넉백 재조정이 발생하지 않는다. 아트가 씬에 확정 배치되기 전인
    /// 지금이 이 값을 바꿀 수 있는 마지막 시점이라 여기서 정리했다.
    ///
    /// 주의 — 이 상수는 <b>선언일 뿐 스스로 강제되지 않는다.</b> 실제 텍스처에 이 값을 먹이는
    /// 건 AshSpriteImportRules(AssetPostprocessor)다. 그게 없던 동안 임포트된 텍스처들이
    /// 60 / 80 / 100으로 제각각이 됐던 게 이 상수를 믿으면 안 되는 이유다.
    /// </summary>
    public const int PixelsPerUnit = 32;

    /// <summary>
    /// 기준 해상도 640x360 기준의 직교 카메라 크기.
    /// 세로 360픽셀 / PPU 32 = 11.25유닛이고, orthographicSize는 그 절반이라 5.625다.
    /// 640x360은 1280x720(2배)과 1920x1080(3배)에 정수배로 떨어져서, 나중에 픽셀 퍼펙트를
    /// 켜도 픽셀이 뭉개지지 않는다.
    ///
    /// 화면에 담기는 범위는 가로 20 x 세로 11.25유닛. 방 하나를 가로 13 x 세로 9유닛으로
    /// 잡을 예정이므로 "방 하나 = 화면 하나"가 여백을 두고 성립한다.
    ///
    /// 수정(아트 확정 시점): PPU가 16 → 32로 오르면서 기준 해상도가 320x180 → 640x360이
    /// 됐다. <b>이 상수의 값 자체는 5.625 그대로다.</b> 세로 유닛 수(11.25)가 안 바뀌었기
    /// 때문이고, 그래서 씬의 카메라도 손댈 필요가 없다. 정수배 조건은 오히려 더 좋아졌다
    /// (720p 2배 / 1080p 3배).
    /// </summary>
    public const float CameraOrthographicSize = 5.625f;

    // ── 레이어 ────────────────────────────────────────────────────────────

    // 0~7번은 유니티 예약 슬롯이라 8번부터 채운다.
    private static readonly string[] UserLayers =
    {
        "Player",       // 플레이어 본체
        "Enemy",        // 적 본체
        "PlayerAttack", // 플레이어가 만든 히트박스 / 투사체
        "EnemyAttack",  // 적이 만든 히트박스 / 투사체
        "Wall",         // 벽, 문, 지형 장애물
        "Pickup",       // 아이템, 회복, 재화
    };

    // 충돌을 "켤" 조합만 적는다. 우리 레이어끼리의 나머지 조합은 전부 끈다.
    //
    // 화이트리스트로 쓴 이유: 나중에 레이어를 추가했을 때 기본값이 "꺼짐"이어야 안전하다.
    // 반대로(블랙리스트로) 쓰면 새 레이어가 조용히 모든 것과 충돌하면서 원인 모를 버그를 만든다.
    private static readonly string[,] EnabledPairs =
    {
        { "Player", "Enemy" },        // 서로 밀어낸다. 적이 플레이어를 관통하지 않게
        { "Player", "Wall" },
        { "Player", "Pickup" },       // 아이템 획득 트리거
        { "Player", "EnemyAttack" },  // 플레이어가 맞는 경로
        { "Enemy", "Enemy" },         // 적끼리 겹쳐서 한 덩어리로 보이는 것 방지
        { "Enemy", "Wall" },
        { "Enemy", "PlayerAttack" },  // 적이 맞는 경로
        { "PlayerAttack", "Wall" },   // 투사체가 벽에 막히게
        { "EnemyAttack", "Wall" },
        { "Wall", "Pickup" },         // 드랍된 아이템이 벽을 뚫고 나가지 않게
    };

    // 위 목록에 없어서 자동으로 꺼지는 것 중 특히 중요한 조합:
    //   Player       x PlayerAttack — 자기 공격에 자기가 맞는 문제
    //   Enemy        x EnemyAttack  — 적이 자기 공격에 맞는 문제
    //   PlayerAttack x EnemyAttack  — 공격끼리 서로 상쇄되는 의도치 않은 동작
    //   Pickup       x Pickup       — 아이템끼리 밀치며 굴러다니는 문제

    // ── 정렬 레이어 ────────────────────────────────────────────────────────

    // 추가 생성: 2D는 z좌표가 아니라 Sorting Layer로 앞뒤가 정해진다.
    //
    // 지금 만들어두는 이유: 나중에 만들면 이미 찍어놓은 프리팹의 SpriteRenderer를 전부 다시
    // 태깅해야 한다. 레이어 이름은 나중에 바꾸기 어렵지만 개수를 늘리는 건 쉬우므로,
    // 지금은 "바닥 / 그 위에 서는 것 / 그 위에 뜨는 것"만 나눠두고 필요할 때 사이에 끼운다.
    //
    // 목록 순서가 곧 그리는 순서다. 뒤에 있을수록 위에 그려진다.
    private static readonly string[] SortingLayerNames =
    {
        "Background", // 방 밖 배경, 원경
        "Floor",      // 바닥 타일
        "Decal",      // 바닥에 눌어붙는 것 — 핏자국, 그을음, 장판
        "Entity",     // 플레이어 / 적 / 아이템. Y좌표로 자기들끼리 다시 정렬한다
        "VFX",        // 슬래시, 폭발 — 캐릭터 위에 떠야 한다
        "UI",         // 월드 공간 UI, 데미지 숫자
    };

    // ── 씬 ────────────────────────────────────────────────────────────────

    private const string SceneFolder = "Assets/Scenes";

    // 빌드 설정에도 이 순서 그대로 등록된다. Title이 0번이라 빌드하면 여기서 시작한다.
    private static readonly string[] ScenePaths =
    {
        SceneFolder + "/Title.unity",
        SceneFolder + "/Game.unity",
        SceneFolder + "/Result.unity",
    };

    [MenuItem("Tools/재의 길/프로젝트 세팅 적용")]
    public static void ApplyAll()
    {
        // 씬을 새로 만들면 지금 열린 씬이 닫힌다. 저장 안 된 작업이 있으면 먼저 물어본다.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("[재의 길] 세팅을 취소했다.");
            return;
        }

        int addedLayers = ApplyLayers();

        // 레이어가 등록된 다음에야 이름으로 번호를 찾을 수 있으므로 순서가 중요하다.
        ApplyCollisionMatrix();

        // 추가 생성: 탑다운이라 중력이 0이어야 하고, 2D 앞뒤 정렬용 레이어가 필요하다.
        int addedSortingLayers = ApplySortingLayers();
        ApplyPhysics2DSettings();

        int createdScenes = CreateScenes();
        ApplyBuildSettings();
        ApplyPlayerSettings();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[재의 길] 프로젝트 세팅 완료 — 레이어 {addedLayers}개 추가, " +
                  $"정렬 레이어 {addedSortingLayers}개 추가, 씬 {createdScenes}개 생성, " +
                  $"충돌 매트릭스 적용, 2D 중력 0, PPU {PixelsPerUnit} / 카메라 size {CameraOrthographicSize}");
    }

    /// <summary>
    /// TagManager.asset의 레이어 목록에 우리 레이어를 채운다.
    /// 이미 같은 이름이 있으면 건너뛰므로 여러 번 실행해도 중복되지 않는다.
    /// </summary>
    /// <returns>새로 추가한 레이어 개수</returns>
    private static int ApplyLayers()
    {
        // ProjectSettings 폴더의 에셋은 Assets 밖에 있지만 이 경로로 직접 열 수 있다.
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("[재의 길] TagManager.asset을 못 찾았다. 레이어 설정을 건너뛴다.");
            return 0;
        }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layersProp = tagManager.FindProperty("layers");
        int added = 0;

        foreach (string layerName in UserLayers)
        {
            // 이미 등록된 이름이면 아무것도 하지 않는다.
            if (LayerMask.NameToLayer(layerName) != -1) continue;

            // 8번(첫 사용자 슬롯)부터 비어 있는 칸을 찾아 넣는다.
            // 슬롯 번호를 고정하지 않은 이유는, 이미 뭔가 들어 있는 칸을 덮어쓰지 않기 위해서다.
            bool placed = false;
            for (int i = 8; i < layersProp.arraySize; i++)
            {
                SerializedProperty slot = layersProp.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                placed = true;
                added++;
                break;
            }

            if (!placed)
                Debug.LogError($"[재의 길] 빈 레이어 슬롯이 없어 '{layerName}'을 넣지 못했다.");
        }

        tagManager.ApplyModifiedProperties();
        return added;
    }

    /// <summary>
    /// 2D 레이어 충돌 매트릭스를 EnabledPairs 화이트리스트대로 적용한다.
    ///
    /// Physics2DSettings.asset을 직접 편집하지 않고 내장 API(Physics2D.IgnoreLayerCollision)를
    /// 쓴 이유: 저장 형식이 32칸짜리 비트마스크 배열이라 손으로 계산하면 틀리기 쉽고,
    /// 유니티 버전이 올라가며 형식이 바뀌면 그대로 깨진다.
    /// </summary>
    private static void ApplyCollisionMatrix()
    {
        // 이름 → 레이어 번호로 미리 바꿔둔다. 하나라도 못 찾으면 반쯤 적용된 상태를 만들지 않고 중단한다.
        List<int> layerIds = new List<int>();
        foreach (string layerName in UserLayers)
        {
            int id = LayerMask.NameToLayer(layerName);
            if (id == -1)
            {
                Debug.LogError($"[재의 길] 레이어 '{layerName}'이 없어 충돌 매트릭스를 건너뛴다.");
                return;
            }
            layerIds.Add(id);
        }

        // 켜야 할 조합을 빠른 조회용 집합으로 만든다. (a,b)와 (b,a)는 같은 것으로 취급한다.
        HashSet<int> enabled = new HashSet<int>();
        for (int i = 0; i < EnabledPairs.GetLength(0); i++)
        {
            int a = LayerMask.NameToLayer(EnabledPairs[i, 0]);
            int b = LayerMask.NameToLayer(EnabledPairs[i, 1]);
            if (a == -1 || b == -1) continue;
            enabled.Add(PairKey(a, b));
        }

        // 우리 레이어끼리의 모든 조합을 순회하며 켜기/끄기를 확정한다.
        // "목록에 적지 않은 조합은 끈다"가 이 함수의 규칙이다.
        foreach (int a in layerIds)
        {
            foreach (int b in layerIds)
            {
                if (b < a) continue; // 같은 조합을 두 번 처리하지 않는다

                bool shouldCollide = enabled.Contains(PairKey(a, b));
                Physics2D.IgnoreLayerCollision(a, b, !shouldCollide);
            }
        }
    }

    /// <summary>레이어 두 개를 순서에 상관없이 같은 키로 만든다. 레이어는 0~31이므로 32진수처럼 쓴다.</summary>
    private static int PairKey(int a, int b)
    {
        int low = Mathf.Min(a, b);
        int high = Mathf.Max(a, b);
        return low * 32 + high;
    }

    /// <summary>
    /// 추가 생성: Sorting Layer를 등록한다. 이미 같은 이름이 있으면 건너뛴다(멱등).
    ///
    /// TagManager.asset의 m_SortingLayers 배열을 직접 다룬다. 항목 하나는
    /// { name, uniqueID, locked } 세 값으로 이뤄지고, 배열에 놓인 순서가 곧 그리는 순서다.
    ///
    /// uniqueID를 직접 발급하는 이유: 이 값이 프리팹의 SpriteRenderer에 저장되는 실제 참조라
    /// 서로 겹치면 두 레이어가 같은 것으로 취급되어 정렬이 뒤섞인다. 유니티가 대신 발급해주는
    /// InternalEditorUtility.AddSortingLayer()가 있었지만 Unity 6에서 제거됐다.
    /// </summary>
    /// <returns>새로 추가한 정렬 레이어 개수</returns>
    private static int ApplySortingLayers()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("[재의 길] TagManager.asset을 못 찾았다. 정렬 레이어 설정을 건너뛴다.");
            return 0;
        }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty sortingLayers = tagManager.FindProperty("m_SortingLayers");
        if (sortingLayers == null)
        {
            Debug.LogError("[재의 길] TagManager에서 m_SortingLayers를 못 찾았다.");
            return 0;
        }

        // 1단계 — 목록에 없는 이름을 배열 끝에 추가한다. 뒤에 놓일수록 위에 그려진다.
        // ID는 여기서 정하지 않고 2단계에서 일괄로 잡는다.
        HashSet<string> existingNames = new HashSet<string>();
        for (int i = 0; i < sortingLayers.arraySize; i++)
            existingNames.Add(sortingLayers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);

        int added = 0;
        foreach (string layerName in SortingLayerNames)
        {
            if (existingNames.Contains(layerName)) continue;

            int index = sortingLayers.arraySize;
            sortingLayers.InsertArrayElementAtIndex(index);
            SerializedProperty entry = sortingLayers.GetArrayElementAtIndex(index);

            entry.FindPropertyRelative("name").stringValue = layerName;
            entry.FindPropertyRelative("uniqueID").intValue = 0; // 2단계에서 채운다
            entry.FindPropertyRelative("locked").boolValue = false;

            existingNames.Add(layerName);
            added++;
        }

        // 2단계 — uniqueID를 검사해서 비어 있거나(0) 다른 항목과 겹치는 것을 다시 발급한다.
        //
        // 추가한 직후가 아니라 따로 도는 이유: 예전 버전이 잘못 넣어둔 ID도 여기서 같이
        // 복구된다. 실제로 부호 비트를 안 지운 해시가 0으로 잘려 Default와 겹친 적이 있다.
        HashSet<int> usedIds = new HashSet<int>();
        int repaired = 0;

        for (int i = 0; i < sortingLayers.arraySize; i++)
        {
            SerializedProperty entry = sortingLayers.GetArrayElementAtIndex(i);
            string layerName = entry.FindPropertyRelative("name").stringValue;
            SerializedProperty idProp = entry.FindPropertyRelative("uniqueID");
            int currentId = idProp.intValue;

            // Default는 uniqueID 0이 정상이고 유니티가 쥐고 있는 값이라 건드리지 않는다.
            if (layerName == "Default")
            {
                usedIds.Add(currentId);
                continue;
            }

            // 값이 있고 아직 아무도 안 쓰는 값이면 그대로 둔다.
            // 이미 프리팹이 이 ID를 참조하고 있을 수 있어서, 멀쩡한 값은 절대 바꾸지 않는다.
            if (currentId != 0 && !usedIds.Contains(currentId))
            {
                usedIds.Add(currentId);
                continue;
            }

            int newId = MakeSortingLayerId(layerName, usedIds);
            idProp.intValue = newId;
            usedIds.Add(newId);
            repaired++;
        }

        if (added > 0 || repaired > 0) tagManager.ApplyModifiedProperties();

        if (repaired > 0)
            Debug.Log($"[재의 길] 정렬 레이어 ID {repaired}개를 다시 발급했다(비었거나 중복이었음).");

        return added;
    }

    /// <summary>
    /// 정렬 레이어 이름에서 uniqueID를 만든다.
    ///
    /// 난수가 아니라 이름에서 결정론적으로 뽑는 이유: 프로젝트를 다시 세팅하거나 다른 컴퓨터에서
    /// 이 도구를 돌려도 같은 이름이면 같은 ID가 나와야, 프리팹에 저장된 정렬 레이어 참조가
    /// 깨지지 않는다. 난수로 발급하면 재실행할 때마다 모든 스프라이트의 정렬이 초기화된다.
    /// </summary>
    private static int MakeSortingLayerId(string layerName, HashSet<int> usedIds)
    {
        // 문자열 해시를 직접 계산한다. string.GetHashCode()는 유니티/런타임 버전에 따라
        // 값이 달라질 수 있어서 프로젝트 파일에 저장할 값으로는 쓰지 않는다.
        int id = 17;
        foreach (char c in layerName)
            id = unchecked(id * 31 + c);

        // 부호 비트를 지워 항상 양수로 만든다.
        //
        // 이게 없으면 곱셈이 int 범위를 넘어가 음수가 되는 이름이 나오는데, uniqueID는 부호
        // 없는 값으로 저장돼서 음수를 넣으면 0으로 잘린다. 0은 Default의 ID라, 그 레이어가
        // 통째로 Default와 같은 것으로 취급되어 앞뒤 정렬이 뒤섞인다.
        id &= 0x7FFFFFFF;

        if (id == 0) id = 1; // 0은 Default가 이미 쓴다

        // 만에 하나 겹치면 빈 값을 찾을 때까지 밀어낸다. 끝까지 가면 1로 되감는다.
        while (usedIds.Contains(id))
            id = id == int.MaxValue ? 1 : id + 1;

        return id;
    }

    /// <summary>
    /// 추가 생성: 2D 물리 전역 설정. 핵심은 중력을 0으로 만드는 것이다.
    ///
    /// 탑다운은 위에서 내려다보는 시점이라 화면의 아래쪽이 "아래"가 아니다. 기본값
    /// (0, -9.81)을 그대로 두면 Rigidbody2D를 붙이는 순간 플레이어와 적이 화면 아래로
    /// 흘러내린다. 각 Rigidbody2D마다 gravityScale을 0으로 만드는 방법도 있지만, 프리팹이
    /// 늘어날수록 하나씩 빠뜨리게 되므로 전역에서 한 번에 끄는 쪽이 안전하다.
    ///
    /// Physics2D.gravity 프로퍼티 대신 설정 에셋을 직접 쓰는 이유: 프로퍼티 쪽은 플레이 모드가
    /// 끝나면 되돌아가는 경우가 있어 프로젝트 설정으로 남지 않는다.
    /// </summary>
    private static void ApplyPhysics2DSettings()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError("[재의 길] Physics2DSettings.asset을 못 찾았다. 중력 설정을 건너뛴다.");
            return;
        }

        SerializedObject physics2D = new SerializedObject(assets[0]);
        SerializedProperty gravity = physics2D.FindProperty("m_Gravity");

        if (gravity == null)
        {
            Debug.LogError("[재의 길] Physics2DSettings에서 m_Gravity를 못 찾았다. 중력을 직접 0으로 바꿔라.");
            return;
        }

        gravity.vector2Value = Vector2.zero;
        physics2D.ApplyModifiedProperties();
    }

    /// <summary>
    /// Title / Game / Result 씬을 만든다. 이미 있으면 절대 건드리지 않는다.
    /// 각 씬에는 아래 CreateCamera가 만드는 규격 카메라 하나만 들어간다.
    /// </summary>
    /// <returns>새로 만든 씬 개수</returns>
    private static int CreateScenes()
    {
        if (!AssetDatabase.IsValidFolder(SceneFolder))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        int created = 0;
        foreach (string path in ScenePaths)
        {
            // 이미 만든 씬을 덮어쓰면 작업물이 통째로 날아간다. 존재하면 무조건 건너뛴다.
            if (System.IO.File.Exists(path)) continue;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera();
            EditorSceneManager.SaveScene(scene, path);
            created++;
        }

        return created;
    }

    /// <summary>
    /// 2D용 직교 카메라를 만든다.
    /// 세 씬 모두 같은 규격이어야 씬을 전환할 때 화면에 담기는 범위가 튀지 않는다.
    /// </summary>
    private static void CreateCamera()
    {
        GameObject go = new GameObject("Main Camera");
        go.tag = "MainCamera";

        Camera cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = CameraOrthographicSize;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.06f, 0.08f); // 재/그을음 톤의 어두운 배경

        // 2D에서는 깊이가 정렬용으로만 쓰이므로 클리핑 범위를 넉넉히 열어둔다.
        cam.nearClipPlane = -100f;
        cam.farClipPlane = 100f;

        // 카메라가 z축 뒤에 있어야 원점(z=0)에 놓인 스프라이트가 보인다.
        go.transform.position = new Vector3(0f, 0f, -10f);

        // 빈 씬으로 만들었으므로 오디오 리스너가 없다. 없으면 사운드가 아예 안 들린다.
        go.AddComponent<AudioListener>();
    }

    /// <summary>빌드 설정에 씬 세 개를 Title → Game → Result 순으로 등록한다.</summary>
    private static void ApplyBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
        foreach (string path in ScenePaths)
        {
            if (!System.IO.File.Exists(path)) continue;
            scenes.Add(new EditorBuildSettingsScene(path, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>제품명과 기본 해상도를 맞춘다. 제품명은 세이브 파일 경로에도 쓰인다.</summary>
    private static void ApplyPlayerSettings()
    {
        PlayerSettings.productName = "재의 길";
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;

        // 창을 옮기거나 포커스를 잃어도 물리가 멈추지 않게 한다. 디버깅할 때 편하다.
        PlayerSettings.runInBackground = true;
    }
}
