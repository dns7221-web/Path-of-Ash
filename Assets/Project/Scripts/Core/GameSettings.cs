using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 추가 생성 — 플레이어가 고른 환경 설정(볼륨·화면)을 들고 있고, 저장하고, 실제로 적용한다.
///
/// <b>이 프로젝트는 "죽으면 완전 초기화"가 규칙인데 왜 저장하는가.</b>
/// 저장을 금지한 대상은 게임 진행(영구 성장·언락)이지 플레이 환경이 아니다. 볼륨을 켤 때마다
/// 다시 맞추는 건 난이도가 아니라 불편일 뿐이라, 규칙이 지키려던 것과 아무 상관이 없다.
///
/// <b>static인데 상태를 가진다.</b> <see cref="GameFlow"/>는 상태가 없어서 안전하다고 적어뒀는데
/// 여기는 다르다. 그래도 되는 이유: 값의 진짜 주인은 <see cref="PlayerPrefs"/>이고 이 클래스의
/// 필드는 <b>매번 디스크를 읽지 않으려는 캐시</b>일 뿐이다. 씬이 죽어도 잃을 것이 없고,
/// 한 판의 상태가 아니므로 초기화 규칙과도 부딪히지 않는다.
///
/// <b>믹서가 없어도 동작한다.</b> 지금 프로젝트에는 AudioSource가 하나도 없다. 믹서 에셋을
/// 만들기 전이라면 마스터 볼륨은 <see cref="AudioListener.volume"/>로 대신 적용하고,
/// 배경음·효과음 값은 저장만 해둔다. 나중에 소리를 붙일 때 믹서만 만들어 넣으면
/// 설정 화면은 손댈 것 없이 그대로 이어진다.
/// </summary>
public static class GameSettings
{
    // ── 화면 규격 ─────────────────────────────────────────────────────────

    /// <summary>픽셀아트 기준 해상도. PPU 32와 카메라 설정이 이 값을 전제로 맞춰져 있다.</summary>
    public const int BaseWidth = 640;
    public const int BaseHeight = 360;

    /// <summary>
    /// 고를 수 있는 창 크기 배수.
    ///
    /// 해상도 목록(Screen.resolutions)을 그대로 보여주지 않는 이유:
    /// 이 게임은 640x360을 정수배로 늘려야 픽셀이 안 일렁인다. 1366x768 같은 비정수배를
    /// 고를 수 있게 두면 그건 "설정에서 고를 수 있는 화질 저하"가 된다.
    /// </summary>
    public static readonly int[] WindowScales = { 2, 3, 4 };

    // ── 저장 키 ───────────────────────────────────────────────────────────
    //
    // 접두사를 붙이는 이유: PlayerPrefs는 회사·프로젝트 단위로 한 곳에 쌓인다.
    // "Master" 같은 흔한 이름은 다른 프로젝트의 값과 섞일 수 있다.

    private const string MasterKey = "Ash.Audio.Master";
    private const string BgmKey = "Ash.Audio.Bgm";
    private const string SfxKey = "Ash.Audio.Sfx";
    private const string FullscreenKey = "Ash.Screen.Fullscreen";
    private const string ScaleKey = "Ash.Screen.WindowScale";

    // ── 믹서 연결 ─────────────────────────────────────────────────────────

    /// <summary>Resources 폴더에 둘 믹서 에셋 이름. 없으면 대체 경로로 동작한다.</summary>
    private const string MixerResourcePath = "GameAudioMixer";

    /// <summary>믹서에서 노출(Expose)해둬야 하는 파라미터 이름.</summary>
    private const string MasterParam = "MasterVolume";
    private const string BgmParam = "BgmVolume";
    private const string SfxParam = "SfxVolume";

    // ── 값 ────────────────────────────────────────────────────────────────

    private static float master = 1f;
    private static float bgm = 0.8f;
    private static float sfx = 0.8f;
    private static bool fullscreen = true;
    private static int scaleIndex = 1;   // 기본 3배 = 1920x1080

    private static AudioMixer mixer;
    private static bool loaded;
    private static bool dirty;

    /// <summary>값이 바뀔 때마다 알린다. 설정 화면이 표시를 갱신할 때 쓴다.</summary>
    public static event Action Changed;

    // ── 속성 ──────────────────────────────────────────────────────────────

    /// <summary>마스터 볼륨(0~1).</summary>
    public static float MasterVolume
    {
        get { EnsureLoaded(); return master; }
        set
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(master, value)) return;

