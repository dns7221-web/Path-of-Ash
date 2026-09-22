using UnityEngine;

/// <summary>
/// 잿불 망령의 행동을 소유한다. <b>답은 "옆으로 피한다"</b> — 곧게 달려드는 돌진이다.
///
/// 상태 흐름:
///   Roam ──(탐지)──> Chase ──(사거리)──> Windup ──> Charge ──> Cooldown ──> Chase
///   Chase ──(놓침)──> Roam
///   어디서든 ──(피격)──> Hit ──> Cooldown
///   어디서든 ──(사망)──> Dead
///
/// 수정(코드 리뷰 시점): Chase와 Cooldown을 추가했다. 이전 구조는 Roam(랜덤 배회) 다음이
/// 바로 Windup이라 세 가지가 동시에 깨져 있었다.
/// 1) 플레이어를 향해 다가가는 상태가 없어서, 한 번 돌진하고 빗나가면 아무 방향으로나
///    걸어갔다. 적이 쫓아오지 않으니 위협이 되지 않았다.
/// 2) 탐지 반경(7)이 돌진 이동거리(12 x 0.34 = 4.08유닛)보다 커서 돌진이 항상 허공을
///    지나갔다. 지금은 <see cref="chargeRange"/>를 돌진 거리보다 짧게 두어 반드시 닿는다.
/// 3) 돌진 직후 바로 다시 탐지되어 끊김 없이 돌진을 반복했다. Cooldown이 그 틈을 만든다.
///
/// 수정(적 추가): 탐지·넉백·피격·사망·풀 복귀를 <see cref="EnemyBase"/>로 옮겼다.
/// 여기 남은 것은 <b>이 적이 어떻게 싸우는지</b>뿐이다.
/// 옮긴 필드의 이름을 그대로 뒀기 때문에 프리팹에 저장된 값은 그대로 살아 있다 —
/// 유니티는 어느 클래스가 선언했는지가 아니라 <b>이름</b>으로 직렬화 값을 찾는다.
///
/// <b>속도 값의 기준</b>: 플레이어 걷기 11 / 달리기 20 / 방 49.8x28유닛이다.
/// 추격이 걷기보다 느려야 플레이어가 걸어서 거리를 벌 수 있고(스태미나를 안 쓰는 선택지),
/// 돌진은 달리기보다 빨라야 "달리면 무조건 안전"이 되지 않는다. 그 사이의 긴장이 이 적의
/// 전부다.
/// </summary>
public class EnemyWraith : EnemyBase
{
    private enum State { Roam, Chase, Windup, Charge, Cooldown, Hit, Dead }

    [Header("돌진 사거리")]
    [Tooltip("이 거리 안으로 들어오면 돌진을 준비한다. 돌진 이동거리(chargeSpeed x " +
             "chargeSeconds)보다 짧아야 돌진이 실제로 닿는다.")]
    [SerializeField, Min(0.1f)] private float chargeRange = 7f;

    [Header("이동 속도")]
    [Tooltip("배회 속도. 플레이어를 못 찾은 상태라 느긋해도 된다.")]
    [SerializeField, Min(0f)] private float roamSpeed = 6f;

    [Tooltip("추격 속도. 플레이어 걷기(11)보다 느려야 걸어서 거리를 벌 수 있다.")]
    [SerializeField, Min(0f)] private float chaseSpeed = 8f;

    [Tooltip("돌진 속도. 플레이어 달리기(20)보다 빨라야 달리기만으로 안전해지지 않는다.")]
    [SerializeField, Min(0f)] private float chargeSpeed = 24f;

    [Tooltip("배회 방향을 바꾸는 주기(초).")]
    [SerializeField, Min(0.1f)] private float roamDirectionSeconds = 1.2f;

    [Header("동작 시간")]
    [Tooltip("예비동작(초). windup 클립 4프레임 / 10fps = 0.4초에 맞췄다.")]
    [SerializeField, Min(0f)] private float windupSeconds = 0.4f;

    [Tooltip("돌진(초). charge 클립 4프레임 / 12fps = 0.33초에 맞췄다.")]
    [SerializeField, Min(0f)] private float chargeSeconds = 0.34f;

