using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Project/Art/Sprites 아래 텍스처의 임포트 설정을 코드로 강제하는 AssetPostprocessor.
///
/// 이 파일이 존재하는 이유는 <see cref="AshProjectSetup.PixelsPerUnit"/> 주석에 이미 적혀 있다.
/// PPU 상수는 "선언일 뿐 스스로 강제되지 않아서", 텍스처를 드래그해 넣을 때마다 유니티 기본값
/// 100이 먹었고 그 결과 스프라이트마다 PPU가 제각각이 됐다. 인스펙터에서 손으로 고치는 방식은
/// 파일이 늘어나면 반드시 하나를 빠뜨리고, 빠뜨린 하나는 "얘만 유독 크게 나오는" 버그가 된다.
/// 그래서 규칙을 코드에 두고 임포트할 때마다 다시 먹인다.
///
/// <b>인스펙터에서 수동으로 바꾼 값은 다음 임포트 때 되돌아간다.</b> 이건 부작용이 아니라 의도다.
/// 규칙을 바꾸고 싶으면 이 파일의 표를 고쳐야 한다.
///
/// AssetPostprocessor를 쓴 이유: 유니티가 임포트 파이프라인에 제공하는 내장 훅이다.
/// 메뉴 버튼으로 만들면 "누르는 걸 잊는" 경로가 생기는데, 이건 잊을 수가 없다.
/// </summary>
public class AshSpriteImportRules : AssetPostprocessor
{
    // ── 경로 규칙 ──────────────────────────────────────────────────────────

    /// <summary>이 폴더 아래에 있는 텍스처만 건드린다. TextMesh Pro나 패키지 샘플은 손대면 안 된다.</summary>
    private const string SpriteRoot = "Assets/Project/Art/Sprites/";

    /// <summary>
    /// 추가 생성 — 화면 UI에 쓰는 스프라이트 폴더(스태미나 게이지 프레임 등).
    ///
    /// Art 폴더 전체를 대상으로 하지 않고 UI만 따로 적은 이유: Art/Title과 Art/Result의 배경
    /// 이미지는 이미 지금 설정으로 씬에 배치돼 크기가 맞춰져 있다. 여기서 규칙을 먹이면
    /// PPU가 바뀌면서 타이틀 화면의 배경 크기가 통째로 달라진다. 건드릴 이유가 없으니 뺐다.
    /// </summary>
    private const string UiRoot = "Assets/Project/Art/UI/";

    /// <summary>
    /// 추가 생성 — TMP 폰트 아틀라스 폴더. UI 폴더 안에 있지만 제외해야 한다.
    ///
    /// 폰트 아틀라스는 AshFontAtlasPointFilter가 Point 필터로 관리하는 대상이고, 여기서
    /// Bilinear를 덮어씌우면 픽셀 폰트가 뿌옇게 뭉개진다. 두 도구가 같은 파일을 서로 다른
    /// 값으로 되돌리는 싸움이 나므로 경계를 명확히 나눈다.
    /// </summary>
    private const string FontRoot = "Assets/Project/Art/UI/Fonts/";

    /// <summary>
    /// 추가 생성 — 캐릭터 그림을 담는 또 하나의 뿌리.
    ///
    /// 보스 그림을 여기에 넣으면서 생겼다(Art/Characters/Boss/AshKing). 원래 뿌리인
    /// Art/Sprites 아래로 옮기지 않고 규칙을 넓힌 이유: 폴더를 옮기면 GUID는 남아도
    /// <b>이미 잘라둔 스프라이트의 경로 참조가 전부 갱신 대상이 된다.</b> 규칙 한 줄을 넓히는
    /// 쪽이 건드리는 곳이 훨씬 적다.
    /// </summary>
    private const string CharacterRoot = "Assets/Project/Art/Characters/";

    /// <summary>
    /// 추가 생성 — 방 배경 그림 폴더(로비, 보스방).
    ///
    /// 배경은 캐릭터와 PPU가 다르다. 캐릭터는 24로 잘게 잡아 화면에 크게 나오지만,
    /// 배경까지 그 값으로 넣으면 방 하나가 화면을 수십 배로 넘어간다.
    /// </summary>
    private const string EnvironmentRoot = "Assets/Project/Art/Environment/";

