using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인벤토리의 칸 하나. 보관함 칸과 장착 칸이 같은 것을 쓴다.
///
/// 둘을 나누지 않은 이유: 하는 일이 "유물 그림을 보여주고, 눌리면 알려주고, 올려두면 알려준다"
/// 셋뿐이고 그게 완전히 같다. 다른 것은 <b>눌렸을 때 무엇을 하느냐</b>인데, 그건 이 칸이 아니라
/// 화면(<see cref="InventoryScreen"/>)이 정할 일이다. 그래서 동작을 밖에서 넣어준다(Bind).
/// </summary>
[DisallowMultipleComponent]
public class RelicSlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    [Tooltip("칸 테두리 그림. 비어 있든 아니든 항상 보인다.")]
    [SerializeField] private Image frameImage;

    [Tooltip("유물 그림. 빈 칸이면 꺼진다.")]
    [SerializeField] private Image iconImage;

    private Action onClick;
    private Action onHover;

    /// <summary>지금 이 칸에 든 것. 빈 칸이면 IsEmpty가 true다.</summary>
    public RelicInstance Item { get; private set; } = RelicInstance.None;

    /// <summary>눌렸을 때와 마우스를 올렸을 때 할 일을 정한다.</summary>
    public void Bind(Action click, Action hover)
    {
        onClick = click;
        onHover = hover;
    }

    /// <summary>칸에 유물을 표시한다. 빈 칸이면 <see cref="RelicInstance.None"/>을 넣는다.</summary>
    public void Show(RelicInstance item)
    {
        Item = item;

        if (iconImage == null) return;

        // 아이콘 오브젝트를 끄고 켜는 대신 sprite만 비우면, 흰 사각형이 남는다.
        bool has = !item.IsEmpty && item.Data.Icon != null;
        iconImage.enabled = has;
        if (has) iconImage.sprite = item.Data.Icon;
    }

    public void OnPointerClick(PointerEventData eventData) => onClick?.Invoke();

    public void OnPointerEnter(PointerEventData eventData) => onHover?.Invoke();

    /// <summary>에디터 도구가 만들 때 참조를 넣어준다.</summary>
    public void SetImages(Image frame, Image icon)
    {
        frameImage = frame;
        iconImage = icon;
    }
}
