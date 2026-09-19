#if UNITY_EDITOR
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 추가 생성(2026-09-19, 보스 궁극기 영상) — 영상 테스트 씬(Video) 전용 조작. 편집기에서만 존재한다.
///
/// 스페이스 = 처음부터 재생, Esc = 건너뛰기. 켜지면 잠깐 뒤 한 번 저절로 재생한다.
/// 재생하는 동안 timeScale을 0으로 만들어 실제 게임처럼 멈춘 상태에서 도는지 확인한다(끝나면 되돌린다).
/// 게임에서는 이 역할을 보스와 PauseGate가 맡는다 — 여기서 timeScale을 직접 만지는 것은 테스트 씬이라서다.
/// </summary>
public class CutsceneVideoTester : MonoBehaviour
{
    [Tooltip("재생할 컷인.")]
    [SerializeField] private CutsceneVideo cutscene;

    [Tooltip("씬이 켜지면 저절로 한 번 재생할지.")]
    [SerializeField] private bool playOnStart = true;

    [Tooltip("저절로 재생하기 전 기다리는 시간(실제 초). 준비(Prepare)가 끝날 틈을 준다.")]
    [SerializeField, Min(0f)] private float startDelay = 1f;

    [Tooltip("재생하는 동안 게임 시간을 멈춰 실제 궁극기처럼 확인할지.")]
    [SerializeField] private bool pauseGameWhilePlaying = true;

    private IEnumerator Start()
    {
        if (cutscene != null) cutscene.Finished += OnFinished;
        if (!playOnStart) yield break;

        yield return new WaitForSecondsRealtime(startDelay);
        PlayCutscene();
    }

    private void OnDestroy()
    {
        if (cutscene != null) cutscene.Finished -= OnFinished;
        Time.timeScale = 1f;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || cutscene == null) return;

        if (keyboard.spaceKey.wasPressedThisFrame && !cutscene.IsPlaying) PlayCutscene();
        if (keyboard.escapeKey.wasPressedThisFrame) cutscene.Skip();
    }

    private void PlayCutscene()
    {
        if (pauseGameWhilePlaying) Time.timeScale = 0f;
        cutscene.Play();
    }

    private void OnFinished()
    {
        Time.timeScale = 1f;
        Debug.Log("[영상 테스트] 컷인 끝 — 게임에서는 여기서 방 전체 폭발이 터진다. 스페이스로 다시 재생.");
    }
}
#endif