            master = value;
            PlayerPrefs.SetFloat(MasterKey, master);
            dirty = true;
            ApplyAudio();
            Changed?.Invoke();
        }
    }

    /// <summary>배경음 볼륨(0~1).</summary>
    public static float BgmVolume
    {
        get { EnsureLoaded(); return bgm; }
        set
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(bgm, value)) return;

            bgm = value;
            PlayerPrefs.SetFloat(BgmKey, bgm);
            dirty = true;
            ApplyAudio();
            Changed?.Invoke();
        }
    }

    /// <summary>효과음 볼륨(0~1).</summary>
    public static float SfxVolume
    {
        get { EnsureLoaded(); return sfx; }
        set
        {
            EnsureLoaded();
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(sfx, value)) return;

            sfx = value;
            PlayerPrefs.SetFloat(SfxKey, sfx);
            dirty = true;
            ApplyAudio();
            Changed?.Invoke();
        }
    }

    /// <summary>전체화면 여부.</summary>
    public static bool Fullscreen
    {
        get { EnsureLoaded(); return fullscreen; }
        set
        {
            EnsureLoaded();
            if (fullscreen == value) return;

            fullscreen = value;
            PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
            dirty = true;
            ApplyScreen();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 창 크기 배수의 <b>번호</b>. 값 자체가 아니라 번호를 저장한다 —
    /// 나중에 고를 수 있는 배수를 바꿔도 저장된 값이 목록 밖으로 나가지 않는다.
    /// </summary>
    public static int WindowScaleIndex
    {
        get { EnsureLoaded(); return scaleIndex; }
        set
        {
            EnsureLoaded();
            value = Mathf.Clamp(value, 0, WindowScales.Length - 1);
            if (scaleIndex == value) return;

            scaleIndex = value;
            PlayerPrefs.SetInt(ScaleKey, scaleIndex);
            dirty = true;
            ApplyScreen();
            Changed?.Invoke();
        }
    }

    /// <summary>지금 고른 창 크기의 배수 값(2, 3, 4).</summary>
    public static int WindowScale => WindowScales[WindowScaleIndex];

    /// <summary>설정 화면에 보여줄 창 크기 문구. 예: "3배 (1920x1080)"</summary>
    public static string WindowScaleLabel
    {
        get
        {
            int s = WindowScale;
            return s + "배 (" + (BaseWidth * s) + "x" + (BaseHeight * s) + ")";
        }
    }

    /// <summary>믹서 에셋이 연결돼 있는가. 설정 화면이 안내 문구를 띄울 때 쓴다.</summary>
    public static bool HasMixer => mixer != null;

    // ── 불러오기와 적용 ───────────────────────────────────────────────────

    /// <summary>
    /// 부팅 때 한 번 불러와 적용한다.
    ///
    /// 씬마다 다시 적용하지 않는 이유: 믹서는 씬이 아니라 에셋이고, Screen 설정은 앱 전역이다.
    /// 둘 다 씬을 넘겨도 그대로 살아 있으므로 한 번이면 충분하다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        // 도메인 리로드를 꺼둔 에디터에서는 이전 실행의 loaded가 true로 남아 있다.
        // 그대로 두면 이번 실행에서 믹서 참조가 없는 채로 시작한다.
        loaded = false;
        mixer = null;

        EnsureLoaded();
        ApplyAll();
    }

    /// <summary>아직 안 읽었으면 PlayerPrefs에서 읽는다.</summary>
    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        master = PlayerPrefs.GetFloat(MasterKey, 1f);
        bgm = PlayerPrefs.GetFloat(BgmKey, 0.8f);
        sfx = PlayerPrefs.GetFloat(SfxKey, 0.8f);
        fullscreen = PlayerPrefs.GetInt(FullscreenKey, 1) != 0;
        scaleIndex = Mathf.Clamp(PlayerPrefs.GetInt(ScaleKey, 1), 0, WindowScales.Length - 1);

        // 믹서는 있으면 쓰고 없으면 대체 경로로 간다. 없다고 에러를 내지 않는다 —
        // 사운드 시스템을 아직 안 만든 지금이 정상 상태다.
        mixer = Resources.Load<AudioMixer>(MixerResourcePath);
    }

    /// <summary>저장된 값을 전부 실제 시스템에 적용한다.</summary>
    public static void ApplyAll()
    {
        EnsureLoaded();
        ApplyAudio();
        ApplyScreen();
    }

    /// <summary>
    /// 볼륨을 믹서에 적용한다.
    ///
    /// 0~1을 그대로 넣지 않고 dB로 바꾸는 이유: 사람 귀는 소리 크기를 로그로 느낀다.
    /// 슬라이더 값을 선형으로 넣으면 위쪽 절반을 움직여도 거의 안 변하고 아래쪽에서
    /// 갑자기 사라진다. dB로 바꾸면 슬라이더를 움직인 만큼 들린다.
    /// </summary>
    private static void ApplyAudio()
    {
        if (mixer != null)
        {
            mixer.SetFloat(MasterParam, ToDecibel(master));
            mixer.SetFloat(BgmParam, ToDecibel(bgm));
            mixer.SetFloat(SfxParam, ToDecibel(sfx));

            // 믹서가 있으면 리스너는 건드리지 않는다. 두 곳에서 같은 소리를 줄이면
            // 슬라이더 절반에서 실제로는 1/4이 된다.
            AudioListener.volume = 1f;
            return;
        }

        // 믹서가 없을 때의 대체 경로. 마스터만 실제로 적용된다.
        // 배경음·효과음은 줄일 대상 자체가 아직 없어서 저장만 되고, 소리를 붙일 때 살아난다.
        AudioListener.volume = master;
    }

    /// <summary>
    /// 0~1 볼륨을 믹서용 dB로 바꾼다. 0은 -80dB(무음)로 보낸다.
    ///
    /// Log10(0)이 음의 무한대라 그대로 넣으면 믹서 값이 NaN이 되고, 그러면 소리가
    /// 다시는 안 돌아온다. 0을 따로 막는 것이 핵심이다.
    /// </summary>
    private static float ToDecibel(float linear)
    {
        if (linear <= 0.0001f) return -80f;
        return Mathf.Log10(linear) * 20f;
    }

    /// <summary>
    /// 창 크기와 전체화면을 적용한다.
    ///
    /// 에디터에서는 실제 적용을 건너뛴다. Game 뷰는 Screen.SetResolution을 따르지 않는다.
    /// <b>대신 무엇을 적용하려 했는지 로그로 남긴다.</b> 그냥 조용히 빠져나오면, 눌러도
    /// 화면이 안 바뀌는 것이 "코드가 아직 안 이어졌다"인지 "에디터라 무시된다"인지
    /// 구별할 수 없다. 값이 여기까지 왔다는 것만 확인돼도 원인 절반이 지워진다.
    /// </summary>
    private static void ApplyScreen()
    {
        if (Application.isEditor)
        {
            string mode = fullscreen
                ? "전체화면(모니터 해상도)"
                : WindowScaleLabel + " 창 모드";

            Debug.Log($"[설정] 화면을 {mode}으로 바꾸려 했다. " +
                      "에디터 Game 뷰는 이 요청을 무시하므로 빌드에서 확인해야 한다.");
            return;
        }

        if (fullscreen)
        {
            // 전체화면에서는 배수가 아니라 모니터 해상도를 쓴다. 배수로 잡으면
            // 모니터보다 크거나 작은 화면이 강제로 늘어나 픽셀이 뭉개진다.
            Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight,
                                 FullScreenMode.FullScreenWindow);
            return;
        }

        int scale = WindowScales[scaleIndex];
        Screen.SetResolution(BaseWidth * scale, BaseHeight * scale, FullScreenMode.Windowed);
    }

    /// <summary>
    /// 바뀐 값을 디스크에 쓴다. <b>설정 화면을 닫을 때 한 번만 부른다.</b>
    ///
    /// 값이 바뀔 때마다 PlayerPrefs.Save()를 부르지 않는 이유: 슬라이더를 끄는 동안
    /// 값이 매 프레임 바뀐다. 그때마다 디스크에 쓰면 드래그가 눈에 띄게 끊긴다.
    /// SetFloat 자체는 메모리에만 쓰므로 값은 이미 안전하다.
    /// </summary>
    public static void Flush()
    {
        if (!dirty) return;

        PlayerPrefs.Save();
        dirty = false;
    }

    /// <summary>모든 설정을 기본값으로 되돌린다.</summary>
    public static void ResetToDefaults()
    {
        EnsureLoaded();

        MasterVolume = 1f;
        BgmVolume = 0.8f;
        SfxVolume = 0.8f;
        Fullscreen = true;
        WindowScaleIndex = 1;

        Flush();
    }
}
