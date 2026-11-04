using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 한 명의 보드 상태. 나/상대 각각 하나씩 가짐 (GameBoardState 참고).
/// </summary>
[System.Serializable]
public class PlayerBoardState
{
    public PlayerSide side;

    // 후공인지 여부. 코스트 시작 속도가 다름 (GameRules.SecondPlayerStartingCost 참고)
    public bool isSecondPlayer;

    // 이 플레이어 본인 턴이 지금까지 몇 번째인지 (코스트 계산에 사용)
    public int ownTurnCount;

    // 필드 4라인. 인덱스 0~3, 비어있으면 null.
    // 소환 시 위치가 고정되고 이후 재배치는 안 됨
    public CardInstance[] lanes = new CardInstance[4];

    // 코스트. 화면 표시는 로마 숫자로 변환해서 보여줄 예정 (UI 쪽에서 처리)
    public int currentCost;
    public int maxCost;

    public int maxHeroHealth = GameRules.StartingHeroHealth;
    public int heroDamage;
    public int fatigueDamage;
    public int HeroHealth => Math.Max(0, maxHeroHealth - heroDamage);
    // Compatibility for older scene/debug code; this now represents damage, not hits.
    public int faceHitCount { get => heroDamage; set => heroDamage = value; }

    // 지금 장착 중인 무기의 공격력(무기 키워드 전용). 장착과 동시에 바로 휘둘러 소모되므로
    // (GameManager.EquipAndSwingWeapon 참고) 평소엔 거의 항상 0이고, 휘두르는 그 순간에만
    // 잠깐 값이 들어갔다가 바로 0으로 돌아옴. 나중에 "여러 턴에 걸쳐 들고 있다가 쓰는" 지속형
    // 무기로 확장하게 되면 이 필드를 그대로 재사용하면 됨
    public int weaponAttack;

    public List<CardData> hand = new List<CardData>();
    public List<CardData> deck = new List<CardData>();

    // 비어있는 라인 하나 찾기 (없으면 -1)
    public int GetEmptyLaneIndex()
    {
        for (int i = 0; i < lanes.Length; i++)
        {
            if (lanes[i] == null) return i;
        }
        return -1;
    }

    // 라인에 카드 놓기. 위치는 한 번 정해지면 안 바뀜
    public bool TryPlaceOnLane(CardInstance card, int laneIndex)
    {
        if (laneIndex < 0 || laneIndex >= lanes.Length) return false;
        if (lanes[laneIndex] != null) return false;

        lanes[laneIndex] = card;
        return true;
    }

    // 정면에 방어 유닛이 없으면 공격력만큼 영웅 피해를 받음.
    public void TakeFaceHit(int damage = 1)
    {
        heroDamage += Math.Max(0, damage);
    }

    // 누적 피해가 시작 체력에 도달하면 패배.
    public bool IsDefeated => heroDamage >= maxHeroHealth;

    // 필드(4라인), 손패, 덱까지 전부 카드가 한 장도 없는지 여부.
    // "둘 다 이 상태면 명치를 덜 맞은 쪽이 승리" 규칙에서 사용 (GameManager.CheckGameOver 참고).
    // 마법 카드는 시전 즉시 소모되고 필드에 남지 않아서(DemoTurnController.TryCastSpell 참고)
    // 여기 카운트할 "마법 슬롯" 자체가 없음 — 예전에 있던 spellSlot 필드(항상 null이던 죽은 코드)는 정리함
    public bool HasNoCards => hand.Count == 0 && deck.Count == 0 && Array.TrueForAll(lanes, c => c == null);

    // 체력이 0 이하인 카드를 필드에서 치움 (전투 페이즈가 끝날 때 호출됨)
    public void RemoveDeadCards()
    {
        for (int i = 0; i < lanes.Length; i++)
        {
            if (lanes[i] != null && !lanes[i].IsAlive) lanes[i] = null;
        }
    }
}
