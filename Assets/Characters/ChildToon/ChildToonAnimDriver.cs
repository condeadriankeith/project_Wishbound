using UnityEngine;

// Feeds the CharacterController's horizontal speed into the Animator so the avatar
// idles when standing still and walks when moving.
[RequireComponent(typeof(Animator))]
public class ChildToonAnimDriver : MonoBehaviour
{
    public CharacterController source;
    public float walkSpeedReference = 4f;
    public float damp = 0.08f;
    Animator anim;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (source == null) source = GetComponentInParent<CharacterController>();
    }

    void Update()
    {
        if (source == null) return;
        var v = source.velocity; v.y = 0f;
        float s = v.magnitude;
        anim.SetFloat(SpeedHash, s, damp, Time.deltaTime);
        anim.speed = s > 0.1f ? Mathf.Clamp(s / walkSpeedReference, 0.6f, 2f) : 1f;
    }
}
