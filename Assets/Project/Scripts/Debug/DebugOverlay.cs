#if UNITY_EDITOR
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성 — `F1`로 여는 화면 위 조사용 패널. 판 상태를 한곳에 보여주고 조작 버튼을 준다.
///
/// 왜 필요한가: 조사용 도구가 <see cref="BossKeyDebugGrant"/>(F3),
/// <see cref="PlayerInvincibleDebugToggle"/>(F6), <see cref="TimeScaleDebugControl"/>(F7~F9),
/// <see cref="AshGaugeDebugFill"/>(F11), 보스 방 점프(B), 사망(K)으로 흩어져 있다.
/// <b>어떤 키가 있는지 기억하는 것 자체가 비용</b>이고, 무엇보다 지금 상태(열쇠 몇 개인지,
/// 보스가 전환까지 얼마 남았는지, 무적이 켜져 있는지)를 볼 방법이 콘솔 로그밖에 없다.
///
/// 로그는 <b>이미 지나간 일</b>만 보여준다. 조사할 때 정작 필요한 것은 "지금 어떤 상태인가"다.
///
/// <b>파일 전체를 <c>#if UNITY_EDITOR</c>로 감쌌다.</b> 다른 조사용 도구와 같은 이유다 —
/// 인스펙터 체크박스로 끄는 방식은 켜둔 채로 빌드하는 실수가 언젠가 반드시 나온다.
///
/// <b>OnGUI로 그리는 이유.</b> 캔버스에 붙이면 이 패널이 씬 구조에 들어가고,
/// <c>AshGameHudBuilder</c>가 다시 돌 때 정리 대상에 걸린다(그 빌더는 HUD를 통째로 지우고
/// 다시 만든다). 게다가 화면이 두 벌 생기는 소프트락이 실제로 났던 프로젝트라 캔버스에
/// 물건을 하나 더 얹는 것 자체가 위험을 늘린다. OnGUI는 씬에 흔적을 하나도 안 남긴다.
///
/// <b>기본은 꺼져 있다.</b> 켜둔 채로 녹화하면 조사용 글자가 영상에 그대로 들어간다.
/// 이 프로젝트는 플레이 영상을 프레임 단위로 뜯어보며 조사하므로 그게 실제로 방해가 된다.
/// </summary>
[DisallowMultipleComponent]
public class DebugOverlay : MonoBehaviour
{
    private const Key ToggleKey = Key.F1;

    /// <summary>
    /// 씬을 다시 훑는 주기(초).
    ///
    /// 왜 매 프레임 안 하는가: <c>FindObjectsByType</c>는 씬 전체를 훑는다. 적이 여럿 있는
    /// 전투 중에 매 프레임 돌리면 <b>조사용 도구가 프레임을 깎는다.</b> 이 프로젝트는 히치를
    /// 조사하려고 영상까지 뜯어본 적이 있어서, 측정 도구가 측정 대상을 흔드는 것이 특히 나쁘다.
    ///
    /// 0.25초면 사람이 읽기에 충분히 즉각적이면서 비용은 1/15로 준다(60fps 기준).
    /// </summary>
    private const float RescanInterval = 0.25f;

    /// <summary>최악 프레임을 기억해 둘 시간(초). 이 시간이 지나면 잊고 다시 잰다.</summary>
    private const float HitchWindow = 3f;

    private bool visible;

    // ── 캐시한 참조 ────────────────────────────────────────────────────────
    //
    // null이 되면 다시 찾는다. 판을 다시 시작하면(씬 재로드) 전부 새로 생기기 때문이다.
    // PlayerInvincibleDebugToggle이 같은 문제를 다루고 있고, 이 도구는 판을 여러 번
    // 반복하며 쓰는 물건이라 그 상황이 오히려 기본값이다.
    private PlayerController player;
    private Health playerHealth;
    private PlayerStamina stamina;
    private AshGauge ashGauge;
    private RelicInventory inventory;
    private RoomSequenceController rooms;
    private RunManager run;
    private EnemyBoss boss;
    private Health bossHealth;

    private int enemyCount;
    private float rescanTimer;

