using System.Collections;
using System.Collections.Generic;
using KJH.Characters;
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
        public CardInstance combatCard;
        public int combatAttack, combatHealth;
    }
    sealed class Strike
    {
        public Lane attacker, defender;
        public int? attackerHealth, defenderHealth, heroHealth;
    }
    readonly List<Lane> lanes = new List<Lane>();
    readonly List<Strike> strikes = new List<Strike>();
    readonly int[] heroHealth = new int[2];
    Coroutine playback;
    int combatTurn;
    PlayerSide? combatWinner;
    ProductionGameController session;
    ProductionGameUI ui;
    GameManager game;
    int lastTurn = -1;
    readonly Color mine = new Color(0.15f, 0.85f, 0.82f), enemy = new Color(1f, 0.57f, 0.25f);
    public int LaneCount => lanes.Count;
    public bool IsPlaying => strikes.Count > 0 && HandlesCombat(game);
    internal bool HandlesCombat(GameManager target) =>
        Application.isPlaying && isActiveAndEnabled && session != null && session.Ready &&
        session.HardwareReady && game == target && target == session.Game;
    public void Build(ProductionGameController flow, ProductionGameUI menu)
    {
        if (session != null) return;
        session = flow; ui = menu;
        session.TableFeedback = this;
        foreach (var slot in FindObjectsByType<ProductionCardSlot>(FindObjectsSortMode.None)) Add(slot.transform, PlayerSide.Me, slot.lane);
        foreach (var slot in FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None)) Add(slot.transform, PlayerSide.Opponent, slot.lane);
        BindGame();
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
    internal void BindGame()
    {
        if (game == session.Game) return;
        Unbind();
        CancelPlayback();
        game = session.Game;
        lastTurn = -1;
        game.OnMinionAttacked += Attack; game.OnMinionHit += Hit; game.OnFaceHit += FaceHit;
        game.OnSpellCast += Spell; game.OnGameOver += End;
        foreach (var lane in lanes) { lane.card = null; lane.pulseUntil = 0; }
    }
    void LateUpdate()
    {
        if (session == null) return;
        BindGame();
        if (!IsPlaying) CancelPlayback();
        else if (playback == null) playback = StartCoroutine(PlayCombat());
        if (!IsPlaying && session.Ready && !game.IsGameOver && lastTurn != game.TurnNumber)
        { lastTurn = game.TurnNumber; ui.ShowFeedback(game.State.CurrentPlayer == PlayerSide.Me ? "내 턴 시작 · 카드를 뽑고 행동하세요" : "상대 턴 · 전투를 지켜보세요"); }
        foreach (var lane in lanes)
        {
            bool visible = session.Ready && session.HardwareReady;
            lane.outline.enabled = visible; lane.label.gameObject.SetActive(visible);
            if (!visible || lane.anchor == null) continue;
            var card = IsPlaying ? lane.combatCard : game.Board.GetBoard(lane.side).lanes[lane.index];
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
                card.data.cardName + "\n공격 " + (IsPlaying ? lane.combatAttack : card.currentAttack) +
                "   체력 " + (IsPlaying ? lane.combatHealth : card.currentHealth);
        }
    }
    void Pulse(Lane lane, Color color) { lane.pulse = color; lane.pulseUntil = Time.unscaledTime + 0.65f; }
    Lane FindLane(PlayerSide side, int index) => lanes.Find(lane => lane.side == side && lane.index == index);
    internal CardInstance DisplayedCard(PlayerSide side, int index) =>
        IsPlaying ? FindLane(side, index)?.combatCard : session.Game.Board.GetBoard(side).lanes[index];
    internal int DisplayedHeroHealth(PlayerSide side) =>
        IsPlaying ? heroHealth[(int)side] : session.Game.Board.GetBoard(side).HeroHealth;
    internal void DisplayedStats(PlayerSide side, int index, CardInstance card, out int attack, out int health)
    {
        var lane = IsPlaying ? FindLane(side, index) : null;
        attack = lane != null && lane.combatCard == card ? lane.combatAttack : card.currentAttack;
        health = lane != null && lane.combatCard == card ? lane.combatHealth : card.currentHealth;
    }
    void Attack(PlayerSide side, int index)
    {
        var attacker = FindLane(side, index);
        if (attacker == null) return;
        if (!HandlesCombat(game)) { Pulse(attacker, Color.white); return; }
        if (strikes.Count > 0 && combatTurn != game.TurnNumber) CancelPlayback();
        if (strikes.Count == 0)
        {
            combatTurn = game.TurnNumber;
            combatWinner = null;
            foreach (var lane in lanes)
            {
                lane.combatCard = game.Board.GetBoard(lane.side).lanes[lane.index];
                lane.combatAttack = lane.combatCard?.currentAttack ?? 0;
                lane.combatHealth = lane.combatCard?.currentHealth ?? 0;
            }
            heroHealth[(int)PlayerSide.Me] = game.Board.me.HeroHealth;
            heroHealth[(int)PlayerSide.Opponent] = game.Board.opponent.HeroHealth;
        }
        var defender = FindLane(side == PlayerSide.Me ? PlayerSide.Opponent : PlayerSide.Me, index);
        strikes.Add(new Strike { attacker = attacker, defender = defender });
    }
    void Hit(PlayerSide side, int index)
    {
        var lane = FindLane(side, index);
        if (lane == null) return;
        if (IsPlaying && game.State.CurrentPhase == TurnPhase.Combat)
        {
            var strike = strikes[strikes.Count - 1];
            int health = game.Board.GetBoard(side).lanes[index].currentHealth;
            if (lane == strike.attacker) strike.attackerHealth = health;
            if (lane == strike.defender) strike.defenderHealth = health;
            return;
        }
        Pulse(lane, new Color(1f, 0.22f, 0.22f));
    }
    IEnumerator PlayCombat()
    {
        // Give the slot views one Update to display every captured card before playing its motion.
        yield return null;
        foreach (var strike in strikes)
        {
            var attack = strike.attacker.anchor.GetComponentInChildren<CharacterMotion>();
            if (attack != null && (attack.animator == null || !attack.isActiveAndEnabled)) attack = null;
            while (attack != null && (attack.IsPlaying || attack.IsAttackAnimating)) yield return null;
            Pulse(strike.attacker, Color.white);
            attack?.Attack();
            float impactDelay = attack != null ? Mathf.Clamp(attack.AttackDuration * 0.5f, 0.10f, 0.35f) : 0.15f;
            yield return new WaitForSeconds(impactDelay);
            ShowImpact(strike.attacker, strike.attackerHealth, playHit: false);
            ShowImpact(strike.defender, strike.defenderHealth, playHit: true);
            if (strike.heroHealth.HasValue)
            {
                var target = strike.attacker.side == PlayerSide.Me ? PlayerSide.Opponent : PlayerSide.Me;
                heroHealth[(int)target] = strike.heroHealth.Value;
                if (strike.defender != null) Pulse(strike.defender, new Color(1f, 0.6f, 0.2f));
                ShowFaceHit(target);
            }
            if (attack == null) yield return new WaitForSeconds(0.10f);
            while (attack != null && (attack.IsPlaying || attack.IsAttackAnimating)) yield return null;
            if (strike.attackerHealth > 0) attack?.Hit();
            if (strike.attackerHealth <= 0) strike.attacker.combatCard = null;
            if (strike.defenderHealth <= 0) strike.defender.combatCard = null;
        }
        playback = null;
        strikes.Clear();
        if (game.IsGameOver) End(combatWinner);
    }
    void ShowImpact(Lane lane, int? health, bool playHit)
    {
        if (lane == null || !health.HasValue) return;
        lane.combatHealth = Mathf.Max(0, health.Value);
        Pulse(lane, new Color(1f, 0.22f, 0.22f));
        if (playHit) lane.anchor.GetComponentInChildren<CharacterMotion>()?.Hit();
    }
    void CancelPlayback()
    {
        if (playback != null) StopCoroutine(playback);
        playback = null;
        strikes.Clear();
    }
    public void WeaponImpact(PlayerSide side, int index)
    {
        Hit(side, index);
        foreach (var slot in FindObjectsByType<KJH.Characters.CharacterSlotAnimationBridge>(FindObjectsSortMode.None))
            if (slot.side == side && slot.laneIndex == index) slot.GetComponentInChildren<KJH.Characters.CharacterMotion>()?.Hit();
    }
    void FaceHit(PlayerSide side)
    {
        if (IsPlaying) strikes[strikes.Count - 1].heroHealth = game.Board.GetBoard(side).HeroHealth;
        else ShowFaceHit(side);
    }
    void ShowFaceHit(PlayerSide side) => ui.ShowFeedback(side == PlayerSide.Me ? "영웅이 피해를 받았습니다!" : "상대 영웅에게 피해를 입혔습니다!");
    void Spell(PlayerSide side, CardData card) => ui.ShowFeedback((side == PlayerSide.Me ? "나 · " : "상대 · ") + card.cardName + " 사용");
    void End(PlayerSide? winner)
    {
        if (IsPlaying) { combatWinner = winner; return; }
        ui.ShowFeedback(winner == null ? "무승부입니다." : winner == PlayerSide.Me ? "승리했습니다!" : "패배했습니다. 다음 판에 다시 도전하세요.");
    }
    void Unbind()
    {
        if (game == null) return;
        game.OnMinionAttacked -= Attack; game.OnMinionHit -= Hit; game.OnFaceHit -= FaceHit;
        game.OnSpellCast -= Spell; game.OnGameOver -= End;
    }
    void OnDisable() => CancelPlayback();
    void OnDestroy() => Unbind();
}