    /// <summary>플레이어 스프라이트 폴더. 경로에 이 조각이 들어 있으면 캐릭터 규칙을 쓴다.</summary>
    private const string PlayerFolder = "/Player/";

    /// <summary>보스 스프라이트 폴더. 캐릭터 규칙을 공유한다.</summary>
    private const string BossFolder = "/Boss/";

    /// <summary>
    /// 추가 생성 — 일반 적 스프라이트 폴더.
    ///
    /// 플레이어와 같은 규칙을 쓰는 이유: 적 시트를 플레이어와 완전히 같은 좌표 규격
    /// (셀 256, 발끝 y=216)으로 뽑았다. PPU가 다르면 같은 바닥에 선 두 캐릭터의 키 비율이
    /// 그림과 달라진다. 적 141px / 플레이어 160px = 88%라는 관계가 화면에서 유지되려면
    /// 둘이 반드시 같은 PPU여야 한다.
    /// </summary>
    private const string EnemyFolder = "/Enemy/";

    // ── 규격 상수 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 캐릭터 스프라이트의 PPU. 타일 규격(<see cref="AshProjectSetup.PixelsPerUnit"/> = 32)과
    /// 일부러 다르게 잡은 값이다.
    ///
    /// 캐릭터 시트는 셀 256px에 캐릭터 키가 160px로 그려져 있다. 여기에 PPU 32를 먹이면
    /// 캐릭터가 5유닛(화면 세로 11.25유닛의 절반)이 되어 방 하나를 혼자 채운다. 반대로
    /// 시트를 2.5배 축소해서 PPU 32에 맞추려면 셀이 102.4px이라 정수로 안 떨어진다.
    ///
    /// 160 / 80 = 2유닛. AshProjectSetup 주석이 처음부터 상정한 "캐릭터 = 2유닛"이 리샘플링
    /// 없이 그대로 나온다. <b>월드 유닛 규격은 하나도 안 바뀐다</b> — 방 13x9, 카메라 5.625
    /// 그대로고, 바뀌는 건 "캐릭터 1유닛을 몇 픽셀로 그리느냐" 하나뿐이다.
    ///
    /// <b>수정(Game 씬 규격 확인 시점): 80 → 32.</b>
    ///
    /// 위 판단은 Game 씬이 규격(카메라 5.625, 화면 20x11.25유닛)대로일 거라는 전제 위에 있었다.
    /// 실제 씬을 재보니 카메라가 14였고 화면이 49.8x28유닛이었다. 던전 배경 Room_raw.png가
    /// 1678x937에 PPU 32라 52.4x29.3유닛이고, 카메라가 거기 맞춰져 있었기 때문이다.
    ///
    /// 그 화면에서 2유닛짜리 캐릭터는 세로의 7%밖에 안 된다(규격 의도는 17.8%). 배경을 기준으로
    /// 씬을 유지하기로 했으므로 캐릭터를 화면에 맞춘다 — 160 / 32 = <b>5유닛</b>이고,
    /// 5 / 28 = 17.9%로 원래 의도한 비율이 그대로 재현된다.
    ///
    /// <b>재수정(화면에서 확인 후): 32 → 24.</b>
    ///
    /// 32(캐릭터 5유닛 = 화면 세로의 17.9%)는 계산상 원래 의도한 비율이었지만, 실제로 보니
    /// 여전히 작았다. 이 프로젝트는 화면 하나가 방 하나인 아레나 구조라 캐릭터가 화면에서
    /// 차지하는 비중이 일반적인 탑다운보다 커야 읽힌다. 160 / 24 = <b>6.67유닛</b>이고
    /// 화면 세로의 23.8%가 된다.
    ///
    /// 여기서 타일 규격(<see cref="AshProjectSetup.PixelsPerUnit"/> = 32)과 다시 갈라졌다.
    /// 배경은 실제로 그려진 해상도가 있어 그 값을 바꿀 수 없고, 캐릭터는 "화면에서 얼마나 크게
    /// 보여야 하는가"가 기준이라 두 값이 꼭 같을 이유가 없다.
    ///
    /// <b>더 키우려면 이 숫자만 낮추면 된다.</b> 화면 세로가 28유닛일 때:
    ///   PPU 32 → 5.00유닛 (17.9%)   PPU 24 → 6.67유닛 (23.8%)
    ///   PPU 20 → 8.00유닛 (28.6%)   PPU 16 → 10.0유닛 (35.7%)
    /// 다만 원본 그림이 160px이라 20 아래로 내려가면 2배 이상 확대되어 뿌옇게 뭉개진다.
    /// 그 이상 키우고 싶으면 PPU가 아니라 시트를 더 큰 해상도로 다시 뽑아야 한다.
    ///
    /// 주의 — 이 값을 바꾸면 캐릭터의 월드 크기가 바뀐다. <see cref="AshPlayerPrefabBuilder"/>의
    /// 콜라이더는 캐릭터 키에 대한 비율로 적어뒀으므로 자동으로 따라가지만, PlayerController의
    /// 이동 속도는 방 크기가 정하는 값이라 따라가지 않는다(그래서 여기서도 안 건드린다).
    /// </summary>
    public const float CharacterPixelsPerUnit = 24f;

