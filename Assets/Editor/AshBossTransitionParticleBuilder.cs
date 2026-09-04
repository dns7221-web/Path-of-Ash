using UnityEditor;
using UnityEngine;

/// <summary>
/// 추가 생성 — 재의 왕 2페이즈 전환의 <b>재 파티클</b>을 만든다.
/// <see cref="AshBossTransitionBuilder"/>가 시트 3장을 만든 뒤에 이어서 부른다.
///
/// <b>이 파티클에는 제어 스크립트가 없다.</b> 계획표의 파티클 항목 네 가지가 전부
/// 파티클 시스템 내장 기능으로 떨어진다:
///
/// <list type="bullet">
/// <item><c>0.000</c> 장막 방출 시작 → Emission의 <b>Rate over Time 곡선</b></item>
/// <item><c>0.875</c> 방출 정지 → 같은 곡선이 이 시각에 0으로 떨어진다</item>
/// <item><c>0.875</c> 힘의 장 ON → <b><c>ParticleSystemForceField</c></b> 컴포넌트</item>
/// <item><c>2.375</c> 파편 버스트 → Emission의 <b>Burst 목록에 time = 2.375</b></item>
/// </list>
///
/// <b>Rate over Time 곡선의 기준 시간이 무엇인지가 여기서 중요하다.</b> 이 곡선은 파티클
/// 개별 수명이 아니라 <b>시스템 duration 전체</b>를 0~1로 훑는다. 그래서 "연출 0.875초에
/// 방출을 멈춘다"를 곡선 하나로 적을 수 있다. 반대로 Velocity over Lifetime 같은 모듈은
/// 파티클 <b>개별 수명</b> 기준이라, 같은 방식으로 "0.875초에 다 같이 빨려든다"를 만들 수 없다
/// — 파티클마다 태어난 시각이 달라서 각자 다른 때에 빨려든다.
///
/// 그래서 빨아들이는 일만 <see cref="ParticleSystemForceField"/>가 맡는다. 힘의 장은
/// <b>살아 있는 파티클 전부에 같은 순간에</b> 작용하는 유일한 내장 수단이다.
/// </summary>
public static class AshBossTransitionParticleBuilder
{
    /// <summary>만들어질 프리팹 경로. 보스 프리팹에 넣을 때 <see cref="AshBossTransitionBuilder"/>가 읽는다.</summary>
    public const string PrefabPath = "Assets/Project/Prefabs/VFX/BossTransitionAsh.prefab";

    /// <summary>
    /// 연출 전체 길이. 시스템 duration을 여기에 맞춰야 Rate over Time 곡선의 가로축이
    /// 계획표의 시각과 같은 자를 쓴다.
    ///
    /// <b>이 숫자가 타임라인 에셋에도 적힌다는 점은 알고 있다.</b> 파티클 시스템의 duration은
    /// 인스펙터에 있는 값이라 타임라인이 대신 정해줄 방법이 없다. 대신 아래 시각들을 전부
    /// 이 상수에서 <b>나눠서</b> 쓰기 때문에, 길이를 바꾸면 곡선과 버스트가 같이 따라간다.
    /// </summary>
    private const float TotalSeconds = 3.125f;

    /// <summary>장막이 멎는 시각. 계획표의 "방출 정지 + 힘의 장 ON"과 같은 자리다.</summary>
    private const float VeilStopSeconds = 0.875f;

    /// <summary>껍질이 깨지는 시각. 파편 버스트가 여기서 한 번 터진다.</summary>
    private const float ShatterSeconds = 2.375f;

