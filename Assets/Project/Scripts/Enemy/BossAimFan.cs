using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-20, 재의 창) — 보스가 겨누는 <b>부채꼴 조준선</b>.
///
/// 재의 창은 창을 먼저 띄워두고 잠시 겨눈 뒤에 나간다. 그동안 어디로 나갈지 보이지 않으면
/// 플레이어가 할 수 있는 일이 "일단 멀리 도망친다" 하나뿐이라, 패턴이 읽히지 않는다.
/// 조준선이 보이면 <b>선 사이로 파고드는</b> 회피가 생긴다 — 잿불 파도의 부채꼴과 같은
/// 모양이지만, 파도는 발사 순간에야 방향이 정해지고 이쪽은 미리 보여준다는 것이 다르다.
///
/// <b>선은 <see cref="LineRenderer"/>로 그린다.</b> 직접 스프라이트를 늘려 붙이는 방법도 있지만,
/// 그러면 길이·굵기·색이 전부 손으로 맞춘 숫자가 되고 각도마다 늘어나는 정도가 달라진다.
/// LineRenderer는 두 점만 주면 굵기와 색 그라데이션을 알아서 유지한다.
///
/// <b>왜 보스의 자식이 아니라 따로 선 오브젝트인가.</b> 보스 프리팹은 크기 배율이 1이 아니다.
/// 자식으로 달면 그 배율이 선 굵기와 길이에 그대로 곱해져서, 보스 크기를 조금만 바꿔도
/// 조준선이 엉뚱하게 굵어지거나 짧아진다. 위치는 어차피 매 프레임 <see cref="Aim"/>에서
/// 다시 잡으므로 부모가 필요 없다.
/// </summary>
[DisallowMultipleComponent]
public class BossAimFan : MonoBehaviour
{
    [Header("모양")]
    [Tooltip("보스 쪽 끝의 굵기(유닛).")]
    [SerializeField, Min(0.01f)] private float startWidth = 0.22f;

    [Tooltip("먼 쪽 끝의 굵기(유닛). 끝으로 갈수록 가늘어야 '겨눈 선'으로 읽힌다.")]
    [SerializeField, Min(0.01f)] private float endWidth = 0.06f;

    [Tooltip("보스 쪽 색. 알파가 곧 진하기다.")]
    [SerializeField] private Color nearColor = new Color(1f, 0.69f, 0f, 0.85f);

    [Tooltip("먼 쪽 색. 알파를 0으로 두면 선이 허공에서 자연스럽게 사라진다.")]
    [SerializeField] private Color farColor = new Color(0.94f, 0.38f, 0f, 0f);

    [Tooltip("그릴 정렬 레이어. 바닥에 깔린 표시라 Decal이 맞다 — 캐릭터 그림을 가리지 않는다.")]
    [SerializeField] private string sortingLayer = "Decal";

    [SerializeField] private int sortingOrder = 5;

    [Header("나타남")]
    [Tooltip("조준선이 0에서 지금 진하기까지 차오르는 시간(초). 0이면 바로 나타난다. " +
             "천천히 켜야 '지금 겨누기 시작했다'가 읽힌다.")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 0.15f;

    // 실제로 그리는 선들. Show가 필요한 만큼 만들고 남는 것은 꺼둔다.
    private readonly System.Collections.Generic.List<LineRenderer> lines = new();

    // 선들을 담아두는 빈 오브젝트. 보스의 자식이 아니라서 따로 들고 있어야 지울 수 있다.
    private Transform root;

    // 런타임에 만든 재질. 씬에 두고 쓰는 물건이 아니라 여기서 만들고 여기서 지운다.
    private Material material;

    // 지금 보여주고 있는 선의 수. 발사할 때 몇 개를 쏘는지와 같은 값이다.
    private int activeCount;

    // 부채꼴 전체 각도 배분과 길이. Aim이 매번 다시 계산하려면 들고 있어야 한다.
    private float spread;
    private float length;
    private float height;

    // 지금 겨누고 있는 방향(정규화). 발사 방향은 이 값에서 나온다.
    private Vector2 aim = Vector2.right;

    // 나타난 정도(0~1). fadeInSeconds 동안 0에서 1로 오른다.
    private float appear;

    private void OnDestroy()
    {
        // 보스가 사라질 때 조준선도 같이 사라져야 한다. 자식이 아니라서 자동으로 안 지워진다 —
        // 놓치면 보스가 죽은 뒤에도 바닥에 선이 남는다.
        if (root != null) Destroy(root.gameObject);
        if (material != null) Destroy(material);
    }

    private void OnDisable()
    {
        Hide();
    }

    /// <summary>
    /// 조준선을 켠다. 개수가 달라지면 모자란 만큼 새로 만들고 남는 것은 끈다.
    /// </summary>
    /// <param name="count">선의 개수. 나갈 창의 수와 같다.</param>
    /// <param name="spreadDegrees">선 사이 각도(도).</param>
    /// <param name="lineLength">선의 길이(유닛). 창이 날아가는 거리에 맞춘다.</param>
    /// <param name="spawnHeight">선이 시작하는 높이(유닛). 창이 나가는 높이와 같아야 한다.</param>
    public void Show(int count, float spreadDegrees, float lineLength, float spawnHeight)
    {
        EnsureRoot();

        activeCount = Mathf.Max(0, count);
        spread = spreadDegrees;
        length = Mathf.Max(0.1f, lineLength);
        height = spawnHeight;
        appear = 0f;

        while (lines.Count < activeCount) lines.Add(CreateLine());

        for (int i = 0; i < lines.Count; i++) lines[i].enabled = i < activeCount;

        Apply();
    }

    /// <summary>
    /// 겨누는 방향을 바꾼다. 시전하는 동안 매 프레임 부른다 — 부채꼴이 통째로 따라 돈다.
    /// </summary>
    public void Aim(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.0001f) aim = direction.normalized;

        // 나타나는 연출도 여기서 같이 민다. 따로 Update를 두면 Show를 부르지 않은 동안에도
        // 계속 돌아야 해서, 꺼져 있는 조준선이 매 프레임 일을 하게 된다.
        appear = fadeInSeconds <= 0f ? 1f : Mathf.MoveTowards(appear, 1f, Time.deltaTime / fadeInSeconds);

        Apply();
    }

