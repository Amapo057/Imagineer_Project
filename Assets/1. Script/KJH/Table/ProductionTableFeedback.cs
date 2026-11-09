using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Presentation only. All damage and card ownership remain in the game session.
public sealed class ProductionTableFeedback : MonoBehaviour
{
    sealed class Lane
    {
        public Transform anchor;
        public PlayerSide side;
        public int index;
        public LineRenderer outline;
        public TextMeshPro label;
        public CardInstance card;
        public float pulseUntil;
        public Color pulse;
    }
    readonly List<Lane> lanes = new List<Lane>();
    ProductionGameController session;
    ProductionGameUI ui;
    GameManager game;
    int lastTurn = -1;
    readonly Color mine = new Color(0.15f, 0.85f, 0.82f), enemy = new Color(1f, 0.57f, 0.25f);
    public int LaneCount => lanes.Count;
    public void Build(ProductionGameController flow, ProductionGameUI menu)
    {
        if (session != null) return;
        session = flow; ui = menu;
        foreach (var slot in FindObjectsByType<ProductionCardSlot>(FindObjectsSortMode.None)) Add(slot.transform, PlayerSide.Me, slot.lane);
        foreach (var slot in FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None)) Add(slot.transform, PlayerSide.Opponent, slot.lane);
    }
    void Add(Transform anchor, PlayerSide side, int index)
    {
        var root = new GameObject("Lane Feedback " + side + " " + index); root.transform.SetParent(transform, false);
        var line = root.AddComponent<LineRenderer>(); line.sharedMaterial = ui.tableMaterial;
        line.useWorldSpace = true; line.loop = true; line.positionCount = 4; line.widthMultiplier = 0.0025f;
        line.numCornerVertices = 3;
        var text = new GameObject("Card Status").AddComponent<TextMeshPro>(); text.transform.SetParent(root.transform, false);
        text.font = ui.font; text.fontSize = 0.14f; text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(0.24f, 0.10f); text.color = side == PlayerSide.Me ? mine : enemy;
        text.outlineWidth = 0.15f; text.outlineColor = Color.black;
        lanes.Add(new Lane { anchor = anchor, side = side, index = index, outline = line, label = text });
    }
    void LateUpdate()
    {
        if (session == null) return;
        if (game != session.Game)
        {
            Unbind(); game = session.Game; lastTurn = -1;
            game.OnMinionAttacked += Attack; game.OnMinionHit += Hit; game.OnFaceHit += FaceHit;
            game.OnSpellCast += Spell; game.OnGameOver += End;
            foreach (var lane in lanes) { lane.card = null; lane.pulseUntil = 0; }
        }
        if (session.Ready && lastTurn != game.TurnNumber)
        { lastTurn = game.TurnNumber; ui.ShowFeedback(game.State.CurrentPlayer == PlayerSide.Me ? "내 턴 시작 · 카드를 뽑고 행동하세요" : "상대 턴 · 전투를 지켜보세요"); }
        foreach (var lane in lanes)
        {
            bool visible = session.Ready && session.HardwareReady;
            lane.outline.enabled = visible; lane.label.gameObject.SetActive(visible);
            if (!visible || lane.anchor == null) continue;
            var card = game.Board.GetBoard(lane.side).lanes[lane.index];
            if (card != lane.card)
            {
                if (card != null) Pulse(lane, new Color(0.95f, 0.84f, 0.38f));
                else if (lane.card != null) Pulse(lane, Color.red);
                lane.card = card;
            }
            var color = lane.side == PlayerSide.Me ? mine : enemy;
            if (Time.unscaledTime < lane.pulseUntil) color = lane.pulse;
            else if (card != null && lane.side == PlayerSide.Opponent && session.CanAct && session.PendingWeapons > 0)
                color = Color.Lerp(enemy, new Color(1f, 0.9f, 0.35f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3));
            else if (card == null) color *= lane.side == PlayerSide.Me && session.CanAct ? 0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 3) : 0.32f;
            color.a = 1;
            lane.outline.startColor = lane.outline.endColor = color;
            var t = lane.anchor; var center = t.position + t.up * 0.006f;
            lane.outline.SetPosition(0, center - t.right * 0.075f - t.forward * 0.09f);
            lane.outline.SetPosition(1, center + t.right * 0.075f - t.forward * 0.09f);
            lane.outline.SetPosition(2, center + t.right * 0.075f + t.forward * 0.09f);
            lane.outline.SetPosition(3, center - t.right * 0.075f + t.forward * 0.09f);
            lane.label.transform.position = center + t.up * 0.06f - t.forward * 0.10f;
            lane.label.transform.rotation = ui.viewCamera.transform.rotation;
            string owner = lane.side == PlayerSide.Me ? "내" : "상대";
            lane.label.text = card == null ? owner + " 필드 " + (lane.index + 1) + "\n빈 칸" :
                card.data.cardName + "\n공격 " + card.currentAttack + "   체력 " + card.currentHealth;
        }
    }
    void Pulse(Lane lane, Color color) { lane.pulse = color; lane.pulseUntil = Time.unscaledTime + 0.65f; }
    void Attack(PlayerSide side, int index) { foreach (var lane in lanes) if (lane.side == side && lane.index == index) Pulse(lane, Color.white); }
    void Hit(PlayerSide side, int index) { foreach (var lane in lanes) if (lane.side == side && lane.index == index) Pulse(lane, new Color(1f, 0.22f, 0.22f)); }
    public void WeaponImpact(PlayerSide side, int index)
    {
        Hit(side, index);
        foreach (var slot in FindObjectsByType<KJH.Characters.CharacterSlotAnimationBridge>(FindObjectsSortMode.None))
            if (slot.side == side && slot.laneIndex == index) slot.GetComponentInChildren<KJH.Characters.CharacterMotion>()?.Hit();
    }
    void FaceHit(PlayerSide side) => ui.ShowFeedback(side == PlayerSide.Me ? "영웅이 피해를 받았습니다!" : "상대 영웅에게 피해를 입혔습니다!");
    void Spell(PlayerSide side, CardData card) => ui.ShowFeedback((side == PlayerSide.Me ? "나 · " : "상대 · ") + card.cardName + " 사용");
    void End(PlayerSide? winner) => ui.ShowFeedback(winner == null ? "무승부입니다." : winner == PlayerSide.Me ? "승리했습니다!" : "패배했습니다. 다음 판에 다시 도전하세요.");
    void Unbind()
    {
        if (game == null) return;
        game.OnMinionAttacked -= Attack; game.OnMinionHit -= Hit; game.OnFaceHit -= FaceHit;
        game.OnSpellCast -= Spell; game.OnGameOver -= End;
    }
    void OnDestroy() => Unbind();
}
