using UnityEngine;

/// <summary>
/// 타이틀 배경을 아주 느리게 확대하고 좌우로 흘린다. 정지 이미지를 살리는 고전 기법(켄 번스).
///
/// 배경 스프라이트를 직접 움직이지 않고 <b>카메라</b>를 움직이는 이유:
/// UI는 Screen Space - Overlay라 카메라의 영향을 전혀 받지 않는다. 그래서 카메라만 건드리면
/// 배경과 재 파티클은 같이 움직이고 제목·안내 문구는 제자리에 고정된다. 배경 오브젝트를
/// 직접 움직였다면 파티클을 따로 맞춰야 했다.
///
/// 줌 주기와 패닝 주기를 서로 다른 값(24초 / 31초)으로 둔 게 중요하다. 같은 주기면 둘이
/// 항상 같은 지점에서 만나서 "아, 반복되는구나"가 보인다. 서로 나누어떨어지지 않는 주기를
/// 쓰면 두 움직임이 겹치는 패턴이 한참 동안 반복되지 않아 계속 살아있는 것처럼 보인다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class TitleCameraDrift : MonoBehaviour
{
    [Header("확대")]
    [Tooltip("얼마나 확대할지. 0.04면 최대 4% 당겨진다. 크게 주면 배경 가장자리가 드러난다.")]
    [SerializeField, Range(0f, 0.15f)] private float zoomAmount = 0.04f;

    [Tooltip("확대가 한 바퀴 도는 데 걸리는 시간(초).")]
    [SerializeField] private float zoomCycleSeconds = 24f;

    [Header("좌우 흘림")]
    [Tooltip("좌우로 움직이는 거리(월드 유닛). 배경 여유분(약 0.45)보다 작아야 검은 가장자리가 안 보인다.")]
    [SerializeField, Range(0f, 1f)] private float panDistance = 0.25f;

    [Tooltip("좌우 왕복 주기(초). 확대 주기와 서로 나누어떨어지지 않는 값이어야 한다.")]
    [SerializeField] private float panCycleSeconds = 31f;

    // 시작 시점의 값을 기준으로 삼는다. 인스펙터에서 카메라 크기를 바꾸면 그게 그대로 기준이 된다.
    private Camera cam;
    private float baseSize;
    private Vector3 basePosition;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        baseSize = cam.orthographicSize;
        basePosition = transform.position;
    }

    private void LateUpdate()
    {
        // LateUpdate에서 도는 이유: 나중에 다른 것이 카메라를 건드리더라도 이 연출이 마지막에
        // 덮어쓰도록 한다. 타이틀에서는 카메라를 두고 다툴 상대가 없지만, Update에 두면
        // 실행 순서에 따라 결과가 달라질 수 있어서 습관적으로 여기 둔다.

        // 0~1을 오가는 값 두 개. Time.unscaledTime을 쓰는 건 타이틀이 timeScale과 무관해야
        // 하기 때문이다.
        float zoomT = Wave(zoomCycleSeconds);
        float panT = Wave(panCycleSeconds);

        // orthographicSize가 작아질수록 화면에 담기는 범위가 좁아진다 = 확대된다.
        cam.orthographicSize = baseSize * (1f - zoomAmount * zoomT);

        // panT는 0~1이므로 -1~1로 옮겨서 좌우 양쪽으로 흔든다.
        float offsetX = (panT * 2f - 1f) * panDistance;
        transform.position = new Vector3(basePosition.x + offsetX, basePosition.y, basePosition.z);
    }

    /// <summary>지정한 주기로 0 → 1 → 0을 부드럽게 오가는 값을 만든다.</summary>
    private float Wave(float cycleSeconds)
    {
        float phase = Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.01f, cycleSeconds);
        return (Mathf.Sin(phase) + 1f) * 0.5f;
    }
}
