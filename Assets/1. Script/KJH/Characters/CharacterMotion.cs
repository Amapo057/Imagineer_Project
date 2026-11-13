using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KJH.Characters
{
    public sealed class CharacterMotion : MonoBehaviour
    {
        public Animator animator;
        public AnimationClip attackClip;
        public AnimationClip hitClip;
        readonly Queue<bool> pending = new Queue<bool>();
        Coroutine playback;
        public bool IsPlaying => playback != null;
        public float AttackDuration => attackClip != null ? attackClip.length : 0.5f;
        public bool IsAttackAnimating => animator != null && animator.isActiveAndEnabled &&
            (animator.GetCurrentAnimatorStateInfo(0).IsName("Attack") ||
             (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName("Attack")));

        [ContextMenu("Preview/Attack")]
        public void Attack() => Enqueue(false);

        [ContextMenu("Preview/Hit")]
        public void Hit() => Enqueue(true);

        void Enqueue(bool hit)
        {
            if (!isActiveAndEnabled || animator == null) return;
            pending.Enqueue(hit);
            if (playback == null) playback = StartCoroutine(PlayPending());
        }

        IEnumerator PlayPending()
        {
            while (pending.Count > 0)
            {
                bool hit = pending.Dequeue();
                animator.SetTrigger(hit ? "Hit" : "Attack");
                var clip = hit ? hitClip : attackClip;
                yield return new WaitForSeconds((clip != null ? clip.length : 0.5f) + 0.1f);
            }
            playback = null;
        }

        void OnDisable()
        {
            StopAllCoroutines();
            playback = null;
            pending.Clear();
        }
    }
}
