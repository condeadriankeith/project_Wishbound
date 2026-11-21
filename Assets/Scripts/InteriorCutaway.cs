using System.Collections.Generic;
using UnityEngine;

/// <summary>Dollhouse cut-away: wall pieces (CutawayWall) whose room-side faces away from the camera are hidden,
/// so the high iso camera always sees into the room.</summary>
public class InteriorCutaway : MonoBehaviour
{
    [Tooltip("Two-storey homes: height of the loft floor (room-local). Everything at or above it hides while the Star is downstairs. 0 = no loft.")]
    public float loftHeight;
    CutawayWall[] walls; Renderer[] renderers; readonly List<Renderer> loft = new List<Renderer>(); Transform star;
    void Start()
    {
        walls = GetComponentsInChildren<CutawayWall>(true);
        renderers = new Renderer[walls.Length];
        for (int i = 0; i < walls.Length; i++) renderers[i] = walls[i].GetComponent<Renderer>();
        if (loftHeight > 0)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!r.GetComponent<CutawayWall>() && transform.InverseTransformPoint(r.bounds.center).y >= loftHeight - .15f && !r.name.StartsWith("Step") && !r.name.StartsWith("Rung") && !r.name.Contains("Ladder") && !r.name.Contains("Chimney") && !r.name.Contains("Flue") && !r.name.Contains("Lantern")) loft.Add(r);
        var p = GameObject.FindGameObjectWithTag("Player"); star = p ? p.transform : null;
    }
    void LateUpdate()
    {
        var cam = Camera.main; if (!cam || walls == null) return;
        Vector3 f = cam.transform.forward; f.y = 0; f.Normalize();
        for (int i = 0; i < walls.Length; i++)
        {
            if (!renderers[i]) continue;
            Vector3 n = walls[i].transform.parent ? walls[i].transform.parent.TransformDirection(walls[i].inward) : walls[i].inward;
            renderers[i].forceRenderingOff = Vector3.Dot(f, n) > .35f;
        }
        if (loft.Count > 0 && star)
        {
            bool downstairs = transform.InverseTransformPoint(star.position).y < loftHeight * .6f;
            foreach (var r in loft) if (r) r.forceRenderingOff = downstairs;
        }
    }
}