    [Tooltip("돌진 후 숨 고르는 시간(초). 이게 없으면 플레이어가 범위 안에 있는 동안 " +
             "끊김 없이 돌진을 반복한다.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 0.7f;

    [Header("참조")]
    [SerializeField] private DamageHitbox chargeHitbox;

    // 추가 생성(2026-09-15) — 돌진 예고선.
    [Header("돌진 예고선")]
    [Tooltip("예비동작 동안 돌진할 길을 바닥에 긋는 선. Tools → 재의 길 → 프리팹 → 망령 돌진 예고선 생성 이 만들어 꽂는다. " +
             "비어 있어도 돌진은 멀쩡히 돈다 — 선 없이 예비동작 모션만 보인다.")]
    [SerializeField] private TelegraphLine chargeTelegraphPrefab;

    // 추가 생성(2026-09-15) — 돌진 출발 자국.
    [Header("돌진 출발 자국")]
    [Tooltip("돌진이 시작되는 순간 출발 자리 바닥에 남기는 자국. Tools → 재의 길 → 프리팹 → 망령 돌진 출발 자국 생성 이 만들어 꽂는다. " +
             "비어 있어도 돌진은 멀쩡히 돈다 — 자국 없이 몸만 튀어 나간다.")]
    [SerializeField] private GameObject chargeLaunchEffectPrefab;

    // 추가 생성(2026-09-19, 망령-1 예비동작 불씨) — 예비동작을 시작할 때 몸 앞에 만드는 모으기 이펙트.
    // 예고선이 "어디로"를 보여준다면 이것은 "곧"을 보여준다. 0.4초 동안 불씨가 앞발로 빨려 든다.
    [Header("파티클 (없어도 동작한다)")]
    [Tooltip("예비동작을 시작할 때 몸 앞에 만들 모으기 이펙트(WraithWindupGather). 비우면 안 만든다.")]
    [SerializeField] private GameObject windupEffectPrefab;

    [Tooltip("모으기 이펙트를 발밑에서 화면 위로 띄울 높이(유닛). 앞발·가슴 높이.")]
    [SerializeField, Min(0f)] private float windupEffectHeight = 2.6f;

    [Tooltip("모으기 이펙트를 돌진 방향으로 밀 거리(유닛).")]
    [SerializeField, Min(0f)] private float windupEffectForward = 1.2f;

    // 추가 생성(2026-09-19, 망령-2 돌진 길 불씨) — 돌진하는 동안만 방출을 켜는 바닥 불씨(자식).
    // 0.34초에 8유닛을 가서 몸만 보면 어디를 지나갔는지 안 남는다. 플레이어 대시 불씨(DashTrail)와 같은 방식이다.
    [Tooltip("돌진하는 동안만 방출을 켤 바닥 불씨(자식, 평소에는 방출이 꺼져 있다). 비우면 안 남긴다.")]
    [SerializeField] private ParticleSystem chargeTrail;

    /// <summary>
    /// 추가 생성(2026-09-15) — 출발 자국이 스스로 안 사라질 때 강제로 지우기까지의 시간(초).
    /// 지금 프리팹은 6프레임 / 16fps = 0.375초 뒤 스스로 지운다. 반복 재생으로 잘못 설정된 프리팹을 꽂았을 때
    /// 돌진마다 방에 쌓이지 않게 하는 안전장치다(PlayerController.DashEffectMaxLifetime과 같은 이유).
    /// </summary>
    private const float LaunchEffectMaxLifetime = 2f;

    private State state;
    private Vector2 roamDirection;
    private Vector2 chargeDirection = Vector2.right;
    private float stateTimer;
    private float roamTimer;

    // 추가 생성(2026-09-15) — 예고선 인스턴스. 처음 쓸 때 자식으로 만들어 두고 돌진마다 켰다 끈다.
    private TelegraphLine chargeTelegraph;

    // 추가 생성(2026-09-15) — 돌진 판정 상자의 모양. 예고선의 길이와 높이를 이 상자에서 읽는다.
    private BoxCollider2D chargeHitboxShape;

    // ── 수명 ──────────────────────────────────────────────────────────────

    protected override void OnSpawned()
    {
        EnterRoam();
    }

    protected override void OnDespawned()
    {
        chargeHitbox?.Deactivate();

        // 추가 생성(2026-09-15) — 예비동작 중에 풀로 돌아가면(방 정리 등) 선이 켜진 채 다음 방에 나온다.
        HideChargeTelegraph();

        // 추가 생성(2026-09-19) — 풀에서 다시 나올 때 지난 돌진의 불씨가 남아 있으면 안 된다.
        SetChargeTrail(false, clear: true);
    }

    /// <summary>
    /// 추가 생성(2026-09-19, 망령-2) — 돌진 길 불씨의 방출만 켜고 끈다. 시스템은 계속 돌고 있어서, 끄면 이미 깔린 불씨는
    /// 제 수명대로 꺼진다. clear면 깔린 불씨까지 지운다(풀로 돌아갈 때).
    /// </summary>
    private void SetChargeTrail(bool on, bool clear = false)
    {
        if (chargeTrail == null) return;

        ParticleSystem.EmissionModule emission = chargeTrail.emission;
        emission.enabled = on;

        if (clear) chargeTrail.Clear(true);
    }

    /// <summary>
    /// 추가 생성(2026-09-19, 망령-1) — 몸 앞에 모으기 이펙트를 만든다. 자식이 아니라 월드에 둔다 — 예비동작 동안 몸은
    /// 제자리라 따라갈 일이 없고, 수명(0.4초 안팎)은 프리팹의 Stop Action이 정리한다.
    /// </summary>
    private void SpawnWindupEffect()
    {
        if (windupEffectPrefab == null) return;

        Vector3 position = transform.position
                           + (Vector3)(chargeDirection * windupEffectForward)
                           + Vector3.up * windupEffectHeight;
        Instantiate(windupEffectPrefab, position, Quaternion.identity);
    }

    private void Update()
    {
        if (state == State.Dead) return;

        switch (state)
        {
            case State.Roam:
                UpdateRoam();
                break;

            case State.Chase:
                UpdateChase();
                break;

            default:
                UpdateTimedState();
                break;
        }
    }

    protected override Vector2 ResolveVelocity()
    {
        return state switch
        {
            State.Roam => roamDirection * roamSpeed,
            State.Chase => ChaseDirection() * chaseSpeed,
            State.Charge => chargeDirection * chargeSpeed,

            // 경직 중에는 밀려나는 속도만 남는다.
            State.Hit => KnockbackVelocity,

            // Windup / Cooldown / Dead는 제자리에 선다.
            // 예비동작 중에 움직이면 플레이어가 "지금 돌진이 온다"를 읽을 수 없다.
            _ => Vector2.zero,
        };
    }

    // ── 배회 ──────────────────────────────────────────────────────────────

    private void UpdateRoam()
    {
        UpdateRoamDirection();

        if (TryAcquireTarget(DetectionRadius))
            state = State.Chase;
    }

    /// <summary>배회 방향을 일정 간격으로 바꿔 매 프레임 떨리는 랜덤 이동을 막는다.</summary>
    private void UpdateRoamDirection()
    {
        roamTimer -= Time.deltaTime;
        if (roamTimer > 0f) return;

        roamTimer = roamDirectionSeconds;
        roamDirection = Random.insideUnitCircle.normalized;
        UpdateFacing(roamDirection.x);
    }

    // ── 추격 ──────────────────────────────────────────────────────────────

    private void UpdateChase()
    {
        // 대상이 사라졌거나(스폰 해제) 죽었으면 추격할 이유가 없다.
        if (!HasLiveTarget)
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        float distance = DistanceToTarget();

        // 탐지 반경이 아니라 더 넓은 LoseRadius로 판단한다(히스테리시스).
        // 두 값이 같으면 경계선에서 추격과 배회가 매 프레임 번갈아 바뀌며 떤다.
        if (distance > LoseRadius)
        {
            ClearTarget();
            EnterRoam();
            return;
        }

        UpdateFacing(Target.position.x - Body.position.x);

        if (distance <= chargeRange)
            BeginWindup();
    }

    /// <summary>추격 중 나아갈 방향. 대상이 없으면 멈춘다.</summary>
    private Vector2 ChaseDirection() => DirectionToTarget();

    // ── 시간이 정해진 상태들 (Windup / Charge / Cooldown / Hit) ──────────────

    private void UpdateTimedState()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        switch (state)
        {
            case State.Windup:
                BeginCharge();
                break;

            case State.Charge:
                BeginCooldown();
                break;

            // 경직이 끝나면 바로 달려들지 않고 Cooldown을 한 번 거친다.
            //
            // 이게 "턴제 느낌"을 만드는 핵심이다. 맞자마자 다시 붙으면 플레이어가 칼을 휘두르는
            // 내내 적이 코앞에 있어서 공격과 피격이 뭉개진다. 경직(0.2초) + 숨 고르기(0.7초)
            // 동안 적이 물러나 있으면, 플레이어의 공격 쿨다운(0.65초)과 주기가 맞물려
            // "쳤다 → 물러났다 → 자리 잡았다 → 다시 친다"는 리듬이 생긴다.
            case State.Hit:
                BeginCooldown();
                break;

            // 숨 고르기가 끝나면 추격으로 돌아간다. 배회로 보내면 눈앞의 플레이어를
            // 두고 딴 데로 걸어가서, 한 번 맞히면 적이 흥미를 잃는 것처럼 보인다.
            case State.Cooldown:
                ResumeChaseOrRoam();
                break;
        }
    }

    private void BeginWindup()
    {
        state = State.Windup;
        stateTimer = windupSeconds;

        // 돌진 방향을 <b>예비동작이 시작될 때</b> 고정한다.
        // 끝날 때 정하면 플레이어가 어디로 피하든 따라붙어서 피할 방법이 없어진다.
        // 시작할 때 고정하면 0.4초의 예비동작이 "지금 옆으로 비키면 산다"는 신호가 된다.
        chargeDirection = DirectionToTarget();
        if (chargeDirection.sqrMagnitude < 0.01f) chargeDirection = Vector2.right;

        UpdateFacing(chargeDirection.x);
        Animator?.SetTrigger(AttackHash);

        // 추가 생성(2026-09-15) — 판정 상자를 고정한 돌진 방향으로 돌린다. 이유는 OrientChargeHitbox 참고.
        // 아래 예고선이 길이와 굵기를 이 상자에서 재므로 반드시 그 앞에 부른다.
        OrientChargeHitbox(chargeDirection);

        // 추가 생성(2026-09-15) — 고정한 방향으로 선을 긋는다. 좌우 반전이 판정 상자 위치를 바꾸므로
        // 반드시 UpdateFacing 뒤에 부른다(길이를 그 상자에서 잰다).
        // 수정(2026-09-15) — 이제 상자를 바꾸는 것은 좌우 반전이 아니라 바로 위의 회전이다. 순서 조건은 그대로다.
        ShowChargeTelegraph();

        // 추가 생성(2026-09-19, 망령-1) — 선과 같은 순간에 불씨를 모은다. 방향이 고정된 뒤라 몸 앞을 정확히 안다.
        SpawnWindupEffect();
    }

    private void BeginCharge()
    {
        state = State.Charge;
        stateTimer = chargeSeconds;
        chargeHitbox?.Activate();

        // 추가 생성(2026-09-15) — 선이 갈라지는 마지막 프레임이 끝나는 순간이 곧 돌진이다. 몸이 선을 따라 나가므로 선은 거둔다.
        HideChargeTelegraph();

        // 추가 생성(2026-09-15) — 선이 걷히는 바로 그 자리에 출발 자국을 남긴다. 경고(선)가 사건(자국)으로 바뀌는 순간이다.
        SpawnLaunchEffect();

        // 추가 생성(2026-09-19, 망령-2) — 몸이 미끄러지는 동안만 바닥 불씨를 뿌린다.
        SetChargeTrail(true);
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 돌진을 시작한 자리 바닥에 출발 자국을 남긴다. 이펙트가 없으면 아무 일도 안 한다.
    ///
    /// <b>왜 필요한가.</b> 돌진은 0.34초에 8유닛을 간다. 너무 빨라서 몸만 보면 "어디서 튀어나왔는지"가 눈에 안 남고,
    /// 맞은 플레이어는 무엇에 맞았는지부터 되짚어야 한다. 플레이어 대시 자국(PlayerController.SpawnDashEffect)과 같은 역할이다.
    ///
    /// 자식이 아니라 월드에 놓는 이유: "여기서 출발했다"는 표시라 몸을 따라가면 뜻이 없어진다.
    ///
    /// 발밑(높이 0)에 두는 이유: 그림이 바닥이 갈라져 터지는 고리다. 예고선은 판정 상자 가운데 높이에서 그어지지만
    /// 그건 "어디까지 맞는가"를 알리는 선이고, 자국은 "어디를 딛고 나갔는가"라 발이 닿은 자리여야 한다.
    ///
    /// 회전만으로 방향을 맞추는 이유: 그림이 오른쪽으로 뻗고 위아래 대칭으로 그려져 있다(생성 프롬프트의 조건).
    /// 피벗이 고리의 왼쪽 끝(출발점)이라, 방향으로 돌리면 망령이 선 자리를 축으로 돈다(AshVfxSpriteSlicer의 Forward).
    /// </summary>
    private void SpawnLaunchEffect()
    {
        if (chargeLaunchEffectPrefab == null) return;

        float angle = Mathf.Atan2(chargeDirection.y, chargeDirection.x) * Mathf.Rad2Deg;
        var effect = Instantiate(chargeLaunchEffectPrefab, transform.position, Quaternion.Euler(0f, 0f, angle));
        Destroy(effect, LaunchEffectMaxLifetime);
    }

    private void BeginCooldown()
    {
        state = State.Cooldown;
        stateTimer = cooldownSeconds;

        // 추가 생성(2026-09-19) — 돌진이 끝났다. 걷는 동안에도 뿌리면 발자국이 아니라 불길이 된다.
        SetChargeTrail(false);
        chargeHitbox?.Deactivate();
    }

    /// <summary>대상이 아직 유효하면 추격으로, 아니면 배회로 돌아간다.</summary>
    private void ResumeChaseOrRoam()
    {
        if (HasLiveTarget)
        {
            state = State.Chase;
            return;
        }

        ClearTarget();
        EnterRoam();
    }

    private void EnterRoam()
    {
        state = State.Roam;
        stateTimer = 0f;
        roamTimer = 0f;
        chargeHitbox?.Deactivate();

        // 추가 생성(2026-09-15) — 풀에서 다시 나올 때도 여기로 들어온다. 선이 꺼진 상태에서 시작한다.
        HideChargeTelegraph();
    }

    // ── 돌진 예고선 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 추가 생성(2026-09-15) — 돌진할 길을 바닥에 긋는다. 방향이 고정된 직후(예비동작 시작)에 부른다.
    ///
    /// 선 길이를 몸이 가는 거리(chargeSpeed x chargeSeconds)가 아니라 <see cref="ChargeReach"/>로 잡는다.
    /// 판정 상자가 몸 앞으로 나가 있어서, 몸이 멈추는 자리에서 선을 끝내면 <b>화살촉 너머에서도 맞는다.</b>
    /// 선 끝 너머에 서 있으면 안전하다고 배운 플레이어가 거기서 맞으면 예고선 자체를 믿지 않게 된다.
    ///
    /// 예비동작 시간을 재생 시간으로 넘기는 이유: 선이 갈라지는 마지막 프레임이 끝나는 순간이
    /// 돌진이 시작되는 순간이어야 한다. windupSeconds를 바꿔도 둘이 같이 움직인다.
    /// </summary>
    private void ShowChargeTelegraph()
    {
        if (chargeTelegraphPrefab == null) return;

        // 처음 한 번만 만든다. 망령은 풀에서 재사용되므로 자식으로 붙은 선도 같이 재사용된다.
        // 돌진마다 만들고 지우면, 예비동작 중에 맞아 돌진이 취소될 때 지울 시점을 따로 챙겨야 한다.
        if (chargeTelegraph == null)
        {
            chargeTelegraph = Instantiate(chargeTelegraphPrefab, transform);
            chargeTelegraph.name = chargeTelegraphPrefab.name;
        }

        // 수정(2026-09-15) — 판정 상자의 굵기도 받아서 넘긴다. 선이 판정보다 가늘면 선 바로 옆에 서도 맞는다.
        float reach = ChargeReach(chargeDirection, out float lineHeight, out float lineThickness);
        chargeTelegraph.transform.localPosition = new Vector3(0f, lineHeight, 0f);
        chargeTelegraph.Show(chargeDirection, reach, lineThickness, windupSeconds);
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 예고선을 끈다. 돌진이 시작됐거나 취소됐을 때(피격·사망·풀 복귀) 부른다.
    ///
    /// 취소될 때 끄는 것이 중요하다. 맞아서 돌진이 취소됐는데 선이 남아 있으면 <b>오지 않을 돌진</b>을 알리게 되고,
    /// 플레이어는 비키지 않아도 되는 자리에서 비키게 된다.
    /// </summary>
    private void HideChargeTelegraph()
    {
        if (chargeTelegraph != null) chargeTelegraph.Hide();
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 이번 돌진이 닿는 가장 먼 거리(유닛)를 선의 출발점부터 잰다.
    ///
    /// 몸이 가는 거리에, 판정 상자가 돌진 방향으로 선의 출발점보다 앞서 있는 만큼을 더한다. 상자의 네 모서리를
    /// 돌진 방향으로 투영해서 가장 앞선 값을 쓴다. 상자는 좌우로만 뒤집히고 돌진 방향으로 돌지 않아서
    /// 방향마다 앞서는 양이 다르다 — 옆으로 돌진하면 상자 폭만큼, 위로 돌진하면 상자 윗변까지다.
    /// 판정이 상자 모양을 바꾸면(콜라이더 재조정) 선 길이도 코드 수정 없이 따라간다.
    ///
    /// 반드시 UpdateFacing 뒤에 불러야 한다. 좌우 반전이 상자 위치를 바꾸기 때문이다.
    ///
    /// 수정(2026-09-15) — 상자가 이제 돌진 방향으로 돈다(OrientChargeHitbox). 그래서 앞서는 양은 어느 방향이든
    /// 상자 오프셋 + 반폭(지금 4.11)으로 같다. 모서리 투영은 그대로 뒀다 — 상자가 어떻게 놓여 있든 맞는 계산이라
    /// 회전을 빼먹은 경우에도 선이 판정보다 짧아지지 않는다. 불러야 하는 조건도 "UpdateFacing 뒤"에서
    /// "OrientChargeHitbox 뒤"로 바뀌었다. 선의 굵기도 같이 잰다.
    /// </summary>
    /// <param name="direction">돌진 방향.</param>
    /// <param name="lineHeight">선의 출발점 높이(발밑 기준). 판정 상자의 세로 가운데라, 옆으로 돌진할 때 선이 판정 한가운데를 지난다.</param>
    /// <param name="lineThickness">추가 생성(2026-09-15) — 선이 덮어야 하는 굵기(유닛). 상자를 돌진 방향과 수직으로 잰 폭이다. 상자가 없으면 0.</param>
    private float ChargeReach(Vector2 direction, out float lineHeight, out float lineThickness)
    {
        float travel = chargeSpeed * chargeSeconds;
        lineHeight = 0f;
        lineThickness = 0f;

        // 수정(2026-09-15) — 상자 찾기를 CacheChargeHitboxShape로 뺐다. 회전 함수도 같은 상자를 쓴다.
        CacheChargeHitboxShape();

        // 상자가 없거나 다른 모양이면 몸이 가는 거리만 쓴다. 선이 짧아질 뿐 돌진은 멀쩡하다.
        if (chargeHitboxShape == null || direction.sqrMagnitude < 0.0001f) return travel;

        direction.Normalize();

        Transform box = chargeHitboxShape.transform;
        Vector2 center = chargeHitboxShape.offset;
        Vector2 half = chargeHitboxShape.size * 0.5f;

        // 좌우 반전은 x만 바꾸므로 상자 가운데의 높이는 방향과 무관하다.
        //
        // 수정(2026-09-15) — 상자를 돌리면 가운데의 높이가 방향마다 달라진다(위로 돌진하면 앞으로 민 만큼 위로 간다).
        // 그래서 "지금 상자 가운데"가 아니라 상자를 돌리는 축의 높이를 읽는다. 돌리기 전 상자 가운데의 높이와 같은 값이다.
        lineHeight = ChargePivotHeight();
        Vector2 start = (Vector2)transform.position + Vector2.up * lineHeight;

        // 추가 생성(2026-09-15) — 돌진 방향에 수직인 축. 선의 굵기 방향이다.
        Vector2 side = new Vector2(-direction.y, direction.x);
        float sideReach = 0f;

        float ahead = 0f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector2 corner = box.TransformPoint(center + new Vector2(half.x * sx, half.y * sy));
                ahead = Mathf.Max(ahead, Vector2.Dot(corner - start, direction));

                // 추가 생성(2026-09-15) — 선 가운데에서 가장 멀리 벗어난 모서리.
                sideReach = Mathf.Max(sideReach, Mathf.Abs(Vector2.Dot(corner - start, side)));
            }
        }

        // 추가 생성(2026-09-15) — 선은 가운데를 기준으로 위아래가 같게 늘어나므로, 더 멀리 벗어난 쪽의 두 배를 굵기로 준다.
        // 상자가 돌진 방향으로 돌아 있으면 상자 높이(size.y)와 같다. 돌지 않은 경우에도 선이 판정을 전부 덮는다.
        lineThickness = sideReach * 2f;

        return travel + ahead;
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 돌진 판정 상자를 돌진 방향으로 돌린다. 예비동작에서 방향을 고정한 직후에 부른다.
    ///
    /// <b>왜.</b> 예전에는 좌우 반전(OnFacingChanged)만 해서, 위·아래로 돌진해도 판정은 몸 옆(바라보는 쪽)으로 뻗은
    /// 가로 띠였다. 예고선은 몸에서 돌진 방향으로 곧게 그어지므로, 위로 돌진할 때는 <b>선 위에 서도 안 맞고 선 옆에서 맞았다</b>
    /// (최대 4.1유닛 치우침). 선을 믿고 비킨 플레이어가 맞으면 예고선 전체를 믿지 않게 된다.
    ///
    /// <b>어디를 축으로 돌리나.</b> 발밑이 아니라 발밑 위의 상자 가운데 높이(= 예고선 출발점)다. 자식 오브젝트의 원점(발밑)을
    /// 축으로 돌리면 상자의 세로 오프셋(0.705)까지 같이 돌아서, 위로 돌진할 때 상자가 선 옆으로 0.7유닛 비켜난다.
    /// 출발점을 축으로 돌리면 어느 방향이든 상자 가운데가 예고선 위에 놓인다.
    ///
    /// 플레이어 공격 판정(PlayerController.UpdateVisuals)은 원점을 축으로 돌린다. 그쪽은 선 같은 예고가 없어서 조금 비켜도
    /// 안 드러나지만, 이쪽은 선이 판정을 알리는 유일한 신호라 어긋나면 안 된다.
    ///
    /// 돌진 중에는 방향이 안 바뀌므로 예비동작 시작에 한 번이면 된다. 풀에서 재사용돼도 다음 예비동작에서 다시 돌린다.
    /// </summary>
    private void OrientChargeHitbox(Vector2 direction)
    {
        if (chargeHitbox == null || direction.sqrMagnitude < 0.0001f) return;

        Transform box = chargeHitbox.transform;

        // 방향은 이제 회전이 맡는다. 예전 좌우 반전으로 뒤집혀 있던 스케일이 남아 있으면 되돌린다.
        Vector3 scale = box.localScale;
        scale.x = Mathf.Abs(scale.x);
        box.localScale = scale;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

        // 축 p를 중심으로 돌린 자식의 자리는 p - R·p다. 그러면 콜라이더 가운데는 p + R·(오프셋 x, 0)에 놓인다 —
        // 출발점에서 돌진 방향으로 오프셋 x만큼 나간 자리, 즉 예고선 위다.
        // Transform.RotateAround를 안 쓴 이유: 그건 지금 자리에서 "더" 돌린다. 매 돌진마다 누적되지 않게
        // 기준 자세(원점, 회전 0)에서 한 번에 놓는 식으로 계산한다.
        Vector3 pivot = new Vector3(0f, ChargePivotHeight(), 0f);
        box.localRotation = rotation;
        box.localPosition = pivot - rotation * pivot;
    }

    /// <summary>
    /// 추가 생성(2026-09-15) — 판정 상자를 돌리는 축의 높이(발밑 기준, 유닛). 돌리기 전 상자 가운데의 높이이고, 예고선도 여기서 출발한다.
    ///
    /// 상자를 돌린 뒤에도 같은 값이 나와야 해서 상자의 지금 위치가 아니라 콜라이더 오프셋에서 읽는다.
    /// </summary>
    private float ChargePivotHeight()
    {
        CacheChargeHitboxShape();
        if (chargeHitboxShape == null) return 0f;

        return chargeHitboxShape.offset.y * Mathf.Abs(chargeHitboxShape.transform.localScale.y);
    }

    /// <summary>추가 생성(2026-09-15) — 돌진 판정 상자의 모양을 한 번만 찾아 둔다.</summary>
    private void CacheChargeHitboxShape()
    {
        if (chargeHitboxShape == null && chargeHitbox != null)
            chargeHitboxShape = chargeHitbox.GetComponent<BoxCollider2D>();
    }

    // ── 피격 / 사망 ────────────────────────────────────────────────────────

    protected override void OnStaggered()
    {
        state = State.Hit;

        // 추가 생성(2026-09-19) — 돌진 도중 맞아 멈췄으면 불씨도 멈춘다.
        SetChargeTrail(false);
        stateTimer = HitSeconds;
        chargeHitbox?.Deactivate();

        // 추가 생성(2026-09-15) — 예비동작 중에 맞으면 돌진이 취소된다. 선도 같이 거둔다.
        HideChargeTelegraph();
    }

    protected override void OnDeath()
    {
        state = State.Dead;

        // 추가 생성(2026-09-19) — 돌진 도중 죽었으면 불씨도 멈춘다(깔린 것은 제 수명대로 꺼진다).
        SetChargeTrail(false);
        chargeHitbox?.Deactivate();

        // 추가 생성(2026-09-15) — 죽으면 오지 않을 돌진이다.
        HideChargeTelegraph();
    }

    // ── 표시 ──────────────────────────────────────────────────────────────

    protected override void OnFacingChanged(bool facesLeft)
    {
        // 스프라이트와 돌진 판정을 같은 방향으로 반전한다.
        // flipX는 그림만 뒤집고 자식 Transform에는 영향이 없어서 히트박스를 따로 옮겨야 한다.
        //
        // 수정(2026-09-15) — 판정 상자를 더 이상 여기서 뒤집지 않는다. 좌우 반전으로는 위·아래 돌진을 나타낼 수 없어서,
        // 예비동작에서 방향을 고정할 때 돌진 방향으로 돌린다(OrientChargeHitbox). 여기서 계속 뒤집으면 추격 중에 좌우가
        // 바뀔 때마다 돌려 둔 상자의 스케일이 뒤집혀 거울상이 된다. 그림 반전은 EnemyBase.UpdateFacing이 이미 한다.
        // 빈 채로 남겨 두는 이유: 이 적에게 "방향이 바뀔 때 같이 할 일"이 없어졌다는 것 자체가 설계 변경의 기록이다.
    }

    protected override void DrawExtraGizmos()
    {
        // chargeRange가 돌진 이동거리보다 짧은지는 눈으로 봐야 안다.
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 1f);
        Gizmos.DrawWireSphere(transform.position, chargeRange);

        // 실제 돌진이 닿는 거리. 위 붉은 원(chargeRange)보다 커야 한다.
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, chargeSpeed * chargeSeconds);
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        // 돌진이 닿지 않는 사거리는 영영 헛친다. 두 값이 인스펙터의 다른 항목에 떨어져 있어
        // 나란히 놓고 보지 않으면 모순이 안 보인다. (EnemyBoss에서 같은 문제를 겪었다.)
        float chargeDistance = chargeSpeed * chargeSeconds;
        if (chargeRange > chargeDistance)
        {
            Debug.LogWarning(
                $"[{name}] 돌진 사거리({chargeRange})가 실제 돌진 거리({chargeDistance:F2})보다 크다. " +
                "돌진이 플레이어 앞에서 멈춘다.", this);
        }
    }
}