    // ── 성능 측정 ──────────────────────────────────────────────────────────
    //
    // 전부 unscaledDeltaTime으로 잰다. 배속을 걸었을 때 scaled를 쓰면 "0.1배속이니까
    // 프레임이 10배 좋아졌다"는 거짓말이 나온다.
    private float smoothedDelta;
    private float worstDelta;
    private float worstAge;

    private GUIStyle boxStyle;
    private GUIStyle labelStyle;

    /// <summary>
    /// 문자열을 매 프레임 새로 잇지 않으려고 재사용한다.
    ///
    /// OnGUI는 프레임당 여러 번 불리고(Layout과 Repaint가 따로 온다) 이 패널은 줄이 열댓 개다.
    /// 그대로 이어 붙이면 프레임마다 문자열 수십 개가 쓰레기가 된다. 리뷰 기준에
    /// "매 프레임 할당(GC 부담)"이 있는 프로젝트에서 조사용 도구가 그걸 만들면 안 된다.
    /// </summary>
    private readonly StringBuilder text = new StringBuilder(512);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("[DebugOverlay]");
        go.AddComponent<DebugOverlay>();
        DontDestroyOnLoad(go);

        Debug.Log("[조사용] <b>F1</b>로 디버그 패널을 연다.");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[ToggleKey].wasPressedThisFrame) visible = !visible;

        TrackFrameTime();

        // 패널이 닫혀 있으면 씬을 훑지 않는다. 안 보이는 값을 위해 비용을 낼 이유가 없다.
        if (!visible) return;

        rescanTimer -= Time.unscaledDeltaTime;
        if (rescanTimer <= 0f)
        {
            Rescan();
            rescanTimer = RescanInterval;
        }
    }

    /// <summary>
    /// 프레임 시간을 재고, 최근에 튄 최악값을 따로 기억한다.
    ///
    /// 평균만 보면 히치가 안 보인다 — 60프레임 중 하나가 200ms여도 평균은 거의 안 움직인다.
    /// 이 프로젝트에서 실제로 문제가 된 것이 <b>설정·인벤토리를 여닫을 때 붙는 133~233ms
    /// 히치</b>였고, 그건 최악값으로만 잡힌다.
    /// </summary>
    private void TrackFrameTime()
    {
        float delta = Time.unscaledDeltaTime;

        // 지수 이동 평균. 표본 배열을 따로 안 들고도 최근 쪽에 가중치가 실린다.
        smoothedDelta = smoothedDelta <= 0f ? delta : Mathf.Lerp(smoothedDelta, delta, 0.1f);

        worstAge += delta;
        if (delta > worstDelta || worstAge > HitchWindow)
        {
            worstDelta = delta;
            worstAge = 0f;
        }
    }

    /// <summary>씬에서 필요한 것들을 찾는다. 이미 들고 있으면 건너뛴다.</summary>
    private void Rescan()
    {
        // 꺼져 있는 순간에도 찾아야 한다. 기본값은 비활성 오브젝트를 건너뛴다.
        if (player == null) player = FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player != null && playerHealth == null) playerHealth = player.GetComponent<Health>();
        if (player != null && stamina == null) stamina = player.GetComponent<PlayerStamina>();

        if (ashGauge == null) ashGauge = FindFirstObjectByType<AshGauge>(FindObjectsInactive.Include);
        if (inventory == null) inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);
        if (rooms == null) rooms = FindFirstObjectByType<RoomSequenceController>(FindObjectsInactive.Include);
        if (run == null) run = FindFirstObjectByType<RunManager>(FindObjectsInactive.Include);

        // 보스는 방에 들어가기 전에는 없다. 계속 다시 찾아야 하는 유일한 대상이다.
        if (boss == null)
        {
            boss = FindFirstObjectByType<EnemyBoss>(FindObjectsInactive.Include);
            bossHealth = boss != null ? boss.GetComponent<Health>() : null;
        }

        // 적 수는 값이 계속 바뀌므로 캐시가 아니라 갱신마다 다시 센다.
        var enemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        enemyCount = 0;
        foreach (var enemy in enemies)
        {
            if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;

            var health = enemy.GetComponent<Health>();
            if (health != null && health.IsDead) continue;

            enemyCount++;
        }
    }

    private void OnGUI()
    {
        if (!visible) return;

        EnsureStyles();

        GUILayout.BeginArea(new Rect(10f, 10f, 330f, Screen.height - 20f));
        GUILayout.BeginVertical(boxStyle);

        DrawState();
        GUILayout.Space(6f);
        DrawActions();
        GUILayout.Space(6f);
        DrawKeyHints();

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    /// <summary>지금 상태를 적는다.</summary>
    private void DrawState()
    {
        text.Clear();

        text.Append("── 판 ──\n");
        if (run != null)
        {
            int minutes = (int)(run.ElapsedSeconds / 60f);
            int seconds = (int)(run.ElapsedSeconds % 60f);
            text.Append($"경과 {minutes}:{seconds:00}   처치 {run.KillCount}   {run.State}\n");
        }

        if (rooms != null) text.Append($"들어간 방 {rooms.EnteredRoomCount}\n");

        if (inventory != null)
        {
            // 열쇠 개수 HUD는 아직 미구현이라(계획표의 이월 항목) 지금은 여기서만 보인다.
            text.Append($"보스 열쇠 {inventory.BossKeyCount}/{inventory.BossKeysRequired}");
            text.Append(inventory.HasAllBossKeys ? "  <b>충족</b>\n" : "\n");
        }

        text.Append("\n── 플레이어 ──\n");
        if (playerHealth != null)
        {
            text.Append($"체력 {playerHealth.Current}/{playerHealth.Max}");
            if (playerHealth.IsInvulnerableForDebug) text.Append("  <b>무적(F6)</b>");
            text.Append("\n");
        }

        if (stamina != null)
        {
            text.Append($"스태미나 {stamina.Current:0}/{stamina.Max:0}");
            text.Append(stamina.IsExhausted ? "  탈진\n" : "\n");
        }

        if (ashGauge != null)
        {
            text.Append($"재 게이지 {ashGauge.Normalized * 100f:0}%");
            text.Append(ashGauge.IsFull ? "  <b>R 가능</b>\n" : "\n");
        }

        text.Append("\n── 보스 ──\n");
        if (bossHealth == null)
        {
            text.Append("(보스 방 밖)\n");
        }
        else
        {
            text.Append($"체력 {bossHealth.Current}/{bossHealth.Max}\n");

            // 전환 임계값까지 몇 대 남았는지가 이 줄의 요점이다. 2페이즈 연출은 이 프로젝트에서
            // 손이 제일 많이 간 부분이고, 확인하려면 매번 여기까지 깎아야 한다.
            if (boss != null)
            {
                int threshold = Mathf.CeilToInt(bossHealth.Max * boss.Phase2HealthRatio);
                int remaining = bossHealth.Current - threshold;

                text.Append(remaining > 0
                    ? $"2페이즈까지 {remaining} (임계 {threshold})\n"
                    : "<b>2페이즈</b>\n");
            }
        }

        text.Append("\n── 씬 ──\n");

        // 보스는 EnemyBase를 상속하지 않아 이 수에 안 들어간다. 위 보스 칸이 따로 있으니
        // 오히려 그편이 읽기 좋다 — 이 숫자가 0이면 잡몹이 없다는 뜻이 정확히 맞는다.
        text.Append($"잡몹 {enemyCount}마리\n");

        // PauseGate 스택은 소프트락을 그 자리에서 잡아준다.
        //
        // 실제로 났던 버그다: 인벤토리 화면이 두 벌 생겨서 하나가 스택에서 안 빠지고,
        // timeScale이 0에 고정돼 게임이 멈췄다. 증상이 "게임이 멈췄다"로만 나타나서
        // 원인을 찾는 데 제일 오래 걸린 종류였다.
        // 화면을 다 닫았는데 이 숫자가 0이 아니면 그게 곧 그 버그다.
        text.Append($"PauseGate 스택 {PauseGate.OpenCount}");
        text.Append(PauseGate.OpenCount > 0 ? "  (정지 중)\n" : "\n");
        text.Append($"timeScale {Time.timeScale:0.##}\n");

        text.Append("\n── 성능 ──\n");
        float fps = smoothedDelta > 0f ? 1f / smoothedDelta : 0f;
        text.Append($"{fps:0} fps  ({smoothedDelta * 1000f:0.0}ms)\n");
        text.Append($"최근 최악 {worstDelta * 1000f:0}ms\n");

        GUILayout.Label(text.ToString(), labelStyle);
    }

    /// <summary>
    /// 조작 버튼.
    ///
    /// <b>여기 있는 것은 기존 키가 하지 못하는 일만이다.</b> 열쇠 지급(F3)이나 무적(F6)을
    /// 버튼으로 또 만들면 같은 동작이 두 곳에 생기고, 한쪽만 고치는 날이 온다.
    /// </summary>
    private void DrawActions()
    {
        GUILayout.Label("── 조작 ──", labelStyle);

        // 방을 클리어시키는 가장 빠른 길.
        //
        // 왜 필요한가: 계획표에 "승리 흐름 완주 검증 — 코드는 다 연결됐으나 끝까지 도달한 적이
        // 없다"가 남아 있다. 보스까지 17방이고 한 판이 8~12분이라 그 확인 한 번의 비용이 너무 크다.
        if (GUILayout.Button("적 전멸 (방 클리어)")) KillAllEnemies();

        // 2페이즈 전환 연출을 보려면 매번 보스를 27 깎아야 한다.
        //
        // 한 대 남기고 멈추는 이유: 여기서 그냥 임계값까지 깎아버리면 전환이 시작되는 순간을
        // 놓친다. 한 대 남겨두면 <b>직접 때려서 전환이 시작되는 그 프레임부터</b> 볼 수 있다.
        if (GUILayout.Button("보스를 2페이즈 직전으로")) BringBossToPhase2Edge();

        if (GUILayout.Button("플레이어 체력 회복")) RestorePlayer();
    }

    /// <summary>기존 조사용 키를 한곳에 적어둔다. 기억하지 않아도 되게 하는 것이 목적이다.</summary>
    private void DrawKeyHints()
    {
        GUILayout.Label(
            "── 키 ──\n" +
            "F1 이 패널        F3 보스 열쇠\n" +
            "F6 무적           F11 재 게이지\n" +
            "F7 배속   F8 멈춤   F9 한 프레임\n" +
            "B 보스 방         K 즉시 사망\n" +
            "1/2/3 문 상태(닫힘/열림/부서짐)",
            labelStyle);
    }

    /// <summary>
    /// 살아 있는 적을 전부 죽인다.
    ///
    /// <b>체력을 0으로 대입하지 않고 <see cref="Health.TakeDamage(int)"/>로 때린다.</b>
    /// <see cref="BossKeyDebugGrant"/>가 지급 경로를 따로 만들지 않고 <c>Acquire</c>를 그대로
    /// 쓴 것과 같은 판단이다 — 조사용 경로가 실제 경로와 갈라지면 방 클리어 판정, 처치 수 집계,
    /// 재 게이지 충전 같은 <b>죽음에 딸린 일들이 안 일어나서</b> "디버그로 지웠더니 문이 안
    /// 열린다"가 된다.
    ///
    /// <b>보스는 구조적으로 안 걸린다.</b> <see cref="EnemyBoss"/>는 <see cref="EnemyBase"/>를
    /// 상속하지 않고 <c>MonoBehaviour</c>를 직접 상속하기 때문에 이 검색에 아예 안 잡힌다.
    /// 그래서 따로 걸러내는 코드를 두지 않았다 — 뒀다면 <b>영영 참이 되지 않는 죽은 검사</b>가
    /// 하나 생기고, 나중에 보스가 EnemyBase로 옮겨오면 그때는 조용히 뜻이 달라진다.
    ///
    /// 결과적으로 원하는 동작과 일치한다. 보스를 이걸로 죽이면 2페이즈 전환을 건너뛰게 되는데,
    /// 그건 대개 보려던 것 자체다.
    /// </summary>
    private void KillAllEnemies()
    {
        var enemies = FindObjectsByType<EnemyBase>(FindObjectsSortMode.None);
        int killed = 0;

        int survived = 0;

        foreach (var enemy in enemies)
        {
            if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;

            var health = enemy.GetComponent<Health>();
            if (health == null || health.IsDead) continue;

            // 무적을 먼저 전부 걷어낸다.
            //
            // <b>여기를 빠뜨리면 이 버튼이 조용히 아무 일도 안 하는 경우가 생긴다.</b>
            // Health.IsInvulnerable은 셋을 본다 — 외부 무적, <b>피격 무적 타이머</b>,
            // 그리고 조사용 무적. 앞의 둘 때문에, 적을 한 대 때린 직후에 이 버튼을 누르면
            // 방금 때린 그 적만 살아남는다. 원인을 짐작하기 아주 어려운 종류의 실패다.
            //
            // 타이머는 공개된 설정 수단이 없고 RestoreFull만 그걸 0으로 되돌린다.
            // 체력을 가득 채웠다가 바로 최대치로 때리므로 결과는 같고, 죽음에 딸린 일들
            // (처치 수, 재 게이지, 방 클리어 판정)은 그대로 일어난다.
            health.IsInvulnerableForDebug = false;
            health.RestoreFull();
            health.TakeDamage(health.Max);

            if (health.IsDead) killed++;
            else survived++;
        }

        string message = $"[조사용] 잡몹 {killed}마리를 죽였다. " +
                         "(보스는 EnemyBase가 아니라서 여기 안 걸린다)";

        // 그래도 살아남은 것이 있으면 반드시 알린다. 숫자가 안 맞는데 조용하면
        // "눌렀는데 왜 문이 안 열리지"로 시간을 날린다.
        if (survived > 0) Debug.LogWarning($"{message}\n{survived}마리가 안 죽었다. Health를 확인해라.");
        else Debug.Log(message);
    }

    /// <summary>보스 체력을 2페이즈 전환 임계값 <b>바로 위</b>로 내린다.</summary>
    private void BringBossToPhase2Edge()
    {
        if (boss == null || bossHealth == null)
        {
            Debug.LogWarning("[조사용] 보스가 없다. B로 보스 방에 들어가서 눌러라.");
            return;
        }

        int threshold = Mathf.CeilToInt(bossHealth.Max * boss.Phase2HealthRatio);

        // 임계값보다 한 칸 위에 세운다. 이유는 버튼 쪽 주석에 적었다 —
        // 전환이 시작되는 순간을 직접 때려서 보게 하려는 것이다.
        int target = threshold + 1;

        // 이미 그 아래면 아무것도 안 한다. 아래에서 RestoreFull을 부르기 때문에, 이 검사가
        // 없으면 <b>2페이즈에 들어간 보스를 1페이즈 문턱으로 되돌려버린다.</b>
        if (bossHealth.Current <= target)
        {
            Debug.Log($"[조사용] 보스가 이미 {bossHealth.Current}로 임계({threshold}) 이하다. " +
                      "되돌리지 않는다.");
            return;
        }

        // 무적을 먼저 걷어낸다. 전환 연출 중에는 외부 무적이 켜져 있고, 방금 때렸다면
        // 피격 무적 타이머도 남아 있다. 둘 중 하나라도 걸려 있으면 TakeDamage가 조용히
        // 아무 일도 안 한다. 타이머를 0으로 되돌리는 공개 수단은 RestoreFull뿐이라
        // 가득 채웠다가 목표치까지 한 번에 깎는다.
        bossHealth.IsInvulnerableForDebug = false;
        bossHealth.RestoreFull();
        bossHealth.TakeDamage(bossHealth.Max - target);

        Debug.Log($"[조사용] 보스 체력 {bossHealth.Current}/{bossHealth.Max} " +
                  $"(2페이즈 임계 {threshold}). <b>한 대 더 때리면 전환이 시작된다.</b>\n" +
                  "F7로 0.1배속을 걸면 3.125초 연출이 31초가 된다.");
    }

    private void RestorePlayer()
    {
        if (playerHealth == null)
        {
            Debug.LogWarning("[조사용] 플레이어의 Health를 못 찾았다. 게임 씬에서 눌러라.");
            return;
        }

        playerHealth.RestoreFull();
        Debug.Log($"[조사용] 체력 회복 {playerHealth.Current}/{playerHealth.Max}");
    }

    /// <summary>
    /// 스타일을 한 번만 만든다.
    ///
    /// GUI.skin은 OnGUI 밖에서 읽으면 안 되므로 Awake가 아니라 첫 호출 때 만든다.
    /// 매번 만들면 안 되는 이유는 <see cref="text"/> 주석에 적은 것과 같다.
    /// </summary>
    private void EnsureStyles()
    {
        if (boxStyle != null) return;

        boxStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 10, 10),
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = true,
            wordWrap = false,
        };
        labelStyle.normal.textColor = Color.white;
    }
}
#endif
