using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 유물을 먹었을 때 화면에 잠깐 뜨는 획득 알림.
///
/// 인벤토리보다 이걸 먼저 만든 이유: 지금까지 유물은 효과가 들어가는데 화면에 아무 신호가
/// 없었다. <b>먹었는데 먹은 줄 모르는 상태</b>다. 로그라이크에서 보상을 받았다는 피드백이
/// 없으면 상자를 여는 행위 자체가 밋밋해진다.
///
/// 인벤토리는 "지난 것을 다시 확인하는" 화면이고 이건 "지금 일어난 일을 알리는" 장치라,
/// 순서상 이쪽이 먼저다. 유물이 세 종류뿐인 지금은 이것만으로 충분할 수도 있다.
///
/// 게임을 멈추지 않는다. 전투 중에 상자를 열 수도 있는데 그때마다 화면이 서면 흐름이 끊긴다.
/// </summary>
[DisallowMultipleComponent]
public class RelicToast : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;

    [Header("시간(초)")]
    [SerializeField, Min(0.05f)] private float fadeIn = 0.25f;
    [SerializeField, Min(0f)] private float hold = 2.5f;
    [SerializeField, Min(0.05f)] private float fadeOut = 0.6f;

    private RelicInventory inventory;
    private Coroutine playing;

    private void Awake()
    {
        // 플레이어가 프리팹 인스턴스라 HUD가 미리 참조를 걸어둘 수 없다.
        // 꺼져 있을 수도 있으니 Include로 찾는다 — 이걸 빼서 상자 유물이 조용히 사라진 적이 있다.
        inventory = FindFirstObjectByType<RelicInventory>(FindObjectsInactive.Include);

        if (inventory == null)
            Debug.LogWarning("[유물 알림] RelicInventory를 못 찾았다. 알림이 뜨지 않는다.", this);

        if (group != null) group.alpha = 0f;
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.Gained += OnRelicGained;
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Gained -= OnRelicGained;
    }

    private void OnRelicGained(RelicData relic)
    {
        if (relic == null) return;

        if (nameText != null) nameText.text = relic.DisplayName;
        if (descriptionText != null) descriptionText.text = relic.Description;

        if (iconImage != null)
        {
            // 아이콘 그림이 아직 없어도 알림은 떠야 한다. 이름과 설명만으로도
            // "무엇을 얻었는가"는 전달된다.
            iconImage.sprite = relic.Icon;
            iconImage.enabled = relic.Icon != null;
        }

        // 연달아 먹으면 이전 알림을 끊고 새로 띄운다. 겹쳐 쌓으면 마지막 것이
        // 언제 사라질지 알 수 없고, 화면에 여러 장이 남는다.
        if (playing != null) StopCoroutine(playing);
        playing = StartCoroutine(Show());
    }

    private IEnumerator Show()
    {
        yield return Fade(0f, 1f, fadeIn);
        yield return new WaitForSeconds(hold);
        yield return Fade(1f, 0f, fadeOut);

        playing = null;
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        if (group == null) yield break;

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / seconds));
            yield return null;
        }

        group.alpha = to;
    }
}