    /// <summary>
    /// 추가 생성 — 플레이어만 쓰는 PPU.
    ///
    /// 왜 플레이어만 다른가:
    /// 같은 PPU 24로 실측했더니 플레이어가 5.88 x 7.92유닛, 망령이 4.96 x 5.83유닛이었다.
    /// 플레이어가 잡몹보다 36% 크고, 보스(6.25 x 8.33)와도 거의 같아서 <b>보스가 커 보이지
    /// 않았다.</b> 방(세로 29유닛)에 비해서도 커서 전투 공간이 좁게 느껴졌다.
    ///
    /// 왜 프리팹 스케일이 아니라 PPU로 줄이는가:
    /// localScale로 0.736배를 하면 픽셀이 비정수 배율로 늘어나 픽셀아트가 뭉개지고,
    /// 움직일 때 가장자리가 아른거린다. PPU를 올리면 픽셀 격자는 그대로 두고 유닛 환산만
    /// 바뀌므로 그림이 깨지지 않는다.
    ///
    /// 32를 고른 이유: 190px / 32 = 5.94유닛으로 망령(5.83)과 거의 같아지고,
    /// 방 타일 규격(<see cref="AshProjectSetup.PixelsPerUnit"/> = 32)과도 숫자가 통일된다.
    ///
    /// 주의 — 이 값을 바꾸면 플레이어 콜라이더와 히트박스 크기도 같이 맞춰야 한다.
    /// 그림만 줄면 판정은 예전 크기로 남아 "안 맞았는데 맞는" 상태가 된다.
    /// </summary>
    public const float PlayerPixelsPerUnit = 32f;

    /// <summary>
    /// 캐릭터 스프라이트의 필터 모드가 Point가 아니라 Bilinear인 이유.
    ///
    /// 이 시트를 실측했더니 한 프레임의 불투명 픽셀 고유 색이 8,243개였고, 색 경계가 4px
    /// 격자에 맞는 비율이 25%(= 무작위 수준)였다. 즉 "픽셀아트처럼 보이게 그린 안티에일리어싱
    /// 일러스트"지 진짜 픽셀아트가 아니다.
    ///
    /// 진짜 픽셀아트라면 Point가 맞다(픽셀을 뭉개지 않으려고). 하지만 이 아트는 화면에 축소돼
    /// 그려지고(텍스처 160px → 화면 약 64px), 그때 Point는 원본 픽셀을 골라 찍는 방식이라
    /// 프레임마다 다른 픽셀이 선택되면서 가장자리가 지글거린다. Bilinear는 섞어서 뽑기 때문에
    /// 조금 부드러워지는 대신 그 떨림이 없다.
    ///
    /// 타일/배경은 나중에 진짜 픽셀아트로 그릴 예정이라 Point를 유지한다.
    /// </summary>
    private const FilterMode CharacterFilterMode = FilterMode.Bilinear;

