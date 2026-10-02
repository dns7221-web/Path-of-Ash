using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// 추가 생성(2026-10-02) — 실제 생성된 프리팹과 Timeline으로 잠금·종료·확인창을 검사한다.
/// 신호를 건너뛰어도 전투 준비가 끝나야 하므로 단순 시간 계산 대신 생명주기를 검증한다.
/// </summary>
public class BossIntroSequenceTests
{
    private const string BossPath = "Assets/Project/Prefabs/Enemies/BossAshKing.prefab";
    private const string PlayerPath = "Assets/Project/Prefabs/Player/Player.prefab";

    // 추가 생성(2026-10-02) — 게임 시간 대기가 끝나지 않을 때 테스트가 영원히 돌지 않게 하는 실제 시간 상한(초).
    private const float RealTimeLimit = 60f;

    /// <summary>
    /// 추가 생성(2026-10-02) — 게임 시간(Time.time) 기준으로 아직 기다려야 하는가.
    ///
    /// WaitForSeconds를 쓰지 않는 이유: 이 테스트는 EditMode 테스트 안에서 EnterPlayMode로 들어간다.
    /// 이때 테스트의 대기와 게임 시계가 같이 흐르지 않았다. 실측으로 5.5초를 기다렸는데 Timeline(GameTime)은
    /// 0.56초밖에 안 흘러서, 연출은 멀쩡히 재생 중인데 "완료 신호가 안 왔다"고 실패했다.
    /// 그래서 Timeline과 같은 시계(Time.time)로 마감을 재고, 한 프레임씩(yield return null) 넘기며 확인한다.
    /// 실제 시간 상한은 게임 시간이 멈춰 버린 경우의 안전장치다.
    /// </summary>
    private static bool KeepWaiting(float gameDeadline, float realDeadline) =>
        Time.time < gameDeadline && Time.realtimeSinceStartup < realDeadline;

