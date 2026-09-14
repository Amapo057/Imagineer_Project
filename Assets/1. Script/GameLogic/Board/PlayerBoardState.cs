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

    // 마법 카드 전용 슬롯 (4라인과는 별개의 자리)
    public CardInstance spellSlot;

    // 코스트. 화면 표시는 로마 숫자로 변환해서 보여줄 예정 (UI 쪽에서 처리)
    public int currentCost;
    public int maxCost;

    // 명치 체력은 데미지 총합이 아니라 "맞은 횟수"로 취급
    public int faceHitCount;

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

    // 명치를 한 대 맞음 (전투 페이즈에서 방어할 카드가 없을 때 호출됨)
    public void TakeFaceHit()
    {
        faceHitCount++;
    }

    // 명치를 GameRules.FaceHitThreshold번 맞으면 패배
    public bool IsDefeated => faceHitCount >= GameRules.FaceHitThreshold;

    // 체력이 0 이하인 카드를 필드에서 치움 (전투 페이즈가 끝날 때 호출됨)
    public void RemoveDeadCards()
    {
        for (int i = 0; i < lanes.Length; i++)
        {
            if (lanes[i] != null && !lanes[i].IsAlive) lanes[i] = null;
        }
        if (spellSlot != null && !spellSlot.IsAlive) spellSlot = null;
    }
}