    /// <summary>
    /// 캐릭터 시트 중 가장 큰 것이 2048x256(walk 8프레임)이다. 이 값이 2048보다 작으면
    /// 유니티가 임포트 단계에서 텍스처를 통째로 축소해버리고, 그러면 셀이 256px이 아니게 되어
    /// 슬라이스 좌표가 전부 어긋난다. 시트가 더 길어지면 이 값을 같이 올려야 한다.
    /// </summary>
    private const int CharacterMaxTextureSize = 2048;

    // ── 훅 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 추가 생성 — 한 장에 여러 칸이 든 시트인가.
    ///
    /// 정규화 도구가 결과 파일에 "_3frames_", "_6frames_" 같은 이름을 붙인다.
    /// 그 규칙을 그대로 판별에 쓴다.
    /// </summary>
    private static bool IsMultiFrameSheet(string path) => path.Contains("frames_");

    /// <summary>
    /// 추가 생성 — 던전 소품 시트인가.
    ///
    /// 폴더가 아니라 파일 이름으로 가르는 이유: Dungeon 폴더 안에 방 배경(Room_raw)과
    /// 소품 시트가 같이 들어 있는데 둘의 PPU가 달라야 한다. 폴더를 나누는 방법도 있지만,
    /// 이미 씬과 프리팹이 지금 경로를 참조하고 있어서 옮기면 그 참조가 전부 끊긴다.
    /// </summary>
    private static bool IsPropSheet(string path)
    {
        return path.Contains("/Dungeon/") &&
               (path.Contains("Props") || path.Contains("InteractionStates") ||
                path.Contains("FloorDecals"));
    }

