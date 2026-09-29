using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-29, 소리) — 소리 목록(<see cref="SoundBank"/>)을 만들고 받아 둔 음원을 이름표에 붙인다.
/// 기본 공격 스킬에 휘두름 소리 이름표를 붙이고, 배경음악은 스트리밍으로 읽게 바꾼다.
/// 수정(2026-09-29, 소리 2차) — 스킬 다섯(기본·Q·W·E·R)의 소리 칸과 대시·발소리까지 채운다. 마법 소리 두 팩(lentikula)은 CC0라 저장소에 있다.
///
/// 규칙은 다른 빌더와 같다 — <b>없는 것만 더하고 비어 있는 칸만 채운다.</b> 인스펙터에서 바꾼 클립·볼륨은 다시 돌려도 그대로다.
///
/// 효과음 팩(Minifantasy Dungeon)은 저장소에 없다(팩 재배포 금지 조건이라 .gitignore). 저장소를 새로 받은 컴퓨터에서는
/// 효과음 칸이 비어 경고만 남고, 게임은 소리 없이 그대로 돈다. 팩을 같은 자리에 넣고 다시 돌리면 채워진다.
/// </summary>
public static class AshSoundBankBuilder
{
    private const string ResourcesFolder = "Assets/Project/Resources";
    private const string BankFolder = ResourcesFolder + "/Audio";
    private const string BankPath = BankFolder + "/SoundBank.asset";
    // 수정(2026-09-29, 소리 2차) — 팩이 셋으로 늘어서 폴더를 나눴다. 효과음 기본값은 이제 전체 경로로 적는다.
    private const string MinifantasyFolder = "Assets/Project/Audio/SFX/MinifantasyDungeon/";      // Leohpaz, 재배포 금지 → 저장소에 없음
    private const string BasicSpellFolder = "Assets/Project/Audio/SFX/BasicSpellImpacts/";      // lentikula, CC0
    private const string DruidSpellFolder = "Assets/Project/Audio/SFX/DruidSpellImpacts/";      // lentikula, CC0
    private const string BgmFolder = "Assets/Project/Audio/BGM/";
    private const string SkillFolder = "Assets/Project/Data/Skills/";

    /// <summary>추가 생성(2026-09-29) — 팩별 파일 경로를 짧게 적으려는 도우미들.</summary>
    private static string Mini(string file) => MinifantasyFolder + file;
    private static string Fire(int n) => $"{BasicSpellFolder}Fire Spell Impacts/Fire Spell Impact {n}.wav";
    private static string Lightning(int n) => $"{BasicSpellFolder}Lightning Spell Impacts/Lightning Spell Impact {n}.wav";
    private static string Earth(int n) => $"{DruidSpellFolder}Earth Spell Impacts/Earth Spell Impact {n}.wav";
    private static string Wind(int n) => $"{DruidSpellFolder}Wind Spell Impacts/Wind Spell Impact {n}.wav";

    /// <summary>효과음 하나의 기본값. 파일은 효과음 팩 안의 이름이다. 볼륨·흔들림·간격·동시 수는 들어 보고 인스펙터에서 바꾼다.</summary>
    private readonly struct SfxDefault
    {
        public readonly SfxId Id;
        public readonly float Volume;
        public readonly float Jitter;
        public readonly float Interval;
        public readonly int Voices;
        public readonly string[] Files;

        public SfxDefault(SfxId id, float volume, float jitter, float interval, int voices, params string[] files)
        {
            Id = id;
            Volume = volume;
            Jitter = jitter;
            Interval = interval;
            Voices = voices;
            Files = files;
        }
    }

