using UnityEngine;

namespace KJH.Characters
{
    public sealed class CharacterPreview : MonoBehaviour
    {
        [ContextMenu("Preview/Attack All")]
        public void AttackAll()
        {
            foreach (var motion in GetComponentsInChildren<CharacterMotion>()) motion.Attack();
        }

        [ContextMenu("Preview/Hit All")]
        public void HitAll()
        {
            foreach (var motion in GetComponentsInChildren<CharacterMotion>()) motion.Hit();
        }
    }
}