    /// <summary>
    /// 텍스처가 임포트되기 직전에 불린다. 여기서 설정을 바꾸면 그 설정으로 임포트된다.
    /// 임포트가 끝난 뒤(OnPostprocessTexture)에 바꾸면 다시 임포트를 유발하므로 여기가 맞다.
    /// </summary>
    private void OnPreprocessTexture()
    {
        // 추가 생성 — 폰트 아틀라스는 다른 도구의 담당이다. 다른 판정보다 먼저 빠져나간다.
        if (assetPath.StartsWith(FontRoot, System.StringComparison.Ordinal)) return;

        // 추가 생성 — Raw 폴더는 손대지 않는다.
        //
        // 정규화 전 원본이 들어 있는 자리다. 여기까지 Multiple로 잡으면 쓰지도 않을 원본이
        // 전부 스프라이트로 임포트되어 메모리와 임포트 시간만 먹는다.
        if (assetPath.Contains("/Raw/")) return;

        // 우리 스프라이트 폴더 밖이면 아무것도 하지 않는다. 패키지 샘플이나 TMP 아틀라스까지
        // 건드리면 남의 에셋을 망가뜨린다.
        //
        // 수정(게이지 프레임 추가 시점): 화면 UI 스프라이트도 대상에 넣었다.
        bool isSpriteFolder = assetPath.StartsWith(SpriteRoot, System.StringComparison.Ordinal);
        bool isUiFolder = assetPath.StartsWith(UiRoot, System.StringComparison.Ordinal);

        // 추가 생성(보스 작업 시점) — 캐릭터와 방 배경의 새 뿌리.
        bool isCharacterFolder = assetPath.StartsWith(CharacterRoot, System.StringComparison.Ordinal);
        bool isEnvironmentFolder = assetPath.StartsWith(EnvironmentRoot, System.StringComparison.Ordinal);

        if (!isSpriteFolder && !isUiFolder && !isCharacterFolder && !isEnvironmentFolder) return;

        // assetImporter는 AssetPostprocessor가 제공하는 "지금 임포트 중인 에셋의 임포터"다.
        // 텍스처 임포트 훅 안이므로 TextureImporter인 것이 보장된다.
        var importer = (TextureImporter)assetImporter;

        // Art/Characters 아래는 전부 캐릭터다. Art/Sprites 쪽은 던전 소품도 같이 있어서
        // 폴더 이름으로 한 번 더 가른다.
        bool isCharacter = isCharacterFolder ||
                           (isSpriteFolder &&
                            (assetPath.Contains(PlayerFolder) ||
                             assetPath.Contains(BossFolder) ||
                             assetPath.Contains(EnemyFolder)));

        importer.textureType = TextureImporterType.Sprite;

        // 알파가 있는 PNG를 스프라이트로 쓸 때 켜야 반투명 가장자리가 검게 뭉치지 않는다.
        importer.alphaIsTransparency = true;

        // 2D 스프라이트는 밉맵이 필요 없다. 켜두면 메모리를 33% 더 먹고, 카메라 거리에 따라
        // 유니티가 멋대로 흐린 밉을 골라서 스프라이트가 뿌옇게 나오는 사고가 난다.
        importer.mipmapEnabled = false;

        // 스프라이트 가장자리에서 반대편 픽셀을 끌어오지 않게 한다. 시트 양 끝 프레임에서
        // 반대쪽 프레임 색이 한 줄 비쳐 보이는 현상을 막는다.
        importer.wrapMode = TextureWrapMode.Clamp;

        if (isCharacter)
        {
            // 추가 생성 — 플레이어만 PPU가 다르다. 잡몹과 키를 맞추려고 32로 올렸다.
            bool isPlayer = assetPath.Contains(PlayerFolder);
            importer.spritePixelsPerUnit = isPlayer ? PlayerPixelsPerUnit : CharacterPixelsPerUnit;
            importer.filterMode = CharacterFilterMode;
            importer.maxTextureSize = CharacterMaxTextureSize;

            // 압축은 캐릭터 시트에 쓰지 않는다. DXT 계열은 4x4 블록 단위로 색을 뭉개는데,
            // 이 아트는 후드 그림자와 검날 균열처럼 좁은 면적의 색 대비가 핵심이라 그게 먼저
            // 깨진다. 캐릭터 텍스처는 몇 장뿐이라 무압축이어도 용량 부담이 없다.
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            // 시트는 여러 프레임이 든 한 장이므로 Multiple이어야 한다.
            // 이미 Multiple이면 대입하지 않는다 — 같은 값이라도 대입 자체가 슬라이스 정보를
            // 다시 계산하게 만들 수 있어서, 잘라둔 프레임을 날리지 않으려는 방어다.
            if (importer.spriteImportMode != SpriteImportMode.Multiple)
                importer.spriteImportMode = SpriteImportMode.Multiple;
        }
        else if (isUiFolder)
        {
            // 추가 생성 — 화면 UI 스프라이트.

            // 100은 CanvasScaler의 Reference Pixels Per Unit 기본값이다. 같은 값으로 맞춰야
            // Image의 "Set Native Size"를 눌렀을 때 텍스처 픽셀 수와 UI 크기가 1:1로 맞는다.
            // 여기가 다르면 게이지 프레임이 의도한 것보다 크거나 작게 나온다.
            importer.spritePixelsPerUnit = 100f;

            // UI는 화면 해상도에 맞춰 확대·축소된다. 게이지 프레임은 1024px 원본이 화면에서
            // 512px 정도로 줄어드는데, Point로 두면 얇은 장식선이 계단처럼 끊긴다.
            importer.filterMode = FilterMode.Bilinear;

            // DXT5(BC3)는 4x4 블록 단위로 색을 뭉갠다. 이 프레임은 1~2px짜리 금속 하이라이트와
            // 양 끝 화살촉 장식이 형태의 전부라 압축하면 그게 먼저 뭉개진다.
            // 인스펙터에 "RGBA Compressed DXT5"로 잡혀 있던 걸 여기서 되돌린다.
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            // UI 프레임은 대개 통짜 한 장이다. Multiple로 두면 잘린 영역이 하나도 없어서
            // Sprite 에셋 자체가 생성되지 않고, Image에 넣을 수가 없다.
            // (실제로 게이지 프레임이 spriteMode: 2로 들어와 문제가 됐다.)
            //
            // <b>다만 여러 칸이 든 시트는 예외다.</b> 유물 아이콘처럼 한 장에 여러 개가
            // 들어 있는 파일까지 Single로 강제하면, 슬라이서가 Multiple로 바꿔도 임포트할
            // 때마다 되돌아가서 잘린 스프라이트가 영영 안 생긴다. 실제로 그렇게 막혔다.
            //
            // 파일 이름으로 가르는 이유: 임포트 시점에는 이미 잘려 있는지 알 수 없고
            // (이 콜백이 그 설정을 정하는 자리다), 우리 시트는 정규화 도구가
            // "..._3frames_768x256" 형태로 이름을 붙여주므로 규칙이 확실하다.
            if (!IsMultiFrameSheet(assetPath))
                importer.spriteImportMode = SpriteImportMode.Single;
        }
        else if (isEnvironmentFolder)
        {
            // 추가 생성 — 방 배경(로비, 보스방).
            //
            // 32는 기존 던전 방(Room_v2)과 같은 값이다. 픽셀 밀도를 맞춰야 방을 오갈 때
            // 바닥 무늬의 굵기가 달라 보이지 않는다.
            //
            // 주의: 같은 PPU라도 <b>방 크기는 다르다.</b> 기존 방은 1678x937(52x29유닛)인데
            // 새 방들은 1254x1254(39x39유닛)라 모양부터 정사각형이다. 벽 위치를 그대로
            // 물려받을 수 없고 방마다 따로 잡아야 한다.
            importer.spritePixelsPerUnit = 32f;

            // 배경은 화면에서 축소되어 그려진다. Point로 두면 바닥 균열이 계단처럼 끊긴다.
            importer.filterMode = FilterMode.Bilinear;

            // 배경 한 장은 통짜다. 잘라 쓸 것이 없다.
            importer.spriteImportMode = SpriteImportMode.Single;
        }
        else if (IsPropSheet(assetPath))
        {
            // 추가 생성 — 던전 소품 시트.
            //
            // 방 배경(Room_raw)과 같은 폴더인데 PPU를 따로 주는 이유: 배경은 1678px 한 장이
            // 방 전체(52유닛)라 PPU 32가 맞지만, 소품 시트는 한 칸에 소품 하나를 꽉 채워 그려서
            // 항아리 하나가 276px이다. PPU 32를 먹이면 항아리가 8.6유닛 — 캐릭터(6.67)보다 커진다.
            //
            // 수정(첫 배치 확인 후): 130 → 60.
            //
            // 130에서는 기둥이 2.5유닛(캐릭터 6.67의 37%)이라 엄폐물이 아니라 바닥 장식처럼
            // 보였다. 목표 크기를 키우는 방법이 두 가지인데, 프리팹 배율을 2배로 올리면
            // 원본을 2배 확대해 그리게 되어 뿌옇게 뭉갠다. PPU를 낮추면 <b>원본 픽셀을 그대로
            // 쓰면서</b> 월드에서만 커진다.
            //
            // 60으로 잡으면 기둥이 5.4유닛으로 나와서 목표(5.5)와 거의 같다 — 배율 1.02.
            // 나머지 소품은 전부 축소(배율 0.3~0.9)라 화질 손실이 없다. 확대는 피하고
            // 축소만 남기는 게 이 값을 고른 기준이다.
            importer.spritePixelsPerUnit = 60f;

            // 캐릭터와 같은 이유로 Bilinear다. 이 소품들도 안티에일리어싱된 그림이고,
            // 화면에서 축소되어 그려진다.
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            // 한 장에 소품 여러 개가 들어 있다.
            if (importer.spriteImportMode != SpriteImportMode.Multiple)
                importer.spriteImportMode = SpriteImportMode.Multiple;
        }
        else
        {
            // 타일/배경 스프라이트는 프로젝트 기본 규격을 따른다.
            importer.spritePixelsPerUnit = AshProjectSetup.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
