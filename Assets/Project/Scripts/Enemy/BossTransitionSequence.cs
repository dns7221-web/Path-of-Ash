using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 추가 생성 — 재의 왕 2페이즈 전환 연출의 <b>바깥쪽 손잡이</b>.
///
/// <b>이 클래스는 시간표를 들고 있지 않다.</b> 3.125초 계획표(무엇을 언제 켜고 끄는지)는
/// 전부 <c>BossTransition.playable</c> 에셋 안에 있고, 여기서는 그걸 재생하고 몇 개의
/// 시그널을 받아 넘길 뿐이다.
///
/// <b>왜 코루틴으로 안 짰는가:</b> 2026-09-01 기록에 "3.125초 타임라인은 결국 그림을 보면서
/// 조정할 값"이라고 적어뒀다. 코루틴이면 0.875를 0.95로 바꿔보는 데 코드 수정 → 컴파일 →
/// Play가 매번 필요하다. 타임라인이면 클립 끝을 끌고 스크럽해서 바로 본다. 이 연출은
/// 보스전의 유일한 절정이라 조정 횟수가 확실히 많다.
///
/// 덤으로 얻는 것: <b>연출 길이가 한 곳에만 적힌다.</b> 예전에는 클립이 0.875초인데 코드가
/// 0.75초를 기다려서 마지막 프레임을 아무도 못 봤다. 이제 보스는 <see cref="TotalSeconds"/>를
/// 물어보므로 두 숫자가 갈라질 자리가 없다.
///
/// <b>왜 SignalReceiver 컴포넌트를 안 쓰는가:</b> 시그널을 받는 표준 방법은
/// <c>SignalReceiver</c> + UnityEvent 연결이지만, 그러면 "시그널이 오면 무엇을 하는가"가
/// 인스펙터의 UnityEvent 목록에 적힌다. 그건 <b>코드에서 안 보이는 연결</b>이고, 보스
/// 프리팹을 다시 만들 때마다 손으로 다시 이어야 한다. <c>INotificationReceiver</c>를 직접
/// 구현하면 같은 내장 기능을 쓰면서 분기가 코드에 남는다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayableDirector))]
public class BossTransitionSequence : MonoBehaviour, INotificationReceiver
{
    [Header("시그널")]
    [Tooltip("갑옷이 다 무너진 순간(계획표 0.875). 보스 스프라이트를 숨긴다.")]
    [SerializeField] private SignalAsset armorBrokenSignal;

    [Tooltip("껍질이 깨지는 순간(계획표 2.375). 이름이 바뀌고 체력바가 다시 찬다.")]
    [SerializeField] private SignalAsset revealedSignal;

    [Tooltip("2페이즈 보스가 나타나는 순간(계획표 2.875). 컨트롤러를 갈아 끼우고 다시 보인다.")]
    [SerializeField] private SignalAsset bossReturnsSignal;

    [Header("화면 흔들림")]
    [Tooltip("연출이 시작될 때(갑옷 붕괴) 흔들 세기와 시간.")]
    [SerializeField, Range(0f, 1f)] private float startShakeStrength = 1f;
    [SerializeField, Min(0f)] private float startShakeSeconds = 0.9f;

    [Tooltip("껍질이 깨질 때 흔들 세기와 시간. 시작보다 짧고 날카롭게.")]
    [SerializeField, Range(0f, 1f)] private float revealShakeStrength = 0.75f;
    [SerializeField, Min(0f)] private float revealShakeSeconds = 0.35f;

    private PlayableDirector director;
    private CameraShake cameraShake;

    // 추가 생성(시그널 유실 수정) — 이번 재생에서 각 신호가 이미 울렸는지.
    //
    // <b>왜 필요한가:</b> 신호가 두 경로로 들어올 수 있다. SignalReceiver의 UnityEvent와
    // 아래 <see cref="OnNotify"/>다. 둘 다 살려둔 이유는 <b>어느 쪽이 실제로 도착하는지가
    // 유니티 버전과 바인딩 상태에 달려 있기 때문</b>이다. 한쪽만 남기면 그쪽이 안 올 때
    // 연출이 통째로 죽는데, 실제로 그렇게 죽었다 — 전환이 끝나도 체력바가 안 차고
    // 이름이 "???"로 남았다.
    //
    // 둘 다 살리면 이번엔 <b>두 번 울릴</b> 위험이 생긴다. 이 세 값이 그것을 막는다.
    private bool firedArmorBroken;
    private bool firedRevealed;
    private bool firedBossReturns;

    /// <summary>
    /// 갑옷이 다 무너진 순간. 보스가 받아서 스프라이트를 숨긴다.
    ///
    /// 숨기는 일을 이 클래스가 직접 안 하는 이유: 스프라이트는 <b>보스의 몸</b>이다.
    /// 연출이 남의 몸을 직접 끄면, 연출이 중간에 끊겼을 때 누가 다시 켜주는지가 흐려진다.
    /// </summary>
    public event Action ArmorBroken;

    /// <summary>껍질이 깨진 순간. 보스가 받아서 밖(체력바)에 전환을 알린다.</summary>
    public event Action Revealed;

    /// <summary>2페이즈 보스가 나타나는 순간. 보스가 받아서 컨트롤러를 갈아 끼운다.</summary>
    public event Action BossReturns;

