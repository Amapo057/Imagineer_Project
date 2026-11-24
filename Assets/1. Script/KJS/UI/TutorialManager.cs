using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class TutorialManager : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private TMP_Text healthLabel;
    [SerializeField] private Button endGameButton;
    [SerializeField] private ProductionGameController session;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.3f;

    private const string MarkerHint = "중앙 0번 마커를 바라봐 주세요";
    private const string DrawHint = "카드를 6장 뽑으세요\n한장씩 뽑아주세요";
    private const string PlaceHint = "원하는 자리에 카드를 배치해주세요";
    private const string WeaponHint = "무기가 착용되었습니다.\n주먹을 쥐어 무기를 소환해 공격하세요";
    private GameNetworkManager network;
    private GameManager match;
    private bool placedCard;
    private bool returningToStart;
    private float visibleAlpha;
    private string resultHint = "";

    void Awake()
    {
        visibleAlpha = label.alpha;
        label.alpha = 0f;
        endGameButton.gameObject.SetActive(false);
    }

    void Start()
    {
        network = session.Network;
        network.OnLoadStartSceneReceived += LoadStartScene;
    }

    void Update()
    {
        if (returningToStart) return;
        var game = session.Game;
        if (match != game)
        {
            if (match != null) match.OnGameOver -= CaptureResult;
            match = game;
            match.OnGameOver += CaptureResult;
            placedCard = false;
            resultHint = "";
        }

        int health = session.TableFeedback?.DisplayedHeroHealth(PlayerSide.Me) ?? game.Board.me.HeroHealth;
        healthLabel.SetText("체력: {0}", health);
        bool finished = game.IsGameOver && !session.CombatPlaying;
        if (endGameButton.gameObject.activeSelf != finished)
            endGameButton.gameObject.SetActive(finished);
        FadeTo(finished ? resultHint : game.IsGameOver ? "" : GetHint(game), Time.unscaledDeltaTime);
    }

    private string GetHint(GameManager game)
    {
        if (session.anchor != null && !session.anchor.IsReady) return MarkerHint;
        if (!session.Ready) return DrawHint;

        if (!placedCard)
        {
            foreach (var card in game.Board.me.lanes)
            {
                if (card == null) continue;
                placedCard = true;
                break;
            }
        }

        if (session.PendingWeapons > 0) return WeaponHint;
        if (placedCard) return "";
        if (VRGameSession.ConfirmedPhysicalDraws(PlayerSide.Me) < game.Rules.openingHand) return DrawHint;
        return session.CanAct ? PlaceHint : "";
    }

    private void CaptureResult(PlayerSide? winner)
    {
        resultHint = winner switch
        {
            PlayerSide.Me => "승리했습니다",
            PlayerSide.Opponent => "패배했습니다",
            _ => "무승부입니다"
        };
    }

    public void ReturnToStart()
    {
        if (returningToStart || !session.Game.IsGameOver || session.CombatPlaying) return;
        if (network.Connected) network.SendLoadStartScene();
        LoadStartScene();
    }

    private void LoadStartScene()
    {
        if (returningToStart) return;
        returningToStart = true;
        endGameButton.gameObject.SetActive(false);
        session.PrepareToLeave();
        VRGameSession.ResetGame();
        SceneManager.LoadScene(0);
    }

    void OnDestroy()
    {
        if (network != null) network.OnLoadStartSceneReceived -= LoadStartScene;
        if (match != null) match.OnGameOver -= CaptureResult;
    }

    private void FadeTo(string hint, float delta)
    {
        if (label.alpha == 0f && label.text != hint) label.text = hint;
        float goal = label.text == hint && hint.Length > 0 ? visibleAlpha : 0f;
        label.alpha = Mathf.MoveTowards(label.alpha, goal, visibleAlpha * delta / fadeDuration);
    }
}