    /// <summary>
    /// 장막이 태어나는 상자의 크기(유닛). <b>화면보다 넉넉히 크다.</b>
    ///
    /// 수정(실제로 돌려보고) — 원래는 반지름 16짜리 <b>구의 껍질</b>에서 뿌렸다. 그러면
    /// 재가 중심에서 16유닛 떨어진 <b>껍데기에만</b> 생긴다. 카메라 orthographic size가
    /// 14라 화면 높이는 28유닛(±14)이고 폭은 16:9에서 약 50유닛(±25)이다. 그래서:
    ///
    /// <list type="bullet">
    /// <item>위아래 ±16은 <b>화면 밖</b>이라 안 보이고</item>
    /// <item>좌우 ±16만 화면 안이라 <b>양옆에 띠 두 줄</b>로 보인다</item>
    /// </list>
    ///
    /// "화면 전체를 감싼다"가 아니라 "옆에서 뭔가 지나간다"가 된 이유다. 껍질을 고른 것은
    /// 보스 발밑에서 재가 태어나는 것을 막으려던 것인데, 그 대가로 <b>가운데가 통째로
    /// 비었다.</b> 힘의 장이 어차피 전부 발밑으로 끌어모으므로 가운데에서 태어난 재도
    /// 곧바로 빨려든다 — 막을 이유가 없었다.
    ///
    /// 상자로 바꾸고 화면(50×28)보다 크게 잡는다. 크게 잡는 이유는 <b>보스가 방 안에서
    /// 움직인 자리에 따라 상자도 같이 움직이기 때문</b>이다. 딱 맞추면 보스가 한쪽으로
    /// 치우쳐 있을 때 반대편 화면이 빈다.
    /// </summary>
    private static readonly Vector3 VeilBox = new Vector3(64f, 38f, 0f);

    /// <summary>
    /// 힘의 장이 닿는 거리. 상자 구석(32, 19)까지 잡아야 하므로 대각선 길이(약 37)보다 크게 둔다.
    /// 여기가 짧으면 <b>화면 가장자리의 재만 안 빨려들고 그 자리에 남는다.</b>
    /// </summary>
    private const float FieldRange = 40f;

