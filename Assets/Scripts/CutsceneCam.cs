using UnityEngine;

/// <summary>
/// Picks a cutscene camera spot that actually sees the subject: inside the room, not in a wall or furniture, with a clear
/// line of sight, preferring a three-quarter view of the face. Shortcut: samples 24 angles x 3 radii, fine for one room.
/// </summary>
public static class CutsceneCam
{
    public static Vector3 Pick(Vector3 focus, Vector3 faceDir, Bounds room, float minD, float maxD, float camHeight)
    {
        faceDir.y = 0f; faceDir = faceDir.sqrMagnitude < .01f ? Vector3.forward : faceDir.normalized;
        var inner = room; inner.Expand(-.8f);
        float camY = Mathf.Clamp(room.min.y + camHeight, room.min.y + .5f, room.max.y - .3f);
        Vector3 best = default; float bestScore = float.MinValue; bool found = false;
        for (int ring = 0; ring < 3 && !found; ring++)
        {
            float d = Mathf.Lerp(maxD, minD, ring * .5f);
            for (int a = 0; a < 360; a += 15)
            {
                Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                Vector3 p = new Vector3(focus.x + dir.x * d, camY, focus.z + dir.z * d);
                if (p.x < inner.min.x || p.x > inner.max.x || p.z < inner.min.z || p.z > inner.max.z) continue;
                if (Physics.CheckSphere(p, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;                       // inside a wall / furniture
                float dist = Vector3.Distance(p, focus);
                if (Physics.Linecast(p, focus, out var hit, ~0, QueryTriggerInteraction.Ignore) && hit.distance < dist - .5f) continue;   // blocked (hits near the subject are the subject itself)
                float score = -Mathf.Abs(Vector3.Dot(dir, faceDir) - .6f);
                if (score > bestScore) { bestScore = score; best = p; found = true; }
            }
            if (found) break;
        }
        // nothing clear: stand just in front of the subject, at the room's own height
        return found ? best : new Vector3(focus.x + faceDir.x * 1.6f, camY, focus.z + faceDir.z * 1.6f);
    }
}