    private static readonly SfxDefault[] SfxDefaults =
    {
        // 휘두름은 목소리 없는 바람 소리(sword_miss)다. 기본 공격은 자주 누르는데 매번 기합이 들어가면 금방 질린다.
        new SfxDefault(SfxId.PlayerSwing, 0.8f, 0.06f, 0.05f, 2, Mini("27_sword_miss_1.wav"), Mini("27_sword_miss_2.wav"), Mini("27_sword_miss_3.wav")),

        // 칼이 맞힌 소리. 한 번 휘둘러 여럿을 맞혀도 0.05초 안의 것은 한 번으로 친다.
        new SfxDefault(SfxId.EnemyHit, 0.9f, 0.06f, 0.05f, 3, Mini("26_sword_hit_1.wav"), Mini("26_sword_hit_2.wav"), Mini("26_sword_hit_3.wav")),

        // 플레이어 피격은 한 번에 하나만 — 비명이 겹치면 시끄럽기만 하다.
        new SfxDefault(SfxId.PlayerHurt, 1f, 0.04f, 0.2f, 1, Mini("11_human_damage_1.wav"), Mini("11_human_damage_2.wav"), Mini("11_human_damage_3.wav")),

        new SfxDefault(SfxId.PlayerDeath, 1f, 0f, 1f, 1, Mini("14_human_death_spin.wav")),

        new SfxDefault(SfxId.ChestOpen, 1f, 0.03f, 0.1f, 1,
            Mini("01_chest_open_1.wav"), Mini("01_chest_open_2.wav"), Mini("01_chest_open_3.wav"), Mini("01_chest_open_4.wav")),

        // 추가 생성(2026-09-29, 소리 2차) — 스킬. 마법 충격음은 Minifantasy보다 10~15dB 크게 녹음돼 있어서 볼륨을 0.3~0.5로 낮춘다.
        // 고른 기준(들어 보기 전 첫 배정): Q는 땅(1단 짧은 것) + 불(2단 긴 것), W는 바람(놓기) + 불(맞기, 짧은 것),
        // E는 땅 울림(긴 것), R은 바람(모으기) + 번개(큰 폭발). 마음에 안 들면 소리 목록에서 클립만 바꾸면 된다.
        new SfxDefault(SfxId.SkillQSlam, 0.4f, 0.05f, 0.1f, 1, Earth(2), Earth(4)),
        new SfxDefault(SfxId.SkillQBurst, 0.45f, 0.04f, 0.1f, 1, Fire(1), Fire(2)),
        new SfxDefault(SfxId.SkillWRelease, 0.3f, 0.08f, 0.1f, 1, Wind(2), Wind(3)),
        new SfxDefault(SfxId.SkillWImpact, 0.35f, 0.06f, 0.08f, 2, Fire(3), Fire(4), Fire(5)),
        new SfxDefault(SfxId.SkillEBurst, 0.45f, 0.04f, 0.1f, 1, Earth(1), Earth(3), Earth(5)),
        new SfxDefault(SfxId.SkillRCharge, 0.35f, 0.03f, 0.5f, 1, Wind(1), Wind(5)),
        new SfxDefault(SfxId.SkillRBurst, 0.5f, 0.03f, 0.5f, 1, Lightning(1), Lightning(3), Lightning(5)),

        // 추가 생성(2026-09-29, 소리 2차) — 이동. 발소리 파일은 아주 작게 녹음돼 있어(다른 효과음보다 15~20dB 아래) 볼륨을 끝까지 올린다.
        new SfxDefault(SfxId.Footstep, 1f, 0.08f, 0.1f, 2, Mini("16_human_walk_stone_1.wav"), Mini("16_human_walk_stone_2.wav"), Mini("16_human_walk_stone_3.wav")),
        new SfxDefault(SfxId.Dash, 0.9f, 0.05f, 0.1f, 1, Mini("15_human_dash_1.wav"), Mini("15_human_dash_2.wav")),
    };