    /// <summary>조준선을 끈다. 발사한 뒤나 패턴이 끊겼을 때 부른다.</summary>
    public void Hide()
    {
        activeCount = 0;
        foreach (LineRenderer line in lines)
        {
            if (line != null) line.enabled = false;
        }
    }

    /// <summary>
    /// <paramref name="index"/>번째 선이 지금 가리키는 방향. 발사할 때 이 값을 그대로 쓴다.
    ///
    /// 각도를 보스 쪽에서 다시 계산하지 않고 여기서 꺼내 쓰는 이유: 두 곳에서 같은 식을
    /// 계산하면 한쪽만 고쳤을 때 <b>보이는 선과 날아가는 창이 어긋난다.</b> 그 어긋남은
    /// "조준선이 거짓말을 한다"로 보이므로, 패턴 자체를 못 믿게 만든다.
    /// </summary>
    public Vector2 DirectionAt(int index)
    {
        return Quaternion.Euler(0f, 0f, AngleOf(index)) * aim;
    }

    /// <summary>가운데를 기준으로 좌우 대칭이 되게 나눈 각도.</summary>
    private float AngleOf(int index)
    {
        float start = -spread * (activeCount - 1) * 0.5f;
        return start + spread * index;
    }

    /// <summary>선들의 위치·색을 지금 상태에 맞춘다.</summary>
    private void Apply()
    {
        if (root == null) return;

        Vector3 origin = transform.position + Vector3.up * height;
        root.position = origin;

        for (int i = 0; i < activeCount && i < lines.Count; i++)
        {
            LineRenderer line = lines[i];
            if (line == null) continue;

            Vector3 direction = (Vector3)DirectionAt(i);
            line.SetPosition(0, origin);
            line.SetPosition(1, origin + direction * length);

            // 진하기만 appear로 조절한다. 굵기를 같이 건드리면 얇은 선이 점점 굵어지는
            // 모양이 되어, 겨누는 것이 아니라 커지는 것처럼 보인다.
            line.startColor = Fade(nearColor, appear);
            line.endColor = Fade(farColor, appear);
        }
    }

    private static Color Fade(Color color, float amount)
    {
        color.a *= Mathf.Clamp01(amount);
        return color;
    }

    private void EnsureRoot()
    {
        if (root != null) return;

        var rootObject = new GameObject($"{name}_AimFan");
        root = rootObject.transform;
    }

    /// <summary>선 하나를 만든다. 색과 굵기는 <see cref="Apply"/>가 매번 다시 넣는다.</summary>
    private LineRenderer CreateLine()
    {
        var lineObject = new GameObject($"AimLine{lines.Count}");
        lineObject.transform.SetParent(root, false);

        var line = lineObject.AddComponent<LineRenderer>();

        // 월드 좌표로 두 점을 직접 찍는다. 로컬 좌표를 쓰면 부모가 움직일 때마다 선이
        // 끌려다니는데, 조준선은 보스가 서 있는 자리에 고정돼 있어야 읽기 쉽다.
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.numCapVertices = 2;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.startWidth = startWidth;
        line.endWidth = endWidth;

        // 그림자와 라이트를 끈다. 이 게임의 조명은 2D 라이트라, 켜두면 조준선만 어둡게 눌린다.
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        line.sortingLayerName = sortingLayer;
        line.sortingOrder = sortingOrder;

        line.material = EnsureMaterial();
        return line;
    }

    /// <summary>
    /// 선에 쓸 재질. 파티클과 같은 Sprites/Default를 쓴다 — 색을 정점에서 받아 그대로 그린다.
    /// </summary>
    private Material EnsureMaterial()
    {
        if (material != null) return material;

        Shader shader = Shader.Find("Sprites/Default");
        material = new Material(shader) { name = "BossAimLine (런타임)" };
        return material;
    }
}
