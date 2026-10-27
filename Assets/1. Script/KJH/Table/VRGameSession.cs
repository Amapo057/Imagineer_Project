using UnityEngine;

/// <summary>
/// 씬 전체가 공유하는 단 하나의 GameManager 인스턴스를 들고 있는 곳.
/// SlotModelSpawner(카드 배치 인식)와 TurnController(End Turn 버튼) 양쪽이 똑같은 게임 상태를
/// 봐야 하므로, 둘 중 어느 한쪽 소유가 아니라 여기 하나로 모아둠.
/// GameManager는 MonoBehaviour가 아니라 순수 C# 클래스라 인스펙터로 못 꽂으므로 static으로 들고 있음.
/// </summary>
public static class VRGameSession
{
    private static GameManager instance;

    public static GameManager Game
    {
        get
        {
            if (instance == null)
            {
                instance = new GameManager();
                // VR 데모는 손패가 없어서(물리 카드를 든 순간이 곧 손패) 덱 구성 자체가 의미 없음.
                // 상대 쪽은 아직 물리 슬롯이 없어서(SlotModelSpawner 참고) 늘 내가 선공으로 고정해둠
                // — 나중에 상대쪽 물리 슬롯/턴 전환 UI가 따로 생기면 이 부분을 다시 검토하면 됨
                instance.StartGame(PlayerSide.Me);
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
        Game.StartGame(firstPlayer);
    }
}
