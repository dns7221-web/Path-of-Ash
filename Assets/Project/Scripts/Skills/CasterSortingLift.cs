using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-24) — 시전자(플레이어)를 자기 이펙트보다 위에 잠깐 그린다. R(왕의 잿불)의 큰 폭발이 캐릭터 모션을 덮던 문제.
///
/// <b>왜 이펙트를 내리지 않고 캐릭터를 올리나.</b> 이펙트를 캐릭터 층(Entity) 아래로 내리면 <b>적들까지</b> 폭발 위로 올라와
/// 폭발에 맞는 것처럼 안 보인다. 폭발은 VFX 층(적 위)에 그대로 두고, 그 폭발이 살아 있는 동안만 시전자 한 명을 VFX 층의
/// 더 높은 순서로 올린다. 액션 게임에서 흔한 "시전자는 자기 이펙트 위" 규칙이다.
///
/// 이펙트가 스스로 사라지면(끝나면 지워지는 프리팹) 원래 층으로 돌려놓는다. 이펙트 수명을 이 클래스가 몰라도 되게,
/// 이펙트 오브젝트가 없어질 때까지 기다린다(안전 상한 있음).
///
/// 그림자처럼 몸보다 아래 순서(음수)에 둔 그림은 올리지 않는다 — 그림자가 폭발 위에 떠 보이면 이상하다.
/// 시전자에게 없으면 처음 쓸 때 스스로 붙는다(<see cref="For"/>).
/// </summary>
[DisallowMultipleComponent]
public class CasterSortingLift : MonoBehaviour
{
    // 올릴 때 쓰는 층과 순서. VFX 이펙트들은 순서 0~2를 쓴다 — 그보다 확실히 위.
    private const string LiftLayer = "VFX";
    private const int LiftOrder = 10;

    // 이펙트가 안 지워지는 프리팹이어도 영영 떠 있지 않게 하는 상한(초).
    private const float MaxLiftSeconds = 3f;

    private readonly List<(SpriteRenderer renderer, int layerId, int order)> saved =
        new List<(SpriteRenderer, int, int)>();

    private Coroutine running;

    /// <summary>시전자에게 붙은 것을 꺼낸다. 없으면 붙인다.</summary>
    public static CasterSortingLift For(Transform caster)
    {
        if (caster == null) return null;
        var lift = caster.GetComponent<CasterSortingLift>();
        return lift != null ? lift : caster.gameObject.AddComponent<CasterSortingLift>();
    }

    /// <summary>effect가 살아 있는 동안 시전자를 그 위에 그린다. 이미 올라가 있으면 기다리는 대상만 새 이펙트로 바꾼다.</summary>
    public void LiftWhile(GameObject effect)
    {
        if (effect == null) return;

        if (running != null) StopCoroutine(running);
        else Raise();

        running = StartCoroutine(HoldUntilGone(effect));
    }

    private IEnumerator HoldUntilGone(GameObject effect)
    {
        float giveUpAt = Time.time + MaxLiftSeconds;
        while (effect != null && Time.time < giveUpAt) yield return null;

        Restore();
        running = null;
    }

    private void Raise()
    {
        saved.Clear();
        int liftLayerId = SortingLayer.NameToID(LiftLayer);

        foreach (SpriteRenderer spriteRenderer in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (spriteRenderer.sortingOrder < 0) continue; // 그림자 등 몸 아래 그림은 그대로

            saved.Add((spriteRenderer, spriteRenderer.sortingLayerID, spriteRenderer.sortingOrder));
            spriteRenderer.sortingLayerID = liftLayerId;
            spriteRenderer.sortingOrder = LiftOrder;
        }
    }

    private void Restore()
    {
        foreach (var (spriteRenderer, layerId, order) in saved)
        {
            if (spriteRenderer == null) continue;
            spriteRenderer.sortingLayerID = layerId;
            spriteRenderer.sortingOrder = order;
        }
        saved.Clear();
    }

    // 도중에 꺼지거나(사망·씬 전환) 지워져도 층이 올라간 채로 남지 않게 되돌린다.
    private void OnDisable()
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }
        Restore();
    }
}
