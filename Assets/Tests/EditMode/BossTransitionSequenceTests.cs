using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// 추가 생성(2026-10-03) — 2페이즈 전환 연출(4.0초판)을 실제 생성된 보스 프리팹과 Timeline으로 검사한다.
///
/// 무엇을 지키려는 테스트인가:
/// <list type="bullet">
/// <item>연출 동안 플레이어가 잠기고 무적이며, 방이 어두워진다.</item>
/// <item>끝까지 보면 완료 신호가 한 번 오고 2페이즈가 된다.</item>
/// <item>스킵해도 건너뛴 순간(갑옷 붕괴·껍질 깨짐·2페이즈 등장)이 정확히 한 번씩 채워진다.</item>
/// <item>중간에 꺼지면(방 퇴장) 완료 신호 없이 잠금과 조명만 돌아온다.</item>
/// </list>
/// 시간 계산이 아니라 생명주기를 검사하는 이유는 1페이즈 등장 테스트(BossIntroSequenceTests)와 같다.
/// 신호를 건너뛰어도 전투가 이어져야 하는 것이 이 연출의 가장 중요한 약속이다.
/// </summary>
public class BossTransitionSequenceTests
{
    private const string BossPath = "Assets/Project/Prefabs/Enemies/BossAshKing.prefab";
    private const string PlayerPath = "Assets/Project/Prefabs/Player/Player.prefab";

    // 게임 시간 대기가 끝나지 않을 때 테스트가 영원히 돌지 않게 하는 실제 시간 상한(초).
    private const float RealTimeLimit = 60f;

    /// <summary>
    /// 게임 시간(Time.time) 기준으로 아직 기다려야 하는가.
    /// WaitForSeconds를 안 쓰는 이유는 BossIntroSequenceTests.KeepWaiting 주석과 같다
    /// (EnterPlayMode 안에서 테스트 대기와 게임 시계가 같이 흐르지 않았다).
    /// </summary>
    private static bool KeepWaiting(float gameDeadline, float realDeadline) =>
        Time.time < gameDeadline && Time.realtimeSinceStartup < realDeadline;

    /// <summary>
    /// 보스 하나를 만들고 전환 연출의 신호 횟수를 세는 묶음.
    /// 세 번(끝까지 보기·스킵·중단) 같은 준비를 하므로 한 곳에 모았다.
    /// </summary>
    private sealed class Run
    {
        public GameObject BossObject;
        public EnemyBoss Boss;
        public BossTransitionSequence Sequence;
        public int ArmorBroken;
        public int Revealed;
        public int BossReturns;
        public int Finished;
    }

