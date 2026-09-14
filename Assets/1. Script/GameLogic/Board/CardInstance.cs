using UnityEngine;

/// <summary>
/// 필드에 실제로 나와 있는 카드 한 장의 "지금 상태".
/// CardData(원본 스탯 — 코스트/공격력/체력/능력)는 게임 내내 안 바뀌지만,
/// 필드에 나온 카드는 전투로 체력이 깎이는 등 상태가 계속 바뀌기 때문에
/// 원본 데이터 + 지금 상태를 합쳐서 따로 들고 있는 클래스.
/// </summary>
[System.Serializable]
public class CardInstance
{
    // 원본 카드 데이터 (기준값). 이 값 자체는 수정하지 않음
    public CardData data;

    // 지금 현재 값 — 전투 중 깎이거나(체력), 나중에 버프/디버프가 생기면 여기가 바뀜
    public int currentAttack;
    public int currentHealth;

    // 이번 턴에 이미 공격했는지 여부
    public bool hasAttackedThisTurn;

    // 어느 편 카드인지
    public PlayerSide owner;

    public CardInstance(CardData data, PlayerSide owner)
    {
        this.data = data;
        this.owner = owner;
        currentAttack = data.attack;
        currentHealth = data.health;
        hasAttackedThisTurn = false;
    }

    // 살아있는지 확인 (체력 0 이하면 죽은 것으로 취급)
    public bool IsAlive => currentHealth > 0;

    // 피해를 입힘 (전투 페이즈에서 사용)
    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
    }
}