    /// <summary>
    /// 연출 전체 길이(초). 보스는 이 값만큼 기다린다.
    ///
    /// 타임라인 에셋이 곧 이 길이의 주인이다. 인스펙터에 같은 숫자를 또 적어두지 않는다.
    ///
    /// <c>director.duration</c>이 아니라 <c>playableAsset.duration</c>을 읽는 이유:
    /// 앞의 것은 <b>재생 그래프가 만들어진 뒤</b>에야 값이 찬다. 보스는 재생을 시작하기
    /// 전에 이 값을 물어보므로, 그때는 0이 돌아와서 <b>기다리지 않고 곧장 넘어간다.</b>
    /// 에셋 쪽은 클립 배치에서 바로 계산되는 값이라 언제 물어봐도 같다.
    /// </summary>
    public float TotalSeconds => director != null && director.playableAsset != null
        ? (float)director.playableAsset.duration
        : 0f;

    private void Awake()
    {
        director = GetComponent<PlayableDirector>();

        // 카메라는 프리팹 밖에 있어서 타임라인이 바인딩할 수 없다. 그래서 흔들림만
        // 시그널을 받아 코드가 건다. 못 찾아도 연출은 그대로 돌아간다 — 흔들림이 없을 뿐이다.
        if (Camera.main != null) cameraShake = Camera.main.GetComponent<CameraShake>();

        if (cameraShake == null)
            Debug.LogWarning("[보스 전환] Main Camera에 CameraShake가 없다. 화면이 안 흔들린다.", this);
    }

    /// <summary>
    /// 연출을 시작한다. 시작 흔들림은 여기서 건다 — 계획표의 0.000 지점이라
    /// 시그널을 하나 더 만들 것 없이 재생과 같은 순간이다.
    /// </summary>
    public void Play()
    {
        // 수정(시그널 유실 추적) — 이번 재생에서 아직 아무 신호도 안 왔다.
        firedArmorBroken = false;
        firedRevealed = false;
        firedBossReturns = false;

        cameraShake?.Shake(startShakeStrength, startShakeSeconds);
        director.Play();
    }

    /// <summary>
    /// 계획표 <c>0.875</c>. <see cref="SignalReceiver"/>의 UnityEvent가 부른다.
    ///
    /// public인 이유는 하나뿐이다 — UnityEvent는 public 메서드만 연결할 수 있다.
    /// 배선은 빌더가 하므로 손으로 이을 일은 없다.
    /// </summary>
    public void RaiseArmorBroken()
    {
        if (firedArmorBroken) return;
        firedArmorBroken = true;

        ArmorBroken?.Invoke();
    }

    /// <summary>계획표 <c>2.375</c>. 껍질이 깨진다.</summary>
    public void RaiseRevealed()
    {
        if (firedRevealed) return;
        firedRevealed = true;

        cameraShake?.Shake(revealShakeStrength, revealShakeSeconds);
        Revealed?.Invoke();
    }

    /// <summary>계획표 <c>2.875</c>. 2페이즈 보스가 나타난다.</summary>
    public void RaiseBossReturns()
    {
        if (firedBossReturns) return;
        firedBossReturns = true;

        BossReturns?.Invoke();
    }

    /// <summary>
    /// 연출을 즉시 멈추고 켜둔 것을 전부 되돌린다.
    ///
    /// <c>Stop</c>이 Activation Track이 켜둔 오브젝트까지 원래 상태로 돌려놓는다
    /// (<c>ActivationTrack.postPlaybackState</c>). 그래서 여기서 이펙트를 하나씩 끌 필요가 없다.
    /// </summary>
    public void StopAndReset()
    {
        director.Stop();
        cameraShake?.StopShake();
    }

    /// <summary>
    /// 타임라인의 시그널이 여기로 들어온다.
    ///
    /// 시그널 트랙에 바인딩을 안 걸어두면 알림이 <c>PlayableDirector</c>가 붙은
    /// 오브젝트로 오고, 그 오브젝트의 <c>INotificationReceiver</c>들이 전부 받는다.
    ///
    /// 이름이 아니라 <b>에셋 참조로 비교</b>하는 이유: 이름 비교는 시그널 파일 이름을
    /// 바꾸는 순간 조용히 안 맞게 된다. 에셋 참조는 파일을 옮기거나 이름을 바꿔도 살아 있고,
    /// 참조가 비면 인스펙터에서 눈으로 보인다.
    /// </summary>
    public void OnNotify(Playable origin, INotification notification, object context)
    {
        if (notification is not SignalEmitter emitter) return;

        SignalAsset asset = emitter.asset;

        if (asset == armorBrokenSignal)
        {
            RaiseArmorBroken();
        }
        else if (asset == revealedSignal)
        {
            RaiseRevealed();
        }
        else if (asset == bossReturnsSignal)
        {
            RaiseBossReturns();
        }
        else
        {
            // 조용히 넘기지 않는 이유: 시그널을 새로 추가하고 여기 분기를 안 달면
            // <b>아무 일도 안 일어나는데 에러도 없다.</b> 그건 찾는 데 제일 오래 걸리는 종류다.
            Debug.LogWarning($"[보스 전환] 받는 데가 없는 시그널이다: {asset?.name}", this);
        }
    }
}
