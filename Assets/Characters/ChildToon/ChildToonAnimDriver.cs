using UnityEngine;

// Feeds the CharacterController's horizontal speed into the Animator so the avatar
// idles when standing still and walks when moving.
[RequireComponent(typeof(Animator))]
public class ChildToonAnimDriver : MonoBehaviour
{
    public CharacterController source;
    public float walkSpeedReference = 4f;
    public float damp = 0.08f;
    Animator anim; Vector3 previousPosition;
    ChildPerformer performer;
    void OnEnable() { previousPosition = source ? source.transform.position : transform.position; }
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (source == null) source = GetComponentInParent<CharacterController>();
    }

    [Tooltip("Horizontal speeds below this are treated as standing still (absorbs tiny per-frame ground/slope correction jitter from CharacterController.Move so it never reads as walking).")]
    public float stillSpeedThreshold = 0.12f;

    void Update()
    {
        if (source == null) return;
        var now = source.transform.position;
        var delta = now - previousPosition; previousPosition = now; delta.y = 0f;
        float raw = Time.deltaTime > 0f && delta.sqrMagnitude < 4f ? delta.magnitude / Time.deltaTime : 0f;
        if (!performer) performer = source.GetComponent<ChildPerformer>();
        float s = raw < stillSpeedThreshold || (performer && performer.Seated) ? 0f : raw;
        anim.SetFloat(SpeedHash, s, damp, Time.deltaTime);
        anim.speed = s > 0.1f ? Mathf.Clamp(s / walkSpeedReference, 0.6f, 2f) : 1f;
    }
}
