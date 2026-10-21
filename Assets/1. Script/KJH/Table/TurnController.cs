using TMPro;
using UnityEngine;

/// <summary>
/// 물리 "End Turn" 버튼(UI.Button + Poke Interaction)에 연결하는 다리 역할.
/// 버튼을 누르면 모든 슬롯이 공유하는 게임 상태(VRGameSession.Game)의 EndTurn()을 호출해서
/// 실제로 턴/코스트/전투를 진행시킴.
///
/// 지금은 상대쪽 물리 슬롯이 아직 없어서(SlotModelSpawner 참고, 4개 슬롯 전부 Me), 상대 턴은
/// 항상 빈 필드로 자동으로 지나가고 곧바로 다시 내 턴(Main)으로 돌아옴 — 즉 버튼을 한 번 누르면
/// "내 턴 종료 + 전투 + 상대의 빈 턴(아무것도 안 함) + 내 턴 다시 시작"까지 한 번에 진행됨.
/// 나중에 상대쪽 물리 슬롯/별도 입력이 생기면 자연스럽게 양쪽 다 실제로 카드를 내는 턴제가 됨.
/// </summary>
public class TurnController : MonoBehaviour
{
    [Tooltip("턴/코스트/명치 상태를 보여줄 디버그 텍스트 (선택사항 — 비워둬도 End Turn 동작엔 지장 없음)")]
    [SerializeField] private TextMeshProUGUI statusText;

    void Start()
    {
        // VRGameSession.Game에 처음 접근하는 순간 게임이 시작되므로, 씬이 켜지자마자
        // 상태 텍스트가 빈 화면이 아니라 "Me's turn (T1)"부터 바로 보이도록 여기서 한 번 갱신함
        UpdateStatusText();
    }

    void Update()
    {
        UpdateStatusText();
    }

    // "End Turn" 버튼의 OnClick()에 연결할 메서드
    public void OnEndTurnButtonClicked()
    {
        VRGameSession.Game.EndTurn();
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        if (statusText == null) return;

        GameManager game = VRGameSession.Game;
        if (game.IsGameOver)
        {
            statusText.text = "Game Over";
            return;
        }

        var board = game.Board.GetBoard(game.State.CurrentPlayer);
        string playerName = game.State.CurrentPlayer == PlayerSide.Me ? "Me" : "Opponent";

        statusText.text =
            $"{playerName}'s turn (T{game.TurnNumber})\n" +
            $"Cost {board.currentCost}/{board.maxCost}  Face {game.Board.me.faceHitCount}:{game.Board.opponent.faceHitCount}";
    }
}
