using UnityEngine;
using UnityEngine.Playables;

public class PlayerVFXController : MonoBehaviour
{
    [System.Serializable]
    public class AttackVFX
    {
        public string stateName;
        public PlayableDirector timeline;
    }

    [SerializeField] private Animator animator;
    [SerializeField] private AttackVFX[] attacks;

    private int currentAttack = -1;

    private void Update()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        int attackIndex = GetAttackIndex(state);

        // 离开 Attack State
        if (attackIndex == -1)
        {
            StopCurrentTimeline();
            currentAttack = -1;
            return;
        }

        // 进入新的 Attack State
        if (attackIndex != currentAttack)
        {
            StopCurrentTimeline();

            currentAttack = attackIndex;

            PlayTimeline(attacks[attackIndex].timeline, state.speed);
        }
        else
        {
            // 持续同步 Animator 的 Speed
            SyncTimelineSpeed(attacks[attackIndex].timeline, state.speed);
        }
    }

    private int GetAttackIndex(AnimatorStateInfo state)
    {
        for (int i = 0; i < attacks.Length; i++)
        {
            if (state.IsName(attacks[i].stateName))
                return i;
        }

        return -1;
    }

    private void PlayTimeline(PlayableDirector timeline, float speed)
    {
        if (timeline == null)
            return;

        timeline.Stop();
        timeline.time = 0;
        timeline.Play();

        SyncTimelineSpeed(timeline, speed);
    }

    private void SyncTimelineSpeed(PlayableDirector timeline, float speed)
    {
        if (timeline == null || !timeline.playableGraph.IsValid())
            return;

        if (timeline.playableGraph.GetRootPlayableCount() > 0)
            timeline.playableGraph.GetRootPlayable(0).SetSpeed(speed);
    }

    private void StopCurrentTimeline()
    {
        if (currentAttack < 0 || currentAttack >= attacks.Length)
            return;

        PlayableDirector timeline = attacks[currentAttack].timeline;

        if (timeline != null)
            timeline.Stop();
    }
}