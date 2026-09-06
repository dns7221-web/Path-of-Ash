using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 추가 생성 — 조작 키를 문장 안에 끼워 보여주는 글자. 키가 바뀌면 스스로 다시 쓴다.
///
/// <b>왜 필요한가.</b> 튜토리얼 안내나 결과 화면 문구는 "Ctrl - 대시"처럼 키를 글자로
/// 적어둔다. 키를 바꿀 수 있게 된 순간 그 글자는 전부 거짓말이 될 수 있다. 실제로
/// 튜토리얼은 키를 바꿀 수 없던 시절에도 이미 어긋나 있었다 — 대시가 Shift로 옮겨갔는데
/// 안내는 Ctrl 그대로였다. <b>사람이 지키기로 한 규칙은 지켜지지 않는다.</b>
///
/// 그래서 문장에는 키 대신 자리표시자를 적어두고, 실제 키는 실행 중에 채워 넣는다.
/// 예: <c>"{Dash} - 대시 (회피)"</c> → <c>"Left Shift - 대시 (회피)"</c>
///
/// 자리표시자 이름은 <see cref="InputBindings"/>의 액션 이름이다. 오타를 내면 그 자리에
/// <c>-</c>가 찍히고 콘솔에 에러가 남는다 — 조용히 틀린 키를 보여주는 것보다 낫다.
/// </summary>
[DisallowMultipleComponent]
public class ControlHintLabel : MonoBehaviour
{
    [Tooltip("보여줄 문장. {Dash}처럼 중괄호 안에 액션 이름을 적으면 실제 키로 바뀐다.")]
    [SerializeField, TextArea] private string template;

    [Tooltip("긴 이름 대신 짧은 표기를 쓴다. 예: 'Left Ctrl' 대신 'Ctrl'. 칸이 좁을 때 켠다.")]
    [SerializeField] private bool useShortKeys;

    private TMP_Text label;

    /// <summary>문장을 바꾼다. 에디터 도구가 만들 때 쓴다.</summary>
    public void SetTemplate(string value)
    {
        template = value;
        Refresh();
    }

    private void Awake()
    {
        // TMP_Text는 UI용(TextMeshProUGUI)과 월드용(TextMeshPro)의 공통 부모다.
        // 튜토리얼 안내는 방 바닥에 놓인 월드 글자라 이 타입으로 받아야 둘 다 된다.
        label = GetComponent<TMP_Text>();

        if (label == null)
            Debug.LogError("[조작 안내] 같은 오브젝트에 TMP_Text가 없다.", this);
    }

    private void OnEnable()
    {
        // 켜질 때마다 다시 쓴다. 꺼져 있는 동안 키가 바뀌었을 수 있다.
        InputBindings.BindingsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        InputBindings.BindingsChanged -= Refresh;
    }

    /// <summary>자리표시자를 지금 키로 바꿔 넣는다.</summary>
    private void Refresh()
    {
        if (label == null || string.IsNullOrEmpty(template)) return;

        label.text = Fill(template, useShortKeys);
    }

    /// <summary>
    /// 문장의 {액션이름}을 실제 키로 바꾼다.
    ///
    /// string.Format을 안 쓰는 이유: 그쪽은 자리표시자가 번호({0})라 문장을 읽어서는
    /// 몇 번이 무슨 키인지 알 수 없다. 이름을 그대로 쓰면 문장만 봐도 뜻이 통한다.
    /// </summary>
    public static string Fill(string template, bool useShortKeys = false)
    {
        if (string.IsNullOrEmpty(template)) return template;

        var builder = new StringBuilder(template.Length + 16);
        int cursor = 0;

        while (cursor < template.Length)
        {
            int open = template.IndexOf('{', cursor);
            if (open < 0) break;

            int close = template.IndexOf('}', open + 1);
            if (close < 0) break;

            builder.Append(template, cursor, open - cursor);

            string id = template.Substring(open + 1, close - open - 1);
            builder.Append(useShortKeys
                ? InputBindings.ShortKeyTextFor(id)
                : InputBindings.KeyboardTextFor(id));

            cursor = close + 1;
        }

        builder.Append(template, cursor, template.Length - cursor);
        return builder.ToString();
    }
}
