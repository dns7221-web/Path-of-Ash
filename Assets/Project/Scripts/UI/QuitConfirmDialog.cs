using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 추가 생성(2026-09-27) — "게임을 종료하시겠습니까?" 확인 창. 타이틀 설정 창 안에 겹쳐 뜬다.
///
/// 씬에 미리 만들어 두지 않고, 설정 창의 [게임 나가기]를 처음 누를 때 코드로 만든다(사용자 결정 — 빌더를 다시 돌리지 않아도 되게).
/// 모양은 설정 창의 닫기 버튼과 그 글자를 <b>복제</b>해서 맞춘다. 버튼 색·눌림 색·글꼴을 여기에 다시 적지 않아도 설정 창과
/// 똑같아지고, 나중에 빌더에서 버튼 모양을 바꾸면 이 창도 같이 바뀐다. 판(패널) 색만은 복제할 원본이 마땅치 않아
/// 빌더 색표와 같은 값을 적어 둔다(SettingsScreen의 탭 색과 같은 사정).
///
/// <b>PauseGate 화면으로 따로 올리지 않는 이유.</b> 설정 창 안의 한 부분이라서다. 화면을 하나 더 쌓으면 ESC가 어느 쪽을 닫을지
/// 스택으로 따져야 하는데, 이 창은 설정 창이 ESC를 받을 때 "확인 창이 떠 있으면 그것부터 닫는다" 한 줄로 충분하다.
/// </summary>
[DisallowMultipleComponent]
public class QuitConfirmDialog : MonoBehaviour
{
    // 빌더(AshSettingsUiBuilder) 색표와 같은 값. 이 창은 실행 중에 만들어서 여기에도 있어야 한다.
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
    private static readonly Color PanelColor = new Color(0.10f, 0.09f, 0.09f, 0.97f);
    private static readonly Color BorderColor = new Color(0.55f, 0.26f, 0.10f, 1f);

    private const float PanelWidth = 640f;   // 캔버스 기준 해상도(1920x1080) 단위
    private const float PanelHeight = 260f;
    private const float MessageFontSize = 30f;

    private Action onConfirm;

    /// <summary>지금 떠 있는가. 설정 창이 ESC를 확인 창과 자기 중 누구에게 줄지 정할 때 쓴다.</summary>
    public bool IsOpen => gameObject.activeSelf;

    /// <summary>
    /// 확인 창을 만든다. 처음에는 숨겨 두고, <see cref="Show"/>로 띄운다.
    /// </summary>
    /// <param name="parent">겹쳐 뜰 곳. 설정 창의 root(창을 열고 닫을 때 같이 켜지고 꺼지는 쪽)라서, 설정 창을 닫으면 같이 사라진다.</param>
    /// <param name="buttonTemplate">모양을 복제할 버튼(설정 창의 닫기).</param>
    /// <param name="message">물어볼 문구.</param>
    /// <param name="confirmLabel">확인 버튼 글자.</param>
    /// <param name="cancelLabel">취소 버튼 글자.</param>
    /// <param name="onConfirm">확인을 눌렀을 때 할 일.</param>
    public static QuitConfirmDialog Create(RectTransform parent, Button buttonTemplate, string message,
                                           string confirmLabel, string cancelLabel, Action onConfirm)
    {
        // 1) 뒤를 어둡게 덮는 막. Image는 기본으로 클릭을 받아서(raycastTarget) 뒤에 있는 설정 창 버튼이 눌리지 않는다.
        RectTransform root = NewRect("QuitConfirmDialog", parent);
        Stretch(root);
        root.gameObject.AddComponent<Image>().color = DimColor;

        // 2) 가운데 판: 테두리색 바탕 + 4px 안쪽의 어두운 판. 빌더의 CreatePanel과 같은 구조라 설정 창과 같은 모양이 된다.
        RectTransform border = NewRect("Panel", root);
        border.anchorMin = border.anchorMax = border.pivot = new Vector2(0.5f, 0.5f);
        border.anchoredPosition = Vector2.zero;
        border.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        border.gameObject.AddComponent<Image>().color = BorderColor;

        RectTransform inner = NewRect("Inner", border);
        Stretch(inner);
        inner.offsetMin = new Vector2(4f, 4f);
        inner.offsetMax = new Vector2(-4f, -4f);
        inner.gameObject.AddComponent<Image>().color = PanelColor;

        // 3) 문구: 버튼 글자(TMP)를 복제해 한글 글꼴·색·정렬을 그대로 쓴다. 판의 위쪽.
        TMPro.TMP_Text templateLabel = buttonTemplate.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (templateLabel != null)
        {
            TMPro.TMP_Text text = Instantiate(templateLabel.gameObject, inner).GetComponent<TMPro.TMP_Text>();
            text.name = "Message";
            SetSlot((RectTransform)text.transform, 0.06f, 0.94f, 0.42f, 0.92f);
            text.text = message;
            text.fontSize = MessageFontSize;
        }

        var dialog = root.gameObject.AddComponent<QuitConfirmDialog>();
        dialog.onConfirm = onConfirm;

        // 4) 버튼 둘: 닫기 버튼을 복제한다. 확인은 왼쪽, 취소는 오른쪽 — 설정 창 아래 줄의 [게임 나가기][닫기]와 같은 쪽이라
        // 방금 누른 자리에 확인이 온다. 복제본에는 닫기의 동작이 따라오지 않는다(코드로 붙인 리스너는 저장되지 않아 복제되지 않는다).
        Button confirm = CloneButton(buttonTemplate, inner, "ConfirmButton", confirmLabel, 0.10f, 0.46f);
        confirm.onClick.AddListener(dialog.Confirm);

        Button cancel = CloneButton(buttonTemplate, inner, "CancelButton", cancelLabel, 0.54f, 0.90f);
        cancel.onClick.AddListener(dialog.Hide);

        root.gameObject.SetActive(false);
        return dialog;
    }

    /// <summary>확인 창을 띄운다. 설정 창의 다른 요소들보다 위에 그리도록 형제 중 맨 뒤로 보낸다(캔버스는 뒤에 있는 것을 나중에 그린다).</summary>
    public void Show()
    {
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
    }

    /// <summary>확인 창을 닫는다(취소 버튼, ESC, 설정 창 닫기).</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>확인을 눌렀다. 창을 닫고 맡겨 둔 일을 한다.</summary>
    private void Confirm()
    {
        Hide();
        onConfirm?.Invoke();
    }

    /// <summary>버튼을 복제해 판 아래쪽의 가로 자리(xMin~xMax)에 놓고 글자를 바꾼다.</summary>
    private static Button CloneButton(Button template, RectTransform parent, string name, string label, float xMin, float xMax)
    {
        GameObject copy = Instantiate(template.gameObject, parent);
        copy.name = name;
        copy.SetActive(true);
        SetSlot((RectTransform)copy.transform, xMin, xMax, 0.10f, 0.34f);

        TMPro.TMP_Text text = copy.GetComponentInChildren<TMPro.TMP_Text>(true);
        if (text != null) text.text = label;

        return copy.GetComponent<Button>();
    }

    /// <summary>RectTransform이 달린 빈 UI 오브젝트를 만든다.</summary>
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    /// <summary>부모를 꽉 채운다(앵커 0~1, 여백 0).</summary>
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>부모 안의 비율 자리(가로 xMin~xMax, 세로 yMin~yMax)를 채운다.</summary>
    private static void SetSlot(RectTransform rect, float xMin, float xMax, float yMin, float yMax)
    {
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
