using TMPro;
using UnityEngine;

/// <summary>
/// 텍스트를 천천히 밝아졌다 어두워지게 만든다. "아무 곳이나 클릭" 같은 안내 문구용.
///
/// 켜졌다 꺼졌다 하는 방식(알파 0 ↔ 1)이 아니라 사인파로 부드럽게 오가게 한 이유:
/// 딱딱 끊기는 깜빡임은 90년대 웹페이지처럼 보인다. 최저 알파를 0이 아니라 0.15 정도로
/// 남겨두면 문구가 완전히 사라지지 않아서, 시선을 끌면서도 산만하지 않다.
///
/// Time.unscaledTime을 쓰는 이유: 나중에 일시정지로 timeScale을 0으로 만들어도 이 문구는
/// 계속 깜빡여야 한다. 멈춘 화면에서 UI까지 얼어붙으면 게임이 죽은 것처럼 보인다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class BlinkingText : MonoBehaviour
{
    [Tooltip("깜빡일 대상. 비워두면 같은 오브젝트의 TMP_Text를 자동으로 찾는다.")]
    [SerializeField] private TMP_Text target;

    [Tooltip("한 번 밝아졌다 어두워지는 데 걸리는 시간(초).")]
    [SerializeField] private float cycleSeconds = 1.6f;

    [Tooltip("가장 어두울 때의 알파. 0으로 두면 문구가 완전히 사라진다.")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.15f;

    [Tooltip("가장 밝을 때의 알파.")]
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 1f;

    /// <summary>컴포넌트를 처음 붙였을 때 대상을 자동으로 채워준다(에디터 전용 콜백).</summary>
    private void Reset()
    {
        target = GetComponent<TMP_Text>();
    }

    private void Awake()
    {
        if (target == null) target = GetComponent<TMP_Text>();
    }

    private void Update()
    {
        if (target == null) return;

        // 사인 결과는 -1~1이라 0~1로 옮긴다. cycleSeconds가 한 바퀴 도는 시간이 되도록
        // 2π를 주기로 나눈다.
        float phase = Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.01f, cycleSeconds);
        float t = (Mathf.Sin(phase) + 1f) * 0.5f;

        Color color = target.color;
        color.a = Mathf.Lerp(minAlpha, maxAlpha, t);
        target.color = color;
    }
}
