using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            cameraObject = new GameObject("Intro test camera", typeof(Camera), typeof(CameraShake));
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
            yield return new WaitForSeconds(1f);
            Assert.That(sequence.IsPlaying, Is.True, "첫 관람은 짧은 버전으로 끝나면 안 된다.");
            Assert.That(boss.IsInIntro, Is.True, "Start가 뒤늦게 실행돼도 AI가 잠겨 있어야 한다.");
            yield return new WaitForSeconds((float)sequence.TotalSeconds);
            Assert.That(completed, Is.EqualTo(1));
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
            yield return new WaitForSeconds(2f);
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