    /// <summary>
    /// 보스를 꺼내 AI를 멈춰 두고(등장 잠금 재사용) 연출에 플레이어와 체력바를 넘긴 뒤 신호 수를 센다.
    /// AI를 멈추는 이유: 이 테스트는 EnterPhase2를 거치지 않고 연출만 직접 튼다. 그동안 보스가 걸어와
    /// 플레이어를 때리면 잠금·무적 검사가 연출 때문인지 전투 때문인지 가릴 수 없다.
    /// </summary>
    private static Run Spawn(PlayerController player, BossHealthBar bar)
    {
        var run = new Run();
        run.BossObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BossPath));
        run.Boss = run.BossObject.GetComponent<EnemyBoss>();
        run.Boss.BeginIntro();
        bar.Bind(run.BossObject.GetComponent<Health>(), run.Boss.Phase2HealthRatio);

        run.Sequence = run.BossObject.GetComponentInChildren<BossTransitionSequence>();
        Assert.That(run.Sequence, Is.Not.Null, "빌더가 보스 프리팹에 전환 연출을 연결해야 한다.");

        run.Sequence.ArmorBroken += () => run.ArmorBroken++;
        run.Sequence.Revealed += () => run.Revealed++;
        run.Sequence.BossReturns += () => run.BossReturns++;
        run.Sequence.Finished += () => run.Finished++;
        run.Sequence.Prepare(player, bar);
        return run;
    }

    /// <summary>추가 생성(2026-10-03) — 끝까지 보기·확인 창 취소·스킵·중단을 실제 재생 중에 검사한다.</summary>
    [UnityTest]
    public IEnumerator Transition_LocksPlayer_SkipsToPhase2_AndAbortsCleanly()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();

        GameObject cameraObject = null;
        GameObject playerObject = null;
        GameObject hudObject = null;
        GameObject lightObject = null;
        Run run = null;
        try
        {
            // AudioListener를 붙이는 이유: 없으면 연출 효과음마다 경고가 쌓여 실제 실패 원인을 가린다.
            cameraObject = new GameObject("Transition test camera", typeof(Camera), typeof(AudioListener), typeof(CameraShake));
            cameraObject.tag = "MainCamera";

            // 방의 전체 조명 대신 쓸 Global Light2D. 연출이 어둡게 했다가 원래대로 돌려놓는지 본다.
            lightObject = new GameObject("Transition test room light", typeof(Light2D));
            var roomLight = lightObject.GetComponent<Light2D>();
            roomLight.lightType = Light2D.LightType.Global;
            roomLight.intensity = 1f;

            playerObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            playerObject.transform.position = new Vector3(0f, -7f, 0f);
            var player = playerObject.GetComponent<PlayerController>();
            var playerHealth = playerObject.GetComponent<Health>();
            hudObject = new GameObject("Transition test HUD", typeof(BossHealthBar));
            var bar = hudObject.GetComponent<BossHealthBar>();

            // ── 1) 끝까지 보기 ─────────────────────────────────────────
            run = Spawn(player, bar);
            Assert.That(run.Sequence.transform, Is.Not.EqualTo(run.BossObject.transform),
                "연출은 TransitionCinematic 자식에 있어야 한다(보스 본체 Animator를 덮지 않게). " +
                "Tools → 재의 길 → 프리팹 → 보스 전환 다시 만들기 를 실행해라.");
            Assert.That(run.Sequence.TotalSeconds, Is.EqualTo(4f).Within(0.01f),
                "새 계획표(4.0초) 타임라인이어야 한다. 보스 전환 다시 만들기 를 실행해라.");

            run.Sequence.Play();
            Assert.That(run.Sequence.IsPlaying && player.IsScripted, Is.True, "연출 동안 플레이어를 잠근다.");
            Assert.That(playerHealth.TakeDamage(1, null), Is.False, "첫 프레임부터 플레이어도 무적이다.");
            Assert.That(BossTransitionSequence.Active, Is.EqualTo(run.Sequence), "설정 창이 ESC를 양보하려면 Active가 잡혀 있어야 한다.");

            // 방이 다 어두워진 뒤(0.5초) 갑옷 붕괴(1.0초) 전까지.
            float gameDeadline = Time.time + 0.8f;
            float realDeadline = Time.realtimeSinceStartup + RealTimeLimit;
            while (KeepWaiting(gameDeadline, realDeadline)) yield return null;
            Assert.That(roomLight.intensity, Is.LessThan(0.6f), "연출 중에는 방이 어두워진다.");
            Assert.That(run.ArmorBroken, Is.Zero, "갑옷 붕괴는 1.0초다(0.5초가 아니다).");

            gameDeadline = Time.time + run.Sequence.TotalSeconds + 0.5f;
            realDeadline = Time.realtimeSinceStartup + RealTimeLimit;
            while (run.Finished == 0 && KeepWaiting(gameDeadline, realDeadline)) yield return null;

            // 실패하면 Director가 어디서 멈췄는지 남긴다. "Expected 1 But was 0"만으로는 원인을 가릴 수 없다.
            var director = run.Sequence.GetComponent<PlayableDirector>();
            Assert.That(run.Finished, Is.EqualTo(1),
                $"끝까지 보면 완료 신호가 한 번 와야 한다. Director state={director.state}, " +
                $"time={director.time:0.00}/{run.Sequence.TotalSeconds:0.00}");
            Assert.That(run.ArmorBroken == 1 && run.Revealed == 1 && run.BossReturns == 1, Is.True,
                $"각 순간은 정확히 한 번씩 온다. 붕괴={run.ArmorBroken}, 깨짐={run.Revealed}, 등장={run.BossReturns}");
            Assert.That(player.IsScripted || run.Sequence.IsPlaying, Is.False, "끝나면 조작이 풀린다.");
            Assert.That(roomLight.intensity, Is.EqualTo(1f).Within(0.001f), "끝나면 방 밝기가 원래대로 돌아온다.");
            Assert.That(BossTransitionSequence.Active, Is.Null);

            run.Sequence.Skip();
            Assert.That(run.Finished, Is.EqualTo(1), "끝난 뒤 스킵은 완료 신호를 또 보내지 않는다.");
            Object.Destroy(run.BossObject);
            yield return null;

            // ── 2) 확인 창 취소 → 스킵 ─────────────────────────────────
            run = Spawn(player, bar);
            run.Sequence.Play();
            run.Sequence.RequestSkipConfirmation();
            Assert.That(BossIntroSkipDialog.IsOpen && PauseGate.IsPaused, Is.True, "ESC는 바로 건너뛰지 않고 확인 창을 연다.");
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(run.Sequence.IsPlaying && player.IsScripted, Is.True, "확인 창이 떠 있는 동안 연출은 멈춰 있다.");

            Object.FindFirstObjectByType<BossIntroSkipDialog>().Close();
            yield return null;
            Assert.That(PauseGate.IsPaused, Is.False);
            Assert.That(run.Sequence.IsPlaying, Is.True, "취소하면 연출이 이어진다.");

            run.Sequence.RequestSkipConfirmation();
            var dialog = Object.FindFirstObjectByType<BossIntroSkipDialog>();
            Button confirm = null;
            foreach (Button button in dialog.GetComponentsInChildren<Button>())
                if (button.name == "Skip") confirm = button;
            Assert.That(confirm, Is.Not.Null, "스킵 버튼이 실제로 있어야 한다.");
            confirm.onClick.Invoke();

            Assert.That(run.Finished, Is.EqualTo(1), "스킵도 완료 신호를 한 번 보낸다.");
            Assert.That(run.ArmorBroken == 1 && run.Revealed == 1 && run.BossReturns == 1, Is.True,
                "스킵으로 건너뛴 순간도 정확히 한 번씩 채워진다. 안 그러면 1페이즈 모습으로 싸움이 시작된다.");
            Assert.That(PauseGate.IsPaused || player.IsScripted, Is.False, "스킵 뒤에는 멈춤과 잠금이 남지 않는다.");
            Assert.That(roomLight.intensity, Is.EqualTo(1f).Within(0.001f), "스킵해도 방 밝기가 돌아온다.");

            // 스킵 때 갑옷 붕괴(흐려지며 끄기)와 2페이즈 등장(떠오르기)이 같은 프레임에 온다.
            // 페이드가 겹치면 0.3초 뒤 붕괴 쪽이 렌더러를 꺼 버렸다(EnemyBoss.spriteFade 주석). 그 뒤에도 보여야 한다.
            gameDeadline = Time.time + 0.5f;
            realDeadline = Time.realtimeSinceStartup + RealTimeLimit;
            while (KeepWaiting(gameDeadline, realDeadline)) yield return null;
            Assert.That(run.BossObject.GetComponent<SpriteRenderer>().enabled, Is.True, "스킵 뒤 2페이즈 보스가 보여야 한다.");
            Object.Destroy(run.BossObject);
            yield return null;

            // ── 3) 중간에 꺼짐(방 퇴장) ────────────────────────────────
            run = Spawn(player, bar);
            run.Sequence.Play();
            gameDeadline = Time.time + 0.6f;
            realDeadline = Time.realtimeSinceStartup + RealTimeLimit;
            while (KeepWaiting(gameDeadline, realDeadline)) yield return null;
            run.Sequence.RequestSkipConfirmation();
            run.BossObject.SetActive(false);

            Assert.That(run.Finished, Is.Zero, "퇴장은 전투 재개가 아니다.");
            Assert.That(PauseGate.IsPaused || player.IsScripted, Is.False, "꺼져도 확인 창과 잠금이 남지 않는다.");
            Assert.That(roomLight.intensity, Is.EqualTo(1f).Within(0.001f), "꺼져도 방 밝기가 돌아온다.");
            Assert.That(BossTransitionSequence.Active, Is.Null);
        }
        finally
        {
            if (run != null && run.BossObject != null) Object.Destroy(run.BossObject);
            if (playerObject != null) Object.Destroy(playerObject);
            if (hudObject != null) Object.Destroy(hudObject);
            if (lightObject != null) Object.Destroy(lightObject);
            if (cameraObject != null) Object.Destroy(cameraObject);
            PauseGate.CloseAll();
        }
    }
}
