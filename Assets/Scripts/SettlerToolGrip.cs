using UnityEngine;

// Tools follow the palm position, but use a stable character-space orientation.
// This avoids inheriting the imported hand's axis correction or the pose at equip time.
[DefaultExecutionOrder(150)]
public class SettlerToolGrip : MonoBehaviour
{
    public Transform avatar;
    public SettlerRig rig;
    public string kind;
    void LateUpdate()
    {
        if (!avatar) return;
        float pitch = 0f;
        if (rig && rig.pose == SettlerRig.Pose.Dig) pitch = 12f + Mathf.Sin(rig.MotionTime * 3.2f) * 18f;
        if (rig && rig.pose == SettlerRig.Pose.Water) pitch = 18f;
        transform.rotation = avatar.rotation * Quaternion.Euler(pitch, 0, 0);
    }
}