    /// <summary>
    /// 추가 생성(2026-09-29, 소리 2차) — 스킬 에셋의 소리 칸 기본값(에셋 이름, 칸 이름, 이름표). 비어 있는(None) 칸만 채운다.
    /// 칸 이름은 스킬 스크립트의 직렬화 필드 이름이다.
    /// </summary>
    private static readonly (string asset, string field, SfxId sfx)[] SkillSoundDefaults =
    {
        ("Skill_Basic_AshSlash", "castSfx", SfxId.PlayerSwing),   // 휘두를 때
        ("Skill_Basic_AshSlash", "hitSfx", SfxId.EnemyHit),       // 칼이 맞혔을 때(MeleeSkillData)
        ("Skill_Q_GroundSlam", "nearSfx", SfxId.SkillQSlam),      // 1단(GroundSlamSkillData)
        ("Skill_Q_GroundSlam", "impactSfx", SfxId.SkillQBurst),   // 2단 폭발
        ("Skill_W_EmberArrow", "releaseSfx", SfxId.SkillWRelease),// 놓을 때(ProjectileSkillData)
        ("Skill_W_EmberArrow", "impactSfx", SfxId.SkillWImpact),  // 맞았을 때(투사체가 낸다)
        ("Skill_E_AshPillar", "impactSfx", SfxId.SkillEBurst),    // 판정 순간(AreaSkillData)
        ("Skill_R_KingsEmber", "castSfx", SfxId.SkillRCharge),    // 무릎 꿇고 모을 때
        ("Skill_R_KingsEmber", "impactSfx", SfxId.SkillRBurst),   // 판정 순간
    };

    /// <summary>배경음악 기본값. 둘 다 CC0이라 저장소에 올라가 있다(출처는 각 폴더의 SOURCE.txt).</summary>
    private static readonly (MusicId id, string path, float volume)[] MusicDefaults =
    {
        (MusicId.Ambient, BgmFolder + "DarkCavernAmbient/dark_cavern_ambient_002.ogg", 0.35f),
        (MusicId.Boss, BgmFolder + "HeavyDungeon/heavy_dungeon_bpm160.ogg", 0.35f),
        // 추가 생성(2026-09-29, 타이틀 음악) — 후보 셋 중 루프가 보장된 3번. 출처는 TitleCandidates/SOURCE.txt.
        (MusicId.Title, BgmFolder + "TitleCandidates/03_dark_shrine_loop.ogg", 0.35f),
        // 추가 생성(2026-09-29, 방 음악) — Game 씬 안에서는 방에 들어갈 때 RoomSequenceController가 고른다.
        (MusicId.Tutorial, BgmFolder + "QuietTensionCandidates/tutorial_safe_room.ogg", 0.35f),
        (MusicId.Dungeon, BgmFolder + "QuietTensionCandidates/dungeon_dark_place.ogg", 0.35f),
    };

    /// <summary>씬별 곡 기본값. 결과 화면은 조용히 둔다.</summary>
    private static readonly (string scene, MusicId music)[] SceneDefaults =
    {
        ("Title", MusicId.Title),   // 수정(2026-09-29, 타이틀 음악) — Ambient → Title. 이미 적힌 씬은 빌더가 안 바꾸므로 SoundBank.asset도 같이 고쳤다.
        ("Game", MusicId.None),   // 수정(2026-09-29, 방 음악) — Ambient → None. Game 안의 곡은 방이 정한다(RoomSequenceController).
        ("Result", MusicId.None),
    };

