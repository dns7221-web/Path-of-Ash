#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — `F3`으로 보스 열쇠를 전부 지급하는 조사용 도구.
///
/// 왜 필요한가: 보스로 가려면 열쇠 넷을 모아야 하는데, 상자마다 25%라 평균 열여섯 방을
/// 돌아야 한다. 보스 방을 손보는 동안 매번 그걸 반복할 수는 없다.
///
/// <b>파일 전체를 <c>#if UNITY_EDITOR</c>로 감쌌다.</b> 빌드에는 이 클래스 자체가 없다.
/// 조사용 키를 인스펙터 체크박스로 끄는 방식으로 두면 <b>켜둔 채로 빌드하는 실수</b>가
/// 언젠가 반드시 나온다. 아예 컴파일되지 않게 하면 그 실수가 불가능해진다.
///
/// 씬에 손대지 않고 실행 시점에 스스로 붙는다. 조사용 오브젝트를 씬에 넣으면 나중에
/// 그걸 빼는 것을 잊고, 그대로 커밋된다.
///
/// 열쇠 목록을 인스펙터로 받지 않고 에셋에서 찾는 이유: 손으로 꽂아두면 유물이 늘어날 때마다
/// 다시 꽂아야 한다. 역할이 BossKey인 것을 모으면 <b>유물을 추가해도 저절로 따라온다.</b>
/// </summary>
[DisallowMultipleComponent]
public class BossKeyDebugGrant : MonoBehaviour
{
    private const Key GrantKey = Key.F3;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[BossKeyDebugGrant]");
        go.AddComponent<BossKeyDebugGrant>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (!keyboard[GrantKey].wasPressedThisFrame) return;

        Grant();
    }

    private static void Grant()
    {
        // 꺼져 있는 순간에도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
        var inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        if (inventory == null)
        {
            Debug.LogWarning("[디버그] RelicInventory를 못 찾았다. 게임 씬에서 눌러라.");
            return;
        }

        int given = 0;
        int skipped = 0;

        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:RelicData"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var relic = UnityEditor.AssetDatabase.LoadAssetAtPath<RelicData>(path);

            if (relic == null || relic.Role != RelicData.RelicRole.BossKey) continue;

            // 이미 가진 것은 건너뛴다. 안 그러면 같은 열쇠가 칸을 두 번 차지한다.
            if (inventory.Has(relic)) { skipped++; continue; }

            // Acquire를 그대로 쓰는 이유: 지급 경로를 따로 만들면 실제 획득과 다르게 동작해서
            // "디버그로는 되는데 상자로는 안 된다"는 상황이 생긴다. 같은 문으로 들어가야 한다.
            inventory.Acquire(relic);
            given++;
        }

        Debug.Log($"[디버그] 보스 열쇠 지급 — 새로 {given}개, 이미 있던 것 {skipped}개. " +
                  $"현재 {inventory.BossKeyCount}/{inventory.BossKeysRequired}\n" +
                  "문은 <b>다음 보상을 챙길 때</b> 부서진 문으로 바뀐다. 방 하나를 더 클리어해라.");
    }
}
#endif
