#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — `F11`로 재 게이지를 가득 채우는 조사용 도구.
///
/// 왜 필요한가: 궁극기(R)는 <see cref="SkillData.RequiresFullAshGauge"/>가 켜져 있어서
/// 게이지가 가득 찼을 때만 나간다. 게이지는 적을 잡아야만 차므로, <b>궁극기 연출을 한 번
/// 보려면 매번 적을 여러 마리 잡아야 한다.</b>
///
/// 궁극기는 이 프로젝트에서 손이 제일 많이 간 연출이다(이펙트 배율, 정렬 레이어, 모션을
/// 덮는 castDelay). 그 확인 한 번의 비용이 "전투 한 판"이면 확인을 덜 하게 되고, 덜 확인한
/// 것이 그대로 남는다. <see cref="TimeScaleDebugControl"/>로 늦춰 봐도 <b>궁극기를 쏠 수
/// 없으면 소용이 없다</b> — 그래서 둘이 짝이다.
///
/// <b>파일 전체를 <c>#if UNITY_EDITOR</c>로 감쌌다.</b> <see cref="BossKeyDebugGrant"/>와 같은 이유다.
///
/// <b>게이지를 직접 대입하지 않고 <see cref="AshGauge.AddKillCharge"/>를 반복해서 부른다.</b>
/// <see cref="BossKeyDebugGrant"/>가 지급 경로를 따로 만들지 않고 <c>Acquire</c>를 그대로 쓴 것과
/// 같은 판단이다 — 조사용 경로가 실제 경로와 갈라지면 "디버그로는 되는데 실제로는 안 된다"가
/// 생긴다. 유물의 <see cref="AshGauge.BonusChargePerKill"/> 보정도 이 길로 가야 그대로 반영된다.
///
/// (<see cref="AshGauge.Current"/>가 <c>private set</c>이라 대입할 수단이 애초에 없기도 하다.
///  그걸 열어달라고 게임 코드를 고치는 것은 조사용 도구가 할 일이 아니다.)
/// </summary>
[DisallowMultipleComponent]
public class AshGaugeDebugFill : MonoBehaviour
{
    /// <summary>
    /// F11인 이유 — <b>F10은 유니티 Recorder의 녹화 시작/정지 단축키다.</b>
    ///
    /// 처음에 F10으로 뒀다가 옮겼다. 시간 제어(F7~F9) 옆에 붙이려던 것이었는데, 이 프로젝트는
    /// 연출을 확인할 때 Recorder로 녹화를 자주 건다. 그 상태에서 게이지를 채우려고 누르면
    /// <b>녹화가 같이 시작되거나 끊긴다.</b> 조사용 도구가 조사 자체를 방해하는 셈이다.
    ///
    /// F10만 건너뛰고 F11로 갔다. 시간 제어 묶음과 여전히 붙어 있어 손이 기억하기 좋다.
    /// <b>여기를 F10으로 "정리"하지 마라</b> — 빈 자리처럼 보이지만 비어 있는 것이 아니다.
    /// </summary>
    private const Key FillKey = Key.F11;

    /// <summary>
    /// 반복 횟수 상한.
    ///
    /// 왜 필요한가: <c>chargePerKill</c>이 0이면 아무리 불러도 게이지가 안 차서 <b>while이
    /// 영영 안 끝나고 에디터가 통째로 멈춘다.</b> 인스펙터에서 실수로 0을 넣는 것은 충분히
    /// 있을 수 있는 일이고, 그때 증상이 "유니티가 죽었다"가 되면 원인을 찾기 어렵다.
    ///
    /// 기본값(최대 100 / 킬당 10)이면 10번이면 찬다. 100은 그 열 배라 정상 설정에서는
    /// 절대 안 걸리면서, 잘못된 설정은 확실히 잡아낸다.
    /// </summary>
    private const int MaxIterations = 100;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[AshGaugeDebugFill]");
        go.AddComponent<AshGaugeDebugFill>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (!keyboard[FillKey].wasPressedThisFrame) return;

        Fill();
    }

    private static void Fill()
    {
        // 꺼져 있는 순간에도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
        var gauge = FindFirstObjectByType<AshGauge>(FindObjectsInactive.Include);
        if (gauge == null)
        {
            Debug.LogWarning("[조사용] AshGauge를 못 찾았다. 게임 씬에서 눌러라.");
            return;
        }

        if (gauge.IsFull)
        {
            Debug.Log("[조사용] 재 게이지가 이미 가득이다. R을 쓸 수 있다.");
            return;
        }

        int calls = 0;
        while (!gauge.IsFull && calls < MaxIterations)
        {
            gauge.AddKillCharge();
            calls++;
        }

        if (!gauge.IsFull)
        {
            Debug.LogWarning($"[조사용] {MaxIterations}번 불렀는데도 안 찼다. " +
                             "AshGauge의 chargePerKill이 0인지 확인해라.");
            return;
        }

        Debug.Log($"[조사용] 재 게이지 가득 참 (킬 {calls}회분). R을 쓸 수 있다.\n" +
                  "연출을 뜯어보려면 F7로 0.1배속을 걸고 쏴라.");
    }
}
#endif