    /// <summary>
    /// 파티클 프리팹을 만든다. 성공하면 만들어진 프리팹 에셋을 돌려준다.
    ///
    /// 메뉴를 따로 안 단 이유: 시트 3장과 <b>같은 시간표를 공유</b>하는 물건이라 따로 만들
    /// 일이 없다. 하나만 다시 만들면 보스 프리팹 안의 구성이 반쪽만 갱신된다.
    /// </summary>
    public static GameObject Build()
    {
        var root = new GameObject("BossTransitionAsh");

        try
        {
            // 수정(재가 알이 되게) — 루트는 <b>빈 컨테이너</b>다.
            //
            // 예전에는 루트 자신이 장막 파티클이었다. 그러면 장막을 끄는 순간 자식인
            // 파편까지 같이 꺼져서, <b>2.375의 버스트가 영영 안 터진다.</b> 장막만 따로
            // 끄려면 장막도 자식이어야 한다.
            BuildVeil(root);
            BuildShards(root);
            BuildForceField(root);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (!ok)
            {
                Debug.LogError($"[보스 전환] 파티클 프리팹 저장에 실패했다: {PrefabPath}");
                return null;
            }

            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// 재 장막 — 화면 전체에 재를 뿌리고, 0.875초에 <b>스스로</b> 방출을 멈춘다.
    ///
    /// 멈추는 일을 코드가 아니라 곡선이 하는 것이 이 함수의 요점이다. 코드로 껐다면
    /// "언제 끄는가"가 스크립트에, "얼마나 오래 도는가"가 인스펙터에 나뉘어 적힌다.
    /// </summary>
    private static void BuildVeil(GameObject root)
    {
        var child = new GameObject("Veil");
        child.transform.SetParent(root.transform, false);

        var particles = child.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.duration = TotalSeconds;
        main.loop = false;

        // 오브젝트가 켜지는 순간 재생한다. 켜는 시각은 타임라인의 Activation Track이 정하므로
        // 여기서 재생 시점을 따로 잡을 필요가 없다 — 켜짐 = 시작이다.
        main.playOnAwake = true;

        // 재가 화면에 오래 떠 있어야 0.875초에 빨려들 <b>대상</b>이 남는다.
        // 수명이 짧으면 힘의 장을 켜는 순간 빨아들일 재가 이미 다 사라지고 없다.
        // 수정(재가 알이 되게) — 최소 수명이 <b>1.625초보다 넉넉히 길어야</b> 한다.
        //
        // 0초에 태어난 재가 알이 서기 전에 수명으로 죽으면, 그 재는 <b>모이는 도중에
        // 사라진다.</b> 몇 개만 그래도 "모여서 알이 됐다"가 깨진다. 2.4면 0초에 태어난
        // 것도 1.625를 0.8초 남기고 넘긴다.
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);

        // 크기. 화면(50×28유닛)을 덮을 만큼 넓게 뿌리므로 조각 하나가 너무 작으면
        // 먼지처럼 흩어져서 <b>장막으로 안 읽힌다.</b> 보스 그림 높이(6.25유닛)의
        // 1/10 안쪽에서 잡는다.
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.62f);

        // 각진 조각이라 도는 것이 보인다. 안 돌리면 같은 방향의 사각형이 깔려서 격자처럼 보인다.
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);

        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.55f, 0.18f, 1f),   // 아직 타는 재
            new Color(0.45f, 0.42f, 0.40f, 1f)); // 식은 재

        // 월드 공간이라야 보스가 밀려나도 이미 뿌려진 재가 따라 움직이지 않는다.
        // 전환 중 보스는 안 움직이지만, 힘의 장이 재를 끌어당기는 동안 시스템 위치를
        // 기준으로 도는 것과 재가 통째로 끌려다니는 것은 전혀 다르게 보인다.
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // 화면 전체를 덮으려면 이만큼 필요하다. 방출량 900 × 0.875초 ≈ 790개가 동시에 산다.
        main.maxParticles = 1500;

        // 프리팹이 보스의 자식이라 스스로를 지우면 안 된다. 정리는 Activation Track이 한다.
        main.stopAction = ParticleSystemStopAction.None;

        var emission = particles.emission;
        emission.enabled = true;

        // 수정 — 140에서 900으로. 140이면 0.875초 동안 겨우 120개가 나오는데, 그걸
        // 화면(50×28유닛)에 흩으면 <b>1600제곱유닛에 120개</b>다. 장막이 아니라 티끌이다.
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(900f, VeilRateCurve());

        var shape = particles.shape;
        shape.enabled = true;

        // 수정 — 구 껍질에서 상자 전체로. 이유는 VeilBox 주석에 적어뒀다.
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = VeilBox;

        // z를 0으로 눌러둔다. 구 모양일 때는 재가 화면 안쪽으로도 퍼져서, 직교 카메라라
        // 위치는 안 밀리지만 <b>같은 레이어 안의 앞뒤 다툼</b>이 프레임마다 달라졌다.
        shape.position = Vector3.zero;

        // 수정(재가 알이 되게) — <b>꼬리를 빼며 사라지지 않는다.</b>
        //
        // 예전에는 수명 끝에서 알파가 0으로 내려갔다. 그래서 화면에서는 재가 발밑으로
        // 모이는 <b>도중에</b> 하나씩 옅어져 없어졌고, 알은 그것과 상관없이 따로 나타났다.
        // "재가 모여서 알이 됐다"가 아니라 "재가 사라지고 알이 생겼다"로 읽힌 이유다.
        //
        // 이제 재는 끝까지 불투명하게 살아 있다가 <b>알이 서는 1.625초에 한꺼번에</b>
        // 사라진다. 그 순간을 정하는 것은 타임라인의 "재 장막" 트랙이고, 같은 자리에
        // 알 시트가 켜지므로 사라지는 장면이 알에 가려진다 — 그게 이어 붙는 지점이다.
        //
        // 나타날 때만 다듬는 이유는 그대로다. 재가 허공에서 갑자기 생기면 그게 제일 티가 난다.
        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(VeilGradient());

        var rotationOverLifetime = particles.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(
            -120f * Mathf.Deg2Rad, 120f * Mathf.Deg2Rad);

        // 힘의 장을 받게 한다. 이 스위치를 안 켜면 ForceField가 아무리 켜져 있어도
        // 이 시스템은 <b>아무 영향도 안 받는다</b> — 에러 없이 그냥 안 빨려든다.
        var externalForces = particles.externalForces;
        externalForces.enabled = true;
        externalForces.multiplier = 1f;

        // 어떤 힘의 장을 받을지 명시한다. 스위치만 켜고 이 값을 안 잡으면 <b>무엇의
        // 영향을 받을지가 기본값에 맡겨진다.</b> 코드로 만든 파티클 시스템에서는 그
        // 기본값을 눈으로 확인할 방법이 없고, 비어 있으면 힘의 장이 아무리 켜져 있어도
        // 에러 없이 그냥 안 빨려든다.
        externalForces.influenceFilter = ParticleSystemGameObjectFilter.LayerMask;
        externalForces.influenceMask = ~0;

        // 장막은 시트(10)와 파편(20)보다 뒤다. 안개가 앞에 오면 알이 안 보인다.
        ApplyRenderer(child, sortingOrder: 0);
    }

    /// <summary>
    /// 파편 버스트 — 2.375초에 한 번 터진다. 자식으로 두는 이유는 <c>Play</c>가 자식까지
    /// 같이 재생하기 때문이다. 부모가 켜지면 이것도 같은 시각에 시작하므로,
    /// 버스트 시각 2.375를 <b>연출 시작 기준</b>으로 그대로 적을 수 있다.
    /// </summary>
    private static void BuildShards(GameObject root)
    {
        var child = new GameObject("Shards");
        child.transform.SetParent(root.transform, false);

        var particles = child.AddComponent<ParticleSystem>();

        var main = particles.main;
        main.duration = TotalSeconds;
        main.loop = false;
        main.playOnAwake = true;

        // 수명을 0.7초 위로 안 올리는 이유: 버스트가 2.375초에 터지는데 연출은 3.125초에
        // 끝나고, 그때 타임라인이 이 오브젝트를 끈다. 수명이 0.75초를 넘으면 <b>아직 날아가는
        // 중인 파편이 허공에서 통째로 사라진다.</b> 남은 시간(0.75초)이 수명의 상한이다.
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);

        // 껍질이 터져 나가는 속도. 알(보스 몸집 6.25유닛)을 한 번에 벗어나야 "깨졌다"가 된다.
        main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.72f, 0.30f, 1f),
            new Color(0.85f, 0.35f, 0.12f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        main.stopAction = ParticleSystemStopAction.None;

        // 조금 떨어지게 한다. 파편이 직선으로만 날면 폭죽처럼 보이고 무게가 안 실린다.
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.35f);

        var emission = particles.emission;
        emission.enabled = true;

        // 상시 방출은 없다. 이 시스템의 존재 이유는 <b>한 순간</b>이다.
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(ShatterSeconds, 90),
        });

        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;

        // 알 크기에서 터진다.
        //
        // 수정 — 2.6 → 5.5 → 7.5. 시트 배율이 1에서 3으로 커졌으니
        // 파편이 나오는 자리도 같이 커져야 한다. 안 그러면 <b>알 한가운데의 좁은 점에서만</b>
        // 파편이 나와서, 깨진 것이 아니라 안에서 뭔가 튀어나온 것으로 보인다.
        shape.radius = 7.5f;
        shape.radiusThickness = 1f;

        var colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FadeGradient());

        var rotationOverLifetime = particles.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(
            -360f * Mathf.Deg2Rad, 360f * Mathf.Deg2Rad);

        // 파편은 껍질 조각이라 <b>전부의 앞</b>에 그린다. 시트(10)보다도 앞이라야
        // 껍질이 알에서 튀어나온 것으로 읽힌다.
        ApplyRenderer(child, sortingOrder: 20);
    }

    /// <summary>
    /// 힘의 장 — 계획표의 "0.875 힘의 장 ON"이 이 오브젝트를 켜는 일이 된다.
    /// <b>꺼진 채로 만들어진다.</b> 켜는 시각은 타임라인의 Activation Track이 소유한다.
    ///
    /// <b>X로 90도 눕히는 것이 이 함수에서 제일 중요한 줄이다.</b>
    /// <see cref="ParticleSystemForceField"/>의 회전은 <b>자기 로컬 Y축</b>을 중심으로 돈다.
    /// 그대로 두면 XY 평면을 내려다보는 이 게임에서는 소용돌이가 <b>화면 안쪽으로 누워서</b>
    /// 재가 좌우로 왔다 갔다 하는 것처럼만 보인다. 90도 눕혀 Y축을 월드 Z와 맞추면
    /// 화면에서 도는 소용돌이가 된다.
    ///
    /// 2026-09-01 기록의 "소용돌이 축은 Orbital Y가 아니라 Orbital Z"가 힘의 장에서는
    /// 이 회전값으로 나타난다. 모듈이 달라도 함정은 같은 것이다.
    /// </summary>
    private static void BuildForceField(GameObject root)
    {
        var child = new GameObject("AshField");
        child.transform.SetParent(root.transform, false);
        child.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var field = child.AddComponent<ParticleSystemForceField>();
        field.shape = ParticleSystemForceFieldShape.Sphere;

        field.endRange = FieldRange;

        // 중심으로 끌어당기는 힘.
        //
        // 수정(재가 알이 되게) — 26 → 55. <b>도착 시각이 곧 알이 서는 시각이어야 한다.</b>
        //
        // 재는 0.875에 끌리기 시작해 1.625에 알이 선다. 주어진 시간은 0.75초뿐인데
        // 화면 구석의 재는 30유닛 넘게 떨어져 있다. 정지 상태에서 그 거리를 그 시간에
        // 가려면 가속도가 2 × 30 ÷ 0.75² ≈ 107은 되어야 한다 — 다만 소용돌이가 경로를
        // 늘리고 도중에 속도가 붙으므로 그만큼은 필요 없다. 55에서 시작해 눈으로 맞춘다.
        //
        // <b>여기가 이 연출에서 제일 예민한 값이다.</b> 약하면 알이 선 뒤에도 재가
        // 계속 흘러들어와 "이미 다 모였다"가 안 되고, 세면 재가 중심을 지나쳐
        // 반대편으로 튄다.
        field.gravity = new ParticleSystem.MinMaxCurve(55f);

        // 수정 2회차 — <b>0으로 둔다. 앞의 두 값이 둘 다 반대 방향이었다.</b>
        //
        // 이 값을 "얼마나 한 점에 모으느냐"로 잘못 알고 있었다. 실제 뜻은 <b>중력이 향하는
        // 지점</b>이다 — 0이면 힘의 장 <b>중심</b>, 1이면 <b>표면</b>이다. 1은 "가장 세게
        // 모은다"가 아니라 <b>바깥 껍데기 쪽으로 민다</b>는 뜻이었다.
        //
        // 그래서 0.85도 1도 재를 밖으로 밀고 있었다. 화면에서 재가 발밑으로 모이는 대신
        // 흩어진 이유이고, 그 전에 "재가 왼쪽으로 쏠린다"고 봤던 것도 같은 원인이다.
        // 끄는 힘(gravity)을 26에서 55로 올린 것은 <b>퍼지는 속도만 올린 셈</b>이었다.
        //
        // 0이면 중심 한 점을 향한다. 이 연출이 처음부터 원하던 것이다 — 재가 발밑에
        // 모여 알이 서는 자리를 채운다.
        field.gravityFocus = 0f;

        // 소용돌이. 끌어당기기만 하면 재가 직선으로 떨어져서 빨려드는 게 아니라 쏟아진다.
        //
        // 끄는 힘을 55로 올리면서 같이 올렸다. 소용돌이가 약하면 <b>재가 중심을 그대로
        // 통과해 반대편으로 튄다.</b> 돌면서 들어와야 중심 근처에서 속도가 꺾인다.
        field.rotationSpeed = new ParticleSystem.MinMaxCurve(240f);
        field.rotationAttraction = new ParticleSystem.MinMaxCurve(0.8f);

        // 꺼진 채로 시작한다. 0.875초에 Activation Track이 켠다.
        child.SetActive(false);
    }

    /// <summary>
    /// 방출량 곡선. duration 전체(0~1)를 가로축으로 훑는다.
    /// 0.875초에서 <b>수직으로</b> 0으로 떨어진다.
    ///
    /// 서서히 줄이지 않는 이유: 계획표에서 이 시각은 "방출 정지 + 힘의 장 ON"이 동시에
    /// 일어나는 <b>전환점</b>이다. 방출이 꼬리를 남기면 빨려드는 재 사이로 새 재가 계속
    /// 태어나서, 무엇이 빨려드는 중인지가 안 읽힌다.
    /// </summary>
    private static AnimationCurve VeilRateCurve()
    {
        float stop = VeilStopSeconds / TotalSeconds;

        var curve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(stop, 1f),
            // 한 프레임(1/60초)만 뒤에 0을 찍어 계단처럼 만든다. 같은 시각에 두 키를 두면
            // 어느 쪽이 이기는지가 보장되지 않는다.
            new Keyframe(stop + (1f / 60f) / TotalSeconds, 0f),
            new Keyframe(1f, 0f));

        // 보간을 계단으로 고정한다. 기본 보간(부드러움)이면 0으로 떨어지기 전에
        // 곡선이 1을 <b>넘어서</b> 방출량이 잠깐 튀어 오른다.
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
        }

        return curve;
    }

    /// <summary>
    /// 추가 생성 — 장막 전용 투명도. <b>나타날 때만 다듬고 끝은 안 다듬는다.</b>
    ///
    /// 파편(<see cref="FadeGradient"/>)과 나눈 이유: 파편은 자기 수명대로 사그라들어야
    /// 맞다. 흩어져 식는 것이 파편이 하는 일이다. 반대로 장막은 <b>사라지면 안 된다</b> —
    /// 모여서 알이 되어야 하므로, 끝을 정하는 것은 수명이 아니라 알이 서는 시각이다.
    /// </summary>
    private static Gradient VeilGradient()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.12f),
                new GradientAlphaKey(1f, 1f),
            });

        return gradient;
    }

    /// <summary>
    /// 수명에 걸친 투명도. 나타날 때와 사라질 때만 다듬고 가운데는 불투명하게 둔다.
    /// 재가 갑자기 생기고 갑자기 없어지는 것이 제일 티가 난다.
    /// </summary>
    private static Gradient FadeGradient()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f),
            });

        return gradient;
    }

    /// <summary>
    /// 렌더러 설정. 타이틀 화면 파티클과 같은 재료(<c>Sprites-Default</c>)를 쓴다.
    ///
    /// <b>텍스처를 안 넣는 것이 의도다.</b> 2026-09-01 기록에 "파티클 텍스처는 매끈한
    /// 글로우가 아니라 재 조각"이라고 적어뒀는데, 그 경고가 가리키는 것은 유니티 <b>기본</b>
    /// 파티클 머티리얼이다 — 가운데가 밝고 가장자리가 흐린 동그란 빛이라, 여러 장 겹치면
    /// 재가 아니라 안개가 된다. <c>Sprites-Default</c>는 텍스처가 없으면 <b>각진 사각형</b>을
    /// 그리고, 여기에 회전을 주면 그것이 곧 재 조각이다.
    ///
    /// 전환 시트를 텍스처로 쓰는 방법도 생각했지만 시트 한 칸은 <b>이펙트 한 프레임 전체</b>지
    /// 조각 하나가 아니다. 그걸 파티클에 물리면 알 그림이 화면에 수백 개 뜬다.
    /// </summary>
    private static void ApplyRenderer(GameObject target, int sortingOrder)
    {
        var renderer = target.GetComponent<ParticleSystemRenderer>();
        renderer.material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        renderer.sortingLayerName = "VFX";
        renderer.sortingOrder = sortingOrder;
    }
}
