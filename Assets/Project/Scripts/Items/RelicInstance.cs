/// <summary>
/// 실제로 주운 유물 하나. 어떤 유물인지(<see cref="RelicData"/>)와, 그때 정해진 수치를 들고 있다.
///
/// <b>보관함이 RelicData가 아니라 이걸 담는 이유는 주사위 때문이다.</b>
/// 왕의 주사위는 최대 체력이 1~7 사이로 무작위다. 수치를 장착할 때 굴리면 플레이어가
/// 뺐다 꼈다를 반복해서 7이 나올 때까지 다시 굴릴 수 있다. 그러면 무작위가 아니라
/// "조금 귀찮은 7"이 된다.
///
/// 그래서 수치는 <b>주울 때 한 번</b> 굴려 여기 박아두고, 장착·해제는 이 값을 그대로 쓴다.
/// 같은 주사위를 두 개 주우면 각각 다른 숫자를 가진 별개의 개체가 된다.
///
/// class가 아니라 struct로 둔 이유: 값 두 개짜리 묶음이고, 보관함 목록에서 자주 복사된다.
/// 참조로 두면 목록에서 꺼낸 것과 장착 칸에 든 것이 같은 개체인지를 매번 신경 써야 한다.
/// </summary>
public readonly struct RelicInstance
{
    /// <summary>어떤 유물인가. null이면 빈 칸이다.</summary>
    public readonly RelicData Data;

    /// <summary>주울 때 정해진 수치. 무작위 유물이면 개체마다 다르다.</summary>
    public readonly float Amount;

    public RelicInstance(RelicData data, float amount)
    {
        Data = data;
        Amount = amount;
    }

    /// <summary>빈 칸인가.</summary>
    public bool IsEmpty => Data == null;

    /// <summary>빈 칸을 나타내는 값.</summary>
    public static RelicInstance None => new RelicInstance(null, 0f);

    /// <summary>주운 유물 하나를 만든다. 무작위 유물이면 여기서 딱 한 번 굴린다.</summary>
    public static RelicInstance Roll(RelicData data)
        => data == null ? None : new RelicInstance(data, data.RollAmount());
}