    /// <summary>추가 생성(2026-10-02) — 전체 재생·반복 단축·확인 취소·확정 스킵·중단을 실제 재생 중 검사한다.</summary>
    [UnityTest]
    public IEnumerator Intro_Completes_SkipsAndAborts_WithoutLeavingLocks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        bool hadPreference = PlayerPrefs.HasKey(BossIntroSequence.SeenPreferenceKey);
        int previousPreference = PlayerPrefs.GetInt(BossIntroSequence.SeenPreferenceKey, 0);
        GameObject cameraObject = null;
        GameObject playerObject = null;
        GameObject bossObject = null;
        GameObject hudObject = null;
        try
        {
            PlayerPrefs.DeleteKey(BossIntroSequence.SeenPreferenceKey);
            // 수정(2026-10-02) — AudioListener를 함께 붙인다. 없으면 연출 효과음마다
            // "There are no audio listeners" 경고가 쌓여 실제 실패 원인을 찾기 어려워진다.
            cameraObject = new GameObject("Intro test camera", typeof(Camera), typeof(AudioListener), typeof(CameraShake));
            cameraObject.tag = "MainCamera";
            playerObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));
            playerObject.transform.position = new Vector3(0f, -7f, 0f);
            var player = playerObject.GetComponent<PlayerController>();
            var playerHealth = playerObject.GetComponent<Health>();
            hudObject = new GameObject("Intro test HUD", typeof(BossHealthBar));
            var bar = hudObject.GetComponent<BossHealthBar>();
            bossObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BossPath));
            var boss = bossObject.GetComponent<EnemyBoss>();
            var health = bossObject.GetComponent<Health>();
            var sequence = bossObject.GetComponentInChildren<BossIntroSequence>();
            Assert.That(sequence, Is.Not.Null, "빌더가 보스 프리팹에 연출을 연결해야 한다.");
            int completed = 0;
            sequence.Finished += () => completed++;
            sequence.Begin(boss, player, bar);
            Assert.That(boss.IsInIntro && player.IsScripted, Is.True);
            Assert.That(health.TakeDamage(1, null), Is.False, "석상은 피해를 받지 않는다.");
            Assert.That(playerHealth.TakeDamage(1, null), Is.False, "첫 프레임부터 플레이어도 무적이다.");
            // 수정(2026-10-02) — WaitForSeconds 대신 게임 시간으로 1초를 잰다(이유는 KeepWaiting 주석).
            float gameDeadline = Time.time + 1f;
            float realDeadline = Time.realtimeSinceStartup + RealTimeLimit;
            while (KeepWaiting(gameDeadline, realDeadline)) yield return null;
            Assert.That(sequence.IsPlaying, Is.True, "첫 관람은 짧은 버전으로 끝나면 안 된다.");
            Assert.That(boss.IsInIntro, Is.True, "Start가 뒤늦게 실행돼도 AI가 잠겨 있어야 한다.");

            // 수정(2026-10-02) — 남은 연출 길이 + 여유 0.5초를 게임 시간으로 기다리되, 완료 신호가 오면 바로 넘어간다.
            gameDeadline = Time.time + (float)sequence.TotalSeconds + 0.5f;
            realDeadline = Time.realtimeSinceStartup + RealTimeLimit;
            while (completed == 0 && KeepWaiting(gameDeadline, realDeadline)) yield return null;
            // 수정(2026-10-02) — 실패하면 Director가 멈췄는지·몇 초에 있는지를 메시지에 남긴다.
            // "Expected 1 But was 0"만으로는 이벤트 누락인지 재생이 안 흐른 것인지 구분할 수 없었다.
            var introDirector = sequence.GetComponent<PlayableDirector>();
            Assert.That(completed, Is.EqualTo(1),
                $"연출이 끝나면 완료 신호가 한 번 와야 한다. Director state={introDirector.state}, " +
                $"time={introDirector.time:0.00}/{sequence.TotalSeconds:0.00}");
            Assert.That(player.IsScripted || boss.IsInIntro, Is.False);
            Assert.That(bar.IsBoundTo(health), Is.True);
            Assert.That(PlayerPrefs.GetInt(BossIntroSequence.SeenPreferenceKey), Is.EqualTo(1));
            sequence.Skip();
            Assert.That(completed, Is.EqualTo(1), "완료 뒤 스킵은 이벤트를 중복 발생시키지 않는다.");
            Object.Destroy(bossObject);
            yield return null;

            bossObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BossPath));
            boss = bossObject.GetComponent<EnemyBoss>();
            sequence = bossObject.GetComponentInChildren<BossIntroSequence>();
            sequence.Begin(boss, player, bar);
            // 수정(2026-10-02) — 단축 길이(1.5초)보다 넉넉한 2초를 게임 시간으로 기다린다. 끝나면 바로 넘어간다.
            gameDeadline = Time.time + 2f;
            realDeadline = Time.realtimeSinceStartup + RealTimeLimit;
            while (sequence.IsPlaying && KeepWaiting(gameDeadline, realDeadline)) yield return null;
            Assert.That(sequence.IsPlaying, Is.False, "관람 기록이 있으면 Timeline 마커의 단축 길이를 따른다.");
            Object.Destroy(bossObject);
            yield return null;

            // 첫 관람에서도 사용자의 명시적 ESC 확인을 허용하고 건너뛰어도 HUD 연결을 보장한다.
            PlayerPrefs.DeleteKey(BossIntroSequence.SeenPreferenceKey);
            bossObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BossPath));
            boss = bossObject.GetComponent<EnemyBoss>();
            health = bossObject.GetComponent<Health>();
            sequence = bossObject.GetComponentInChildren<BossIntroSequence>();
            completed = 0;
            sequence.Finished += () => completed++;
            sequence.Begin(boss, player, bar);
            sequence.RequestSkipConfirmation();
            Assert.That(BossIntroSkipDialog.IsOpen && PauseGate.IsPaused, Is.True);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(sequence.IsPlaying && player.IsScripted, Is.True);
            Object.FindFirstObjectByType<BossIntroSkipDialog>().Close();
            yield return null;
            Assert.That(PauseGate.IsPaused, Is.False);
            Assert.That(sequence.IsPlaying, Is.True, "취소는 연출을 이어간다.");
            sequence.RequestSkipConfirmation();
            var dialog = Object.FindFirstObjectByType<BossIntroSkipDialog>();
            Button confirm = null;
            foreach (Button button in dialog.GetComponentsInChildren<Button>())
                if (button.name == "Skip") confirm = button;
            Assert.That(confirm, Is.Not.Null, "스킵 버튼이 실제로 있어야 한다.");
            confirm.onClick.Invoke();
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(PauseGate.IsPaused || player.IsScripted || boss.IsInIntro, Is.False);
            Assert.That(bar.IsBoundTo(health), Is.True, "이름 카드 신호 전에 스킵해도 체력바를 연결한다.");
            Object.Destroy(bossObject);
            yield return null;

            PlayerPrefs.DeleteKey(BossIntroSequence.SeenPreferenceKey);
            bossObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(BossPath));
            boss = bossObject.GetComponent<EnemyBoss>();
            sequence = bossObject.GetComponentInChildren<BossIntroSequence>();
            completed = 0;
            sequence.Finished += () => completed++;
            sequence.Begin(boss, player, bar);
            sequence.RequestSkipConfirmation();
            bossObject.SetActive(false);
            Assert.That(completed, Is.Zero, "퇴장은 전투 시작이 아니다.");
            Assert.That(PauseGate.IsPaused || player.IsScripted, Is.False);
            Assert.That(PlayerPrefs.GetInt(BossIntroSequence.SeenPreferenceKey, 0), Is.Zero);
        }
        finally
        {
            if (bossObject != null) Object.Destroy(bossObject);
            if (playerObject != null) Object.Destroy(playerObject);
            if (hudObject != null) Object.Destroy(hudObject);
            if (cameraObject != null) Object.Destroy(cameraObject);
            PauseGate.CloseAll();
            if (hadPreference) PlayerPrefs.SetInt(BossIntroSequence.SeenPreferenceKey, previousPreference);
            else PlayerPrefs.DeleteKey(BossIntroSequence.SeenPreferenceKey);
            PlayerPrefs.Save();
        }
        yield return new ExitPlayMode();
    }
}
