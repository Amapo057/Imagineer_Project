using UnityEngine;

namespace KJH.Characters
{
    // Added to slots in the integration scene; the team's spawner stays unchanged.
    public sealed class CharacterSlotAnimationBridge : MonoBehaviour
    {
        public PlayerSide side;
        public int laneIndex;
        GameManager subscribed;

        void OnEnable() => Bind();
        void Update()
        {
            if (!ReferenceEquals(subscribed, VRGameSession.Game)) Bind();
        }

        void Bind()
        {
            Unbind();
            subscribed = VRGameSession.Game;
            subscribed.OnMinionAttacked += OnAttack;
            subscribed.OnMinionHit += OnHit;
        }

        void OnAttack(PlayerSide player, int lane)
        {
            if (ProductionGameController.Current?.TableFeedback?.HandlesCombat(subscribed) == true) return;
            if (player == side && lane == laneIndex)
                GetComponentInChildren<CharacterMotion>()?.Attack();
        }

        void OnHit(PlayerSide player, int lane)
        {
            if (subscribed.State.CurrentPhase == TurnPhase.Combat &&
                ProductionGameController.Current?.TableFeedback?.HandlesCombat(subscribed) == true) return;
            if (player == side && lane == laneIndex)
                GetComponentInChildren<CharacterMotion>()?.Hit();
        }

        void OnDisable() => Unbind();
        void Unbind()
        {
            if (subscribed == null) return;
            subscribed.OnMinionAttacked -= OnAttack;
            subscribed.OnMinionHit -= OnHit;
            subscribed = null;
        }
    }
}