    /// <summary>플레이 중에는 에셋을 만들고 저장할 수 없어서 메뉴를 흐리게 막는다(다른 빌더와 같은 규칙).</summary>
    [MenuItem("Tools/재의 길/씬·세팅/소리 구성", true)]
    private static bool CanBuild() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/재의 길/씬·세팅/소리 구성")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[소리 구성] 플레이 중에는 실행할 수 없다. 플레이를 멈추고 다시 눌러라.");
            return;
        }

        SoundBank bank = EnsureBank();
        var serialized = new SerializedObject(bank);
        var missing = new List<string>();

        int filled = FillSfx(serialized.FindProperty("sfx"), missing)
                     + FillMusic(serialized.FindProperty("music"), missing)
                     + FillSceneMusic(serialized.FindProperty("sceneMusic"));

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(bank);

        // 수정(2026-09-29, 소리 2차) — 기본 공격 하나만 붙이던 것을 스킬 다섯의 소리 칸 전부로 넓혔다.
        int skillSounds = SetSkillSounds();
        int streamed = StreamMusic();

        AssetDatabase.SaveAssets();

        Debug.Log($"[소리 구성] 소리 목록 {BankPath} — 채운 칸 {filled}개, 스킬 소리 칸 {skillSounds}개 채움, " +
                  $"스트리밍으로 바꾼 곡 {streamed}개. 플레이해서 공격·피격·사망·상자·QWER·대시·발소리와 던전·보스 음악을 들어 본다. " +
                  "클립·볼륨은 소리 목록 인스펙터에서, 스킬마다 어느 소리를 낼지는 스킬 에셋의 '소리' 칸에서 바꾼다.", bank);

        if (missing.Count > 0)
        {
            Debug.LogWarning($"[소리 구성] 못 찾은 파일 {missing.Count}개 — Minifantasy 효과음 팩은 저장소에 없어서(재배포 금지) " +
                             "저장소를 새로 받은 컴퓨터면 정상이다. 팩을 Assets/Project/Audio/SFX 아래 같은 자리에 넣고 다시 돌리면 채워진다.\n" +
                             string.Join("\n", missing));
        }
    }

    /// <summary>소리 목록 에셋을 찾고, 없으면 Resources 아래에 만든다.</summary>
    private static SoundBank EnsureBank()
    {
        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        if (bank != null) return bank;

        if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets/Project", "Resources");
        if (!AssetDatabase.IsValidFolder(BankFolder)) AssetDatabase.CreateFolder(ResourcesFolder, "Audio");

        bank = ScriptableObject.CreateInstance<SoundBank>();
        AssetDatabase.CreateAsset(bank, BankPath);
        return bank;
    }

    /// <summary>효과음 칸을 채운다. 이름표가 없으면 기본값으로 더하고, 클립이 하나도 없는 칸만 채운다.</summary>
    /// <returns>새로 더하거나 채운 칸 수.</returns>
    private static int FillSfx(SerializedProperty array, List<string> missing)
    {
        int changed = 0;

        foreach (SfxDefault item in SfxDefaults)
        {
            SerializedProperty entry = FindById(array, (int)item.Id);
            bool added = entry == null;

            if (added)
            {
                // 배열을 늘리면 유니티가 마지막 칸을 복사해 넣는다. 모든 값을 직접 다시 적어야 앞 칸 값이 섞이지 않는다.
                array.arraySize++;
                entry = array.GetArrayElementAtIndex(array.arraySize - 1);
                entry.FindPropertyRelative("id").intValue = (int)item.Id;
                entry.FindPropertyRelative("volume").floatValue = item.Volume;
                entry.FindPropertyRelative("pitchJitter").floatValue = item.Jitter;
                entry.FindPropertyRelative("minInterval").floatValue = item.Interval;
                entry.FindPropertyRelative("maxVoices").intValue = item.Voices;
                entry.FindPropertyRelative("clips").arraySize = 0;
                changed++;
            }

            SerializedProperty clips = entry.FindPropertyRelative("clips");
            if (HasAnyObject(clips)) continue;   // 이미 들어 있으면(사람이 고른 것 포함) 건드리지 않는다

            var found = new List<AudioClip>();
            // 수정(2026-09-29, 소리 2차) — 기본값이 전체 경로라 그대로 읽는다(예전: 효과음 팩 폴더 + 파일 이름).
            foreach (string file in item.Files)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
                if (clip != null) found.Add(clip);
                else missing.Add(file);
            }

            if (found.Count == 0) continue;

            clips.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++)
                clips.GetArrayElementAtIndex(i).objectReferenceValue = found[i];

            if (!added) changed++;
        }

        return changed;
    }

    /// <summary>곡 칸을 채운다. 규칙은 효과음과 같다.</summary>
    private static int FillMusic(SerializedProperty array, List<string> missing)
    {
        int changed = 0;

        foreach ((MusicId id, string path, float volume) in MusicDefaults)
        {
            SerializedProperty entry = FindById(array, (int)id);
            bool added = entry == null;

            if (added)
            {
                array.arraySize++;
                entry = array.GetArrayElementAtIndex(array.arraySize - 1);
                entry.FindPropertyRelative("id").intValue = (int)id;
                entry.FindPropertyRelative("volume").floatValue = volume;
                entry.FindPropertyRelative("clip").objectReferenceValue = null;
                changed++;
            }

            SerializedProperty clipProperty = entry.FindPropertyRelative("clip");
            if (clipProperty.objectReferenceValue != null) continue;

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                missing.Add(path);
                continue;
            }

            clipProperty.objectReferenceValue = clip;
            if (!added) changed++;
        }

        return changed;
    }

    /// <summary>씬별 곡을 채운다. 이미 적힌 씬은 건드리지 않는다.</summary>
    private static int FillSceneMusic(SerializedProperty array)
    {
        int changed = 0;

        foreach ((string scene, MusicId music) in SceneDefaults)
        {
            bool exists = false;
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).FindPropertyRelative("sceneName").stringValue != scene) continue;
                exists = true;
                break;
            }

            if (exists) continue;

            array.arraySize++;
            SerializedProperty entry = array.GetArrayElementAtIndex(array.arraySize - 1);
            entry.FindPropertyRelative("sceneName").stringValue = scene;
            entry.FindPropertyRelative("music").intValue = (int)music;
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// 수정(2026-09-29, 소리 2차) — 기본 공격 휘두름만 붙이던 SetBasicAttackSwing을 스킬 다섯의 소리 칸 전부로 넓혔다.
    /// 비어 있는(None) 칸만 채운다 — 인스펙터에서 바꾼 소리는 다시 돌려도 그대로다.
    /// </summary>
    /// <returns>채운 칸 수.</returns>
    private static int SetSkillSounds()
    {
        int changed = 0;

        foreach ((string asset, string field, SfxId sfx) in SkillSoundDefaults)
        {
            string path = SkillFolder + asset + ".asset";
            var skill = AssetDatabase.LoadAssetAtPath<SkillData>(path);
            if (skill == null)
            {
                Debug.LogWarning($"[소리 구성] 스킬 에셋을 못 찾았다: {path}");
                continue;
            }

            var serialized = new SerializedObject(skill);
            SerializedProperty property = serialized.FindProperty(field);

            // 칸이 없으면 스킬 종류가 다른 것이다(예: 근접 스킬에 '놓는 소리' 칸은 없다). 기본값 표를 고쳐야 한다는 신호라 알린다.
            if (property == null)
            {
                Debug.LogWarning($"[소리 구성] {asset}에 '{field}' 칸이 없다 — 스킬 종류와 기본값 표가 맞는지 본다.", skill);
                continue;
            }

            if (property.intValue != (int)SfxId.None) continue;

            property.intValue = (int)sfx;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(skill);
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// 배경음악을 스트리밍으로 읽게 바꾼다. 기본값(Decompress On Load)이면 2분짜리 스테레오 곡 하나가
    /// 메모리에 약 20MB로 풀린다. 스트리밍은 조금씩 읽어서 곡 길이와 상관없이 가볍다. 이미 다른 방식이면 그대로 둔다.
    /// </summary>
    /// <returns>바꾼 곡 수.</returns>
    private static int StreamMusic()
    {
        int changed = 0;

        foreach (var music in MusicDefaults)
        {
            if (!(AssetImporter.GetAtPath(music.path) is AudioImporter importer)) continue;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.DecompressOnLoad) continue;

            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            changed++;
        }

        return changed;
    }

    /// <summary>배열에서 id가 같은 칸을 찾는다. 없으면 null.</summary>
    private static SerializedProperty FindById(SerializedProperty array, int id)
    {
        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty entry = array.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("id").intValue == id) return entry;
        }

        return null;
    }

    /// <summary>오브젝트 배열에 하나라도 들어 있는가.</summary>
    private static bool HasAnyObject(SerializedProperty array)
    {
        for (int i = 0; i < array.arraySize; i++)
            if (array.GetArrayElementAtIndex(i).objectReferenceValue != null) return true;

        return false;
    }
}
