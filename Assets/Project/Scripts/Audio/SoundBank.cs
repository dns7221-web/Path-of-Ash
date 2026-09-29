using System;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-29, 소리) — 어떤 이름표(<see cref="SfxId"/>, <see cref="MusicId"/>)에 어떤 소리를 낼지 적어 둔 목록.
///
/// <b>소리를 코드에 박지 않고 목록으로 둔 이유:</b> 소리는 들어 보면서 바꾸는 것이다. 클립을 갈아 끼우거나
/// 볼륨·흔들림을 바꿀 때 코드를 열지 않고 인스펙터에서 끝나야 한다. 게임 코드는 "무엇이 일어났나"만 알린다.
///
/// <b>Resources에 두는 이유:</b> <see cref="SoundPlayer"/>가 씬 연결 없이 스스로 찾아 쓰게 하려는 것이다.
/// 씬을 통째로 다시 만드는 빌더가 연결을 지워도 소리가 끊기지 않는다.
/// 에셋은 <c>Tools → 재의 길 → 씬·세팅 → 소리 구성</c>이 만든다.
/// </summary>
[CreateAssetMenu(fileName = "SoundBank", menuName = "재의 길/소리 목록")]
public class SoundBank : ScriptableObject
{
    /// <summary>Resources.Load 경로. 에셋 자리는 Assets/Project/Resources/Audio/SoundBank.asset이다.</summary>
    public const string ResourcePath = "Audio/SoundBank";

    /// <summary>효과음 하나의 설정.</summary>
    [Serializable]
    public class SfxEntry
    {
        [Tooltip("이 설정이 어떤 효과음인가.")]
        public SfxId id;

        [Tooltip("재생할 클립. 여러 개면 매번 무작위로 고른다 — 같은 소리만 반복되는 느낌을 줄인다. 비어 있으면 소리가 안 난다.")]
        public AudioClip[] clips = Array.Empty<AudioClip>();

        [Tooltip("볼륨(0~1). 설정 창의 효과음 볼륨이 여기에 곱해진다.")]
        [Range(0f, 1f)] public float volume = 1f;

        [Tooltip("재생할 때마다 피치를 이 비율만큼 무작위로 흔든다(0.05 = ±5%). 같은 클립도 조금씩 다르게 들린다.")]
        [Range(0f, 0.3f)] public float pitchJitter = 0.05f;

        [Tooltip("같은 효과음을 다시 낼 수 있는 최소 간격(실제 초). 광역기가 적 다섯을 한 번에 때려도 소리가 다섯 겹으로 커지지 않게 한다.")]
        [Min(0f)] public float minInterval = 0.05f;

        [Tooltip("같은 효과음이 동시에 날 수 있는 최대 개수.")]
        [Min(1)] public int maxVoices = 3;
    }

    /// <summary>배경음악 한 곡의 설정.</summary>
    [Serializable]
    public class MusicEntry
    {
        [Tooltip("이 설정이 어떤 곡인가.")]
        public MusicId id;

        [Tooltip("반복 재생할 곡.")]
        public AudioClip clip;

        [Tooltip("볼륨(0~1). 설정 창의 배경음 볼륨이 여기에 곱해진다. 음악 파일은 효과음보다 훨씬 크게 녹음돼 있어서 낮게 둔다.")]
        [Range(0f, 1f)] public float volume = 0.35f;
    }

    /// <summary>씬이 열릴 때 틀 곡.</summary>
    [Serializable]
    public class SceneMusic
    {
        [Tooltip("씬 이름(Build Settings에 올라간 이름).")]
        public string sceneName;

        [Tooltip("이 씬이 열리면 틀 곡. None이면 음악을 서서히 끈다.")]
        public MusicId music;
    }

    [SerializeField] private SfxEntry[] sfx = Array.Empty<SfxEntry>();
    [SerializeField] private MusicEntry[] music = Array.Empty<MusicEntry>();
    [SerializeField] private SceneMusic[] sceneMusic = Array.Empty<SceneMusic>();

    /// <summary>효과음 설정을 찾는다. 없으면 null.</summary>
    public SfxEntry Find(SfxId id)
    {
        foreach (SfxEntry entry in sfx)
            if (entry != null && entry.id == id) return entry;

        return null;
    }

    /// <summary>곡 설정을 찾는다. 없으면 null.</summary>
    public MusicEntry Find(MusicId id)
    {
        foreach (MusicEntry entry in music)
            if (entry != null && entry.id == id) return entry;

        return null;
    }

    /// <summary>
    /// 씬에 정해 둔 곡. 목록에 없는 씬이면 None(음악을 끈다)이다.
    /// 모르는 씬(시험용 씬 등)에서 지난 곡이 계속 흐르는 것보다 조용한 편이 덜 헷갈린다.
    /// </summary>
    public MusicId MusicForScene(string sceneName)
    {
        foreach (SceneMusic entry in sceneMusic)
            if (entry != null && entry.sceneName == sceneName) return entry.music;

        return MusicId.None;
    }
}
