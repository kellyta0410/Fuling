using UnityEngine;
using UnityEngine.Playables;

public class PlayerVFXController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayableDirector attack1Timeline;

    [Header("VFX Trigger")]
    [Range(0f, 1f)]
    [SerializeField] private float attack1TriggerTime = 0.4f;

    private bool attack1VFXPlayed;

    private void Update()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

        if (state.IsName("Attack1"))
        {
            if (!attack1VFXPlayed &&
                state.normalizedTime >= attack1TriggerTime)
            {
                attack1VFXPlayed = true;

                if (attack1Timeline != null)
                {
                    attack1Timeline.Stop();
                    attack1Timeline.time = 0;
                    attack1Timeline.Play();
                }
            }
        }
        else
        {
            attack1VFXPlayed = false;
        }
    }
}