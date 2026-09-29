using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// 추가 생성(2026-09-29, 소리) — 효과음과 배경음악을 내는 곳. 게임 코드는 <c>SoundPlayer.Play(SfxId.ChestOpen)</c> 한 줄만 부른다.
///
/// <b>씬에 두지 않고 게임이 켜질 때 스스로 만든다</b>(RuntimeInitializeOnLoadMethod + DontDestroyOnLoad).
/// 씬에 두면 씬을 통째로 다시 만드는 빌더가 지울 수 있고(인벤토리 화면이 그렇게 사라졌다) 씬마다 하나씩 챙겨야 한다.
/// 스스로 만들면 어느 씬에서 시작하든 있고, 씬이 바뀌어도 배경음악이 끊기지 않고 이어진다.
///
/// <b>볼륨은 오디오 믹서 대신 AudioSource 볼륨에 곱한다.</b> 믹서 에셋은 코드로 안정적으로 만들 수 없어서(편집기 내부 기능)
/// 손으로 만들어야 하는데, 효과음·배경음 두 묶음만 조절하는 데는 곱셈으로 충분하다. 들리는 결과도 같다 —
/// <see cref="GameSettings"/>가 믹서에 넣던 dB(20·log10)는 진폭에 슬라이더 값을 곱한 것과 같다.
/// 마스터 볼륨은 GameSettings가 AudioListener로 이미 적용한다.
///
/// 수정(2026-09-29, 믹서) — 믹서 에셋(Resources/GameAudioMixer)을 손으로 만들어 두면 그쪽을 쓴다.
/// AudioSource를 믹서의 BGM·SFX 그룹에 물리고(<see cref="RouteToMixer"/>) 위 곱셈은 끈다(<see cref="BgmScale"/>·<see cref="SfxScale"/>).
/// 둘 다 하면 슬라이더 절반에서 소리가 1/4로 줄기 때문이다. 믹서가 없거나 그룹 이름이 다르면 예전 곱셈 경로로 돈다.
///
/// <b>효과음 겹침을 막는다.</b> 같은 효과음은 최소 간격 안에 다시 내지 않고, 동시에 날 수 있는 개수도 제한한다.
/// 광역기가 적 다섯을 한 프레임에 때리면 같은 소리가 다섯 겹으로 커져 귀가 아프고 다른 소리를 덮는다.
///
/// 시간은 전부 실제 시간(unscaled)이다. 인벤토리·궁극기 영상 동안 게임이 멈춰도(timeScale 0) 소리와 페이드는 돈다.
/// </summary>
[DisallowMultipleComponent]
public class SoundPlayer : MonoBehaviour
{
    // 효과음용 AudioSource 수 = 동시에 날 수 있는 효과음의 총 개수.
    private const int VoiceCount = 12;

    private static SoundPlayer instance;

    private SoundBank bank;

    private AudioSource[] voices;
    private SfxId[] voiceIds;        // 각 자리가 지금 내는 효과음. 같은 소리의 동시 개수를 셀 때 쓴다.
    private int nextVoice;           // 빈 자리가 없을 때 돌아가며 뺏을 다음 자리
    private readonly Dictionary<SfxId, float> lastPlayedAt = new Dictionary<SfxId, float>();

    private AudioSource[] musicSources;   // 둘을 번갈아 쓰며 교차 페이드한다
    private int activeMusic;
    private MusicId currentMusic = MusicId.None;
    private float currentMusicVolume;     // 목록에 적힌 곡 볼륨(설정 볼륨을 곱하기 전)
    private Coroutine fadeRoutine;

    // 추가 생성(2026-09-29, 믹서) — 믹서 그룹에 물렸는가. 물렸으면 볼륨 조절은 믹서가 맡는다.
    private bool mixerRouted;

    // 추가 생성(2026-09-29, 믹서) — 믹서에서 찾을 그룹 이름. 믹서 창에서 그룹 이름을 이것과 똑같이 짓는다.
    private const string BgmGroupName = "BGM";
    private const string SfxGroupName = "SFX";

    /// <summary>
    /// 추가 생성(2026-09-29, 믹서) — AudioSource 볼륨에 곱할 설정값. 믹서에 물렸으면 믹서가 dB로 줄이므로 1이다.
    /// </summary>
    private float BgmScale => mixerRouted ? 1f : GameSettings.BgmVolume;

    /// <summary>추가 생성(2026-09-29, 믹서) — 효과음 쪽. 이유는 <see cref="BgmScale"/>과 같다.</summary>
    private float SfxScale => mixerRouted ? 1f : GameSettings.SfxVolume;

    // ── 부르는 쪽 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 효과음을 낸다. 목록에 없거나 클립이 비어 있으면 조용히 넘어간다 — 소리 때문에 게임 진행이 멈추면 안 된다.
    /// 효과음 팩이 없는 컴퓨터(저장소를 새로 받은 경우)에서도 게임은 소리 없이 그대로 돈다.
    /// </summary>
    public static void Play(SfxId id)
    {
        if (id == SfxId.None) return;

        SoundPlayer player = Instance;
        if (player != null) player.PlaySfx(id);
    }

