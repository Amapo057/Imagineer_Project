using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 씬 전체가 공유하는 단 하나의 GameManager 인스턴스를 들고 있는 곳.
/// SlotModelSpawner(카드 배치 인식)와 TurnController(End Turn 버튼) 양쪽이 똑같은 게임 상태를
/// 봐야 하므로, 둘 중 어느 한쪽 소유가 아니라 여기 하나로 모아둠.
/// GameManager는 MonoBehaviour가 아니라 순수 C# 클래스라 인스펙터로 못 꽂으므로 static으로 들고 있음.
/// </summary>
public static class VRGameSession
{
    public static bool RequireNetwork { get; set; }
    public static bool NetworkReady { get; set; }
    public static bool NetworkPlayAllowed => !RequireNetwork || (NetworkReady && GameNetworkManager.Instance.Connected);
    private static GameManager instance;
    private static KJH.Balancing.SecondPlayerCoin coin;
    private static readonly HashSet<int> usedPhysicalSpells = new HashSet<int>();
    private static readonly int[] pendingDraws = new int[2];
    private static readonly int[] allocatedDraws = new int[2];
    private static readonly int[] confirmedDraws = new int[2];
    private static readonly int[] physicalHands = new int[2];
    private static readonly int[] deferredFatigue = new int[2];

    private static void CreateGame(PlayerSide firstPlayer)
    {
        usedPhysicalSpells.Clear();
        System.Array.Clear(pendingDraws, 0, 2);
        System.Array.Clear(allocatedDraws, 0, 2);
        System.Array.Clear(confirmedDraws, 0, 2);
        System.Array.Clear(physicalHands, 0, 2);
        System.Array.Clear(deferredFatigue, 0, 2);
        // The physical deck is accounted by draw requests below, not a digital deck.
        instance = new GameManager(resourceExhaustionDrawEnabled: false,
            rules: new MatchRuleSet { fatigueOnEmptyDraw = false });
        instance.OnDrawRequested += (side, count) =>
        {
            int i = (int)side;
            for (int n = 0; n < count; n++)
            {
                // Pending cards already reserve hand space; confirming them grants no extra draws.
                if (physicalHands[i] + pendingDraws[i] >= instance.Rules.maxHandSize) break;
                if (allocatedDraws[i] < GameRules.DeckSize)
                {
                    pendingDraws[i]++;
                    allocatedDraws[i]++;
                }
                else deferredFatigue[i]++;
            }
            ApplyConfirmedFatigue(side);
        };
        instance.StartGame(firstPlayer);
        coin = new KJH.Balancing.SecondPlayerCoin(instance);
    }

    public static int PendingPhysicalDraws(PlayerSide side) { var game = Game; return pendingDraws[(int)side]; }
    public static int ConfirmedPhysicalDraws(PlayerSide side) { var game = Game; return confirmedDraws[(int)side]; }
    public static int RemainingPhysicalDeck(PlayerSide side) => GameRules.DeckSize - ConfirmedPhysicalDraws(side);
    public static int PhysicalHandCount(PlayerSide side) { var game = Game; return physicalHands[(int)side]; }
    public static bool ConsumePhysicalCard(PlayerSide side)
    {
        if (Game.IsGameOver || PhysicalHandCount(side) <= 0) return false;
        physicalHands[(int)side]--;
        return true;
    }
    public static bool ConfirmPhysicalDraw(PlayerSide side)
    {
        int i = (int)side;
        if (Game.IsGameOver || pendingDraws[i] <= 0) return false;
        pendingDraws[i]--;
        confirmedDraws[i]++;
        physicalHands[i]++;
        ApplyConfirmedFatigue(side);
        return true;
    }

    private static void ApplyConfirmedFatigue(PlayerSide side)
    {
        int i = (int)side;
        if (confirmedDraws[i] < GameRules.DeckSize) return;
        var board = instance.Board.GetBoard(side);
        while (deferredFatigue[i] > 0 && !instance.IsGameOver)
        {
            deferredFatigue[i]--;
            board.heroDamage += ++board.fatigueDamage;
            instance.CheckGameOver();
        }
    }

    public static bool TryCastPhysicalSpell(CardData card, PlayerSide side, int lane = -1)
    {
        var game = Game;
        if (card == null || PhysicalHandCount(side) <= 0 || usedPhysicalSpells.Contains((int)side * 100 + card.cardId)) return false;
        if (!game.TryCastSpell(card, side, lane, consumeHand: false,
            onConsumed: () => ConsumePhysicalCard(side))) return false;
        usedPhysicalSpells.Add((int)side * 100 + card.cardId);
        return true;
    }
    public static KJH.Balancing.SecondPlayerCoin Coin
    {
        get
        {
            var game = Game;
            return coin ?? (coin = new KJH.Balancing.SecondPlayerCoin(game));
        }
    }

    public static GameManager Game
    {
        get
        {
            if (instance == null)
            {
                // VR 데모는 손패가 없어서(물리 카드를 든 순간이 곧 손패) 덱 구성 자체가 의미 없음.
                // 손패/덱이 항상 0장이라, GameManager의 "둘 다 카드 없음 -> 무승부" 판정
                // (resourceExhaustionDrawEnabled)을 그대로 켜두면 게임 시작 직후 아직 아무도
                // 카드를 안 낸 상태에서 End Turn만 눌러도 그 자리에서 무승부로 끝나버림 — VR에서는
                // 이 조건 자체가 의미가 없으므로 꺼서 생성함. 승패는 명치를 FaceHitThreshold번
                // 맞았을 때만 결정됨
                CreateGame(PlayerSide.Me);
                // 상대 쪽은 아직 물리 슬롯이 없어서(SlotModelSpawner 참고) 늘 내가 선공으로 고정해둠
                // — 나중에 상대쪽 물리 슬롯/턴 전환 UI가 따로 생기면 이 부분을 다시 검토하면 됨
            }
            return instance;
        }
    }

    // GameNetworkManager가 핸드셰이크로 실제 선공/후공을 정한 뒤 OnGameStartReceived로 알려주면
    // 호출됨. Game getter가 이미 StartGame(PlayerSide.Me)로 시작시켜둔 상태(코스트/ownTurnCount
    // 등)를 네트워크가 정한 진짜 선공 쪽으로 다시 맞춤 — 안 그러면 호스트/클라이언트 양쪽이
    // "누가 선공인지"를 다르게 알고 있어서 코스트 공식(선공 1,2,3.. / 후공 2,2,3..)이 서로 어긋남.
    // 네트워크가 안 붙은 솔로 플레이에서는 이 메서드가 호출되지 않으므로 기존 동작 그대로임.
    public static void ApplyNetworkFirstPlayer(PlayerSide firstPlayer)
    {
        // A network start is a fresh match; retain no costs or lanes from the
        // provisional solo start or a previous round.
        CreateGame(firstPlayer);
    }

    // 재대결(재시작) 전용 - GameManager.StartGame()은 턴/선공만 다시 정하고 라인에 놓인 카드나
    // 명치 맞은 횟수는 그대로 남겨두기 때문에(코드 참고: Board는 안 건드림), 완전히 깨끗한 새
    // 게임을 만들려면 인스턴스 자체를 새로 만들어야 함. GameHUDController.OnRematchButtonClicked
    // /OnRematchReceived에서 호출함 - 네트워크 연결 중이면 이후 ApplyNetworkFirstPlayer로 바로
    // 다시 호출되어 선공이 올바르게 재조정됨(일단 Me로 시작해두는 이유는 솔로 플레이와 동일)
    public static void ResetGame()
    {
        CreateGame(PlayerSide.Me);
    }
}
