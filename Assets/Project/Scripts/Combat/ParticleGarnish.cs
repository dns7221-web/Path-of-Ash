using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추가 생성(2026-09-17, 플레이어 파티클) — 프레임 이펙트에 곁들인 파티클 하나.
///
/// 이펙트 프리팹(EmberSlash, SlamImpact 같은 것)의 자식으로 들어간다. 부모 이펙트는
/// <see cref="SpriteFrameAnimator"/>가 그림을 다 넘기면 스스로를 지우는데, 그대로 두면 자식 파티클도
/// 같이 지워져서 <b>그림이 끝나는 순간 불티가 허공에서 한꺼번에 사라진다.</b> 곁들임이 하는 일이
/// "그림이 끝난 뒤 0.2~0.5초의 여운"이라 그 부분이 제일 먼저 잘린다.
///
/// 그래서 부모를 지우기 직전에 <see cref="ReleaseAll"/>이 이 표식이 붙은 자식을 월드로 떼어 낸다.
/// 떼어 낸 파티클은 남은 불티가 다 꺼지면 스스로 지워진다 — 빌더가 Stop Action을 Destroy로 넣어 둔다
/// (유니티 내장 기능이라 따로 타이머를 두지 않았다).
///
/// 표식 역할도 한다. 다른 이펙트를 복제해서 만드는 빌더(자폭병 폭발, 사수 화살 등)가 이 표식이 붙은
/// 자식을 걷어내서, 원본의 곁들임이 엉뚱한 복제본에 딸려 가지 않게 한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public class ParticleGarnish : MonoBehaviour
{
    // GetComponentsInChildren 결과를 받을 재사용 목록. 이펙트가 끝날 때마다 배열을 새로 만들지 않으려고 둔다.
    // 메인 스레드에서만 부르고 한 호출 안에서 비우므로 여럿이 같이 써도 섞이지 않는다.
    private static readonly List<ParticleGarnish> Buffer = new List<ParticleGarnish>();

    /// <summary>
    /// root 아래의 곁들임 파티클을 전부 월드로 떼어 낸다. root를 지우기 <b>직전에</b> 부른다.
    /// 곁들임이 없는 이펙트(몬스터 VFX 등)에서는 아무 일도 하지 않는다.
    ///
    /// 왜 자식 쪽 OnDestroy에서 스스로 떨어지지 않나: 부모가 지워질 때는 자식도 이미 지워지는 중이라
    /// 그 시점에는 부모를 바꿀 수 없다. 지우는 쪽이 먼저 떼어 내야 한다.
    /// </summary>
    public static void ReleaseAll(GameObject root)
    {
        if (root == null) return;

        root.GetComponentsInChildren(true, Buffer);
        for (int i = 0; i < Buffer.Count; i++) Buffer[i].Release();
        Buffer.Clear();
    }

    /// <summary>
    /// 곁들임을 그림과 같은 방향·길이로 맞춘다. 그림을 회전 대신 좌우 반전(flipX)과 가로 배율로
    /// 맞추는 이펙트(Q 내려찍기)에서 부른다.
    ///
    /// <b>좌우 반전을 Y축 180도 회전으로 하는 이유:</b> SpriteRenderer.flipX는 그림에만 먹고 파티클에는
    /// 없다. 음수 배율로 뒤집으면 Scaling Mode(Local)에 따라 크기까지 뒤집혀 결과를 믿기 어렵다.
    /// Y축으로 반 바퀴 돌리면 화면(XY 평면)에서는 정확히 좌우만 바뀌고 크기와 위쪽(+Y)은 그대로다.
    ///
    /// <b>가로 배율을 셰이프 모듈에 곱하는 이유:</b> Scaling Mode가 Local이라 부모 이펙트의 배율은
    /// 파티클에 안 먹는다(그래서 불티 크기가 유닛 그대로다). 대신 방출 영역의 길이만 그림을 따라
    /// 줄어야 위·아래로 쓸 때 짧아진 균열 밖에서 불티가 솟지 않는다.
    ///
    /// 곁들임 자식은 회전 없이 만든다는 전제다(<c>AshPlayerParticleBuilder</c>가 그렇게 만든다).
    /// </summary>
    /// <param name="root">방금 만든 이펙트 인스턴스.</param>
    /// <param name="flipX">그림을 좌우 반전했는가.</param>
    /// <param name="lengthScale">그림의 가로 배율에 곱한 값. 1이면 그대로.</param>
    public static void MatchSprite(GameObject root, bool flipX, float lengthScale)
    {
        if (root == null) return;

        root.GetComponentsInChildren(true, Buffer);
        for (int i = 0; i < Buffer.Count; i++)
        {
            ParticleGarnish garnish = Buffer[i];
            garnish.transform.localRotation = flipX ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;

            if (Mathf.Approximately(lengthScale, 1f)) continue;

            // 인스턴스의 모듈을 바꾸므로 프리팹 에셋의 값은 그대로다.
            ParticleSystem.ShapeModule shape = garnish.GetComponent<ParticleSystem>().shape;
            Vector3 scale = shape.scale;
            scale.x *= lengthScale;
            shape.scale = scale;
        }
        Buffer.Clear();
    }

    /// <summary>
    /// 부모에서 떼어 내 월드에 남긴다. 이미 떨어져 있거나 할 일이 끝난 파티클은 건드리지 않는다.
    /// </summary>
    private void Release()
    {
        if (transform.parent == null) return;

        var particles = GetComponent<ParticleSystem>();

        // 불티가 다 꺼졌고 더 나올 것도 없으면 부모와 같이 지워져도 잃을 게 없다.
        // 떼어 낸 뒤에는 Stop Action이 다시 불리지 않으므로, 이미 멈춘 것을 떼어 내면 월드에 빈 오브젝트로 남는다.
        if (!particles.IsAlive(true)) return;

        // 월드 위치·회전은 그대로 두고 부모만 끊는다.
        transform.SetParent(null, true);

        // SetParent(null, true)는 부모 배율을 localScale로 옮겨 온다. Scaling Mode가 Local이라 그 값이
        // 곧바로 불티 크기와 방출 영역에 곱해지므로, 부모 밑에서 쓰던 자기 배율(1)로 되돌린다.
        transform.localScale = Vector3.one;

        // 따라다니며 뿌리던 것(화살 꼬리)은 여기서 방출을 멈춘다. 주인이 사라졌으니 더 뿌릴 자리가 없다.
        // 한 번만 도는 것은 그대로 둔다 — 시작 지연으로 뒤늦게 나오는 불티(E 바닥 잔불)가 아직 안 나왔을 수 있다.
        ParticleSystem.MainModule main = particles.main;
        if (main.loop) particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
