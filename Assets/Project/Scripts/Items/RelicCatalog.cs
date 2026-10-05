using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-10-05, 이어하기) — 게임에 있는 모든 유물의 목록. 저장 파일의 유물 이름을 실제 에셋으로 되돌릴 때 쓴다.
///
/// <b>왜 필요한가.</b> 저장 파일은 글자일 뿐이라 에셋 참조를 담을 수 없다. 그래서 유물을 이름(<see cref="SavedRelic.id"/>)으로
/// 저장하는데, 실행 중에 "이름 → 에셋"을 찾으려면 모든 유물을 들고 있는 곳이 하나 있어야 한다.
///
/// <b>왜 Resources 폴더에 두는가.</b> 이 목록을 쓰는 <see cref="RunSaveController"/>는 씬에 미리 배치하지 않고 실행 중에 붙는다.
/// 인스펙터로 에셋을 꽂을 자리가 없으니, 유니티 내장 <see cref="Resources.Load{T}(string)"/>로 이름만으로 불러온다.
/// 씬(Game.unity)을 고치지 않아도 되는 것이 이 선택의 이유다. Resources의 단점(안에 든 것이 전부 빌드에 들어감)은
/// 여기선 문제가 없다 — 유물은 어차피 상자 보상으로 전부 빌드에 들어간다.
///
/// 유물을 새로 만들면 <b>Tools → 재의 길 → 프리팹 → 유물 카탈로그 갱신</b>을 누른다. 빠뜨리면 RunSaveTests가 빨갛게 알려 준다.
/// <b>유물 에셋 이름을 바꾸면</b> 예전 저장 파일에서 그 유물을 찾지 못한다(그 칸만 비고 나머지는 이어진다).
/// </summary>
[CreateAssetMenu(fileName = "RelicCatalog", menuName = "재의 길/유물 카탈로그")]
public class RelicCatalog : ScriptableObject
{
    /// <summary>Resources 폴더 안에서의 경로(확장자 없음). Assets/Project/Resources/RelicCatalog.asset</summary>
    public const string ResourcePath = "RelicCatalog";

    [Tooltip("게임에 있는 모든 유물. 메뉴 '유물 카탈로그 갱신'이 채운다.")]
    [SerializeField] private RelicData[] relics = Array.Empty<RelicData>();

    /// <summary>들고 있는 유물 전부.</summary>
    public IReadOnlyList<RelicData> Relics => relics;

    /// <summary>
    /// 이름으로 유물을 찾는다. 없으면 null.
    /// 열다섯 개를 처음부터 훑는다 — 이어하기 때 한 번, 스무 번 안쪽으로만 불려서 사전(Dictionary)을 만들 이유가 없다.
    /// </summary>
    public RelicData Find(string id)
    {
        if (string.IsNullOrEmpty(id) || relics == null) return null;

        foreach (RelicData relic in relics)
            if (relic != null && relic.name == id) return relic;

        return null;
    }

    /// <summary>Resources에서 목록을 불러온다. 없으면 null.</summary>
    public static RelicCatalog Load() => Resources.Load<RelicCatalog>(ResourcePath);
}