    /// <summary>배경음악을 바꾼다. 이미 그 곡이면 아무것도 하지 않는다. None이면 서서히 끈다.</summary>
    public static void PlayMusic(MusicId id, float fadeSeconds = 1.5f)
    {
        SoundPlayer player = Instance;
        if (player != null) player.ChangeMusic(id, fadeSeconds);
    }

    /// <summary>배경음악을 서서히 끈다.</summary>
    public static void StopMusic(float fadeSeconds = 1.5f) => PlayMusic(MusicId.None, fadeSeconds);

    // ── 만들기 ────────────────────────────────────────────────────────────

    /// <summary>플레이 중에만 있다. 편집 모드(테스트 포함)에서는 null이라 소리 호출이 아무 일도 하지 않는다.</summary>
    private static SoundPlayer Instance
    {
        get
        {
            if (instance == null && Application.isPlaying) Create();
            return instance;
        }
    }

    /// <summary>게임이 켜질 때(첫 씬을 불러오기 전) 만든다. 그래야 첫 씬의 배경음악도 놓치지 않는다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (instance == null) Create();
    }

    /// <summary>도메인 리로드를 끈 플레이 설정에서도 지난 플레이의 참조가 남지 않게 비운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;

    private static void Create()
    {
        var root = new GameObject("SoundPlayer");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<SoundPlayer>();
    }

    private void Awake()
    {
        // 누가 씬에 하나 더 두었으면 먼저 생긴 쪽만 남긴다. 둘이면 같은 음악이 두 겹으로 난다.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        bank = Resources.Load<SoundBank>(SoundBank.ResourcePath);
        if (bank == null)
        {
            Debug.LogWarning("[소리] 소리 목록(Resources/Audio/SoundBank)이 없다. " +
                             "Tools → 재의 길 → 씬·세팅 → 소리 구성 을 실행해라. 소리 없이 계속한다.", this);
        }

        voices = new AudioSource[VoiceCount];
        voiceIds = new SfxId[VoiceCount];
        for (int i = 0; i < VoiceCount; i++) voices[i] = CreateSource(false);

        musicSources = new[] { CreateSource(true), CreateSource(true) };

        // 추가 생성(2026-09-29, 믹서) — 믹서가 있으면 방금 만든 자리들을 그룹에 물린다.
        mixerRouted = RouteToMixer(GameSettings.Mixer);

        GameSettings.Changed += OnSettingsChanged;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        GameSettings.Changed -= OnSettingsChanged;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    /// <summary>
    /// 추가 생성(2026-09-29, 믹서) — 효과음 자리는 SFX 그룹, 배경음 자리는 BGM 그룹으로 보낸다.
    ///
    /// AudioSource.outputAudioMixerGroup에 그룹을 넣으면 그 소리는 믹서를 거쳐 나간다. 그러면 GameSettings가
    /// 믹서의 노출 파라미터(BgmVolume·SfxVolume)를 바꾸는 것만으로 묶음 전체의 볼륨이 바뀐다.
    /// 나중에 BGM 그룹에 로우패스(먹먹하게)나 덕킹(효과음이 날 때 음악 줄이기)을 걸 때도 코드는 그대로다.
    /// </summary>
    /// <returns>두 그룹을 다 찾아 물렸으면 true. 하나라도 없으면 아무것도 안 바꾸고 false(곱셈 경로 유지).</returns>
    private bool RouteToMixer(AudioMixer mixer)
    {
        if (mixer == null) return false;

        AudioMixerGroup bgmGroup = FindGroup(mixer, BgmGroupName);
        AudioMixerGroup sfxGroup = FindGroup(mixer, SfxGroupName);
        if (bgmGroup == null || sfxGroup == null)
        {
            // 절반만 물리면 한쪽은 믹서, 한쪽은 곱셈이 돼서 슬라이더마다 동작이 달라진다. 둘 다 있을 때만 쓴다.
            Debug.LogWarning($"[소리] 믹서에 '{BgmGroupName}'·'{SfxGroupName}' 그룹이 둘 다 있어야 한다. " +
                             "믹서 없이(볼륨 곱셈으로) 계속한다.", this);
            return false;
        }

        foreach (AudioSource voice in voices) voice.outputAudioMixerGroup = sfxGroup;
        foreach (AudioSource music in musicSources) music.outputAudioMixerGroup = bgmGroup;
        return true;
    }

    /// <summary>
    /// 추가 생성(2026-09-29, 믹서) — 이름이 정확히 같은 그룹을 찾는다.
    /// FindMatchingGroups는 경로 일부로 찾아서 "SFX"로 "SFX_UI" 같은 것도 걸리므로 이름을 한 번 더 비교한다.
    /// </summary>
    private static AudioMixerGroup FindGroup(AudioMixer mixer, string groupName)
    {
        foreach (AudioMixerGroup group in mixer.FindMatchingGroups(groupName))
            if (group.name == groupName) return group;

        return null;
    }

    /// <summary>재생 자리 하나를 만든다. 전부 2D다 — 카메라가 방 하나를 다 비추는 게임이라 소리 위치를 따로 두지 않는다.</summary>
    private AudioSource CreateSource(bool music)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = music;
        source.spatialBlend = 0f;

        // 소리가 한꺼번에 많이 날 때 배경음악이 먼저 잘리지 않게 우선순위를 가장 높게 둔다(숫자가 작을수록 높다).
        source.priority = music ? 0 : 128;
        return source;
    }

    // ── 효과음 ────────────────────────────────────────────────────────────

    private void PlaySfx(SfxId id)
    {
        SoundBank.SfxEntry entry = bank != null ? bank.Find(id) : null;
        if (entry == null || entry.clips == null || entry.clips.Length == 0) return;

        AudioClip clip = entry.clips[Random.Range(0, entry.clips.Length)];
        if (clip == null) return;

        // 겹침 막기 — 너무 붙어서 오거나 이미 여러 개 나고 있으면 이번 것은 건너뛴다.
        float now = Time.unscaledTime;
        if (lastPlayedAt.TryGetValue(id, out float last) && now - last < entry.minInterval) return;
        if (CountPlaying(id) >= entry.maxVoices) return;

        int voice = PickVoice();
        AudioSource source = voices[voice];
        source.clip = clip;
        source.volume = entry.volume * SfxScale;   // 수정(2026-09-29, 믹서) — 믹서가 있으면 곱하지 않는다
        source.pitch = 1f + Random.Range(-entry.pitchJitter, entry.pitchJitter);
        source.Play();

        voiceIds[voice] = id;
        lastPlayedAt[id] = now;
    }

    /// <summary>지금 이 효과음을 내고 있는 자리 수.</summary>
    private int CountPlaying(SfxId id)
    {
        int count = 0;
        for (int i = 0; i < voices.Length; i++)
            if (voiceIds[i] == id && voices[i].isPlaying) count++;

        return count;
    }

    /// <summary>
    /// 빈 자리를 고른다. 다 차 있으면 돌아가며 다음 자리를 뺏는다 — 돌아가며 쓰니 대개 가장 오래전에 시작한 소리가 잘린다.
    /// </summary>
    private int PickVoice()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            int index = (nextVoice + i) % voices.Length;
            if (voices[index].isPlaying) continue;

            nextVoice = (index + 1) % voices.Length;
            return index;
        }

        int stolen = nextVoice;
        nextVoice = (nextVoice + 1) % voices.Length;
        return stolen;
    }

    // ── 배경음악 ──────────────────────────────────────────────────────────

    private void ChangeMusic(MusicId id, float fadeSeconds)
    {
        if (id == currentMusic) return;

        // 목록에 없거나 클립이 비었으면 "끄기"로 친다.
        SoundBank.MusicEntry entry = id != MusicId.None && bank != null ? bank.Find(id) : null;
        if (entry != null && entry.clip == null) entry = null;

        currentMusic = entry != null ? id : MusicId.None;
        currentMusicVolume = entry != null ? entry.volume : 0f;

        AudioSource from = musicSources[activeMusic];
        AudioSource to = null;

        if (entry != null)
        {
            activeMusic = 1 - activeMusic;
            to = musicSources[activeMusic];
            to.clip = entry.clip;
            to.volume = 0f;
            to.Play();
        }

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(CrossFade(from, to, fadeSeconds));
    }

    /// <summary>
    /// 앞 곡을 줄이면서 새 곡을 키운다. 바로 바꾸면 곡이 뚝 끊겨서 방이 바뀐 게 아니라 소리가 고장 난 것처럼 들린다.
    /// </summary>
    /// <param name="to">새 곡을 튼 자리. null이면 앞 곡을 끄기만 한다.</param>
    private IEnumerator CrossFade(AudioSource from, AudioSource to, float seconds)
    {
        float fromStart = from.volume;

        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float u = t / seconds;
            from.volume = Mathf.Lerp(fromStart, 0f, u);

            // 목표 볼륨을 매 프레임 다시 읽는다 — 페이드 중에 설정 창에서 볼륨을 바꿔도 따라간다.
            if (to != null) to.volume = Mathf.Lerp(0f, currentMusicVolume * BgmScale, u);
            yield return null;
        }

        from.volume = 0f;
        from.Stop();
        from.clip = null;

        if (to != null) to.volume = currentMusicVolume * BgmScale;
        fadeRoutine = null;
    }

    /// <summary>
    /// 설정 창에서 배경음 볼륨을 움직이면 바로 들려야 한다. 효과음은 짧아서 다음 재생부터 적용돼도 충분하다.
    /// 페이드 중이면 페이드가 매 프레임 새 값을 읽으므로 여기서 건드리지 않는다.
    /// </summary>
    private void OnSettingsChanged()
    {
        if (fadeRoutine != null || currentMusic == MusicId.None) return;

        musicSources[activeMusic].volume = currentMusicVolume * BgmScale;
    }

    /// <summary>씬이 열리면 그 씬에 정해 둔 곡으로 바꾼다(같은 곡이면 끊지 않고 이어진다).</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single || bank == null) return;

        ChangeMusic(bank.MusicForScene(scene.name), 1.5f);
    }
}
