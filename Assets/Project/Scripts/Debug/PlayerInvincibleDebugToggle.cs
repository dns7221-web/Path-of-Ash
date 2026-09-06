#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — `F6`으로 플레이어 무적을 켜고 끄는 조사용 도구.
///
/// 왜 필요한가: 보스 패턴을 손볼 때마다 보스 방까지 가서, 살아남으면서, 12초에 한 번 나오는
/// 패턴을 기다려야 한다. 한 대만 잘못 맞아도 처음부터다. 패턴이 <b>어떻게 보이는지</b>를
/// 확인하는 일과 <b>피할 수 있는지</b>를 확인하는 일은 다른 작업인데, 무적이 없으면 둘을
/// 항상 같이 해야 한다.
///
/// <b>파일 전체를 <c>#if UNITY_EDITOR</c>로 감쌌다.</b> <see cref="BossKeyDebugGrant"/>와 같은 이유다 —
/// 인스펙터 체크박스로 끄는 방식은 켜둔 채로 빌드하는 실수가 언젠가 반드시 나온다.
///
/// 씬에 손대지 않고 실행 시점에 스스로 붙는다. 조사용 오브젝트를 씬에 넣으면 나중에 그걸
/// 빼는 것을 잊고 그대로 커밋된다.
///
/// <see cref="Health.IsInvulnerableExternally"/>가 아니라 <see cref="Health.IsInvulnerableForDebug"/>를
/// 쓰는 이유는 그 속성 주석에 적어뒀다 — 앞의 것은 PlayerController가 매 프레임 덮어쓴다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInvincibleDebugToggle : MonoBehaviour
{
    private const Key ToggleKey = Key.F6;

    private bool active;
    private Health target;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[PlayerInvincibleDebugToggle]");
        go.AddComponent<PlayerInvincibleDebugToggle>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[ToggleKey].wasPressedThisFrame)
        {
            active = !active;
            Apply();

            Debug.Log(active
                ? "[조사용] 플레이어 무적 <b>켜짐</b> (F6으로 끈다). 이 상태의 밸런스는 믿지 마라."
                : "[조사용] 플레이어 무적 꺼짐.");
        }

        // 켜둔 채로 판을 다시 시작하면(씬 재로드) 플레이어가 새로 생겨서 참조가 끊긴다.
        // 그때 다시 붙여주지 않으면 "분명 켰는데 죽는" 상태가 된다. 이 도구는 판을 여러 번
        // 반복하며 쓰는 물건이라 그 상황이 오히려 기본값이다.
        if (active && target == null) Apply();
    }

    /// <summary>지금 씬의 플레이어를 찾아 조사용 무적 상태를 반영한다.</summary>
    private void Apply()
    {
        if (target == null)
        {
            // 연출 중에 잠깐 꺼져 있어도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
            var player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (player != null) target = player.GetComponent<Health>();
        }

        if (target == null)
        {
            Debug.LogWarning("[조사용] 플레이어의 Health를 못 찾았다. 게임 씬에서 눌러라.");
            return;
        }

        target.IsInvulnerableForDebug = active;
    }
}
#endif
