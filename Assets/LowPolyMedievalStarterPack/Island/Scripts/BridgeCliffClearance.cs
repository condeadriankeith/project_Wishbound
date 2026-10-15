using UnityEngine;

/// <summary>
/// Post-process pass that pushes bridge endpoints away from cliff bases.
/// A "cliff base" = a point where the ground is relatively flat right next to
/// a steep slope — the classic spot where bridge meshes clip into or float
/// above messy cliff-wall geometry.
///
/// This version samples the ground via raycasts against a Collider (works
/// with procedurally generated meshes, not just Unity's built-in Terrain).
///
/// Usage:
///   1. Add this component to any GameObject in the scene (e.g. IslandRoads).
///   2. Assign groundCollider (e.g. the Island mesh's MeshCollider) and/or
///      set groundLayerMask to match whatever layer your ground sits on.
///   3. Call ClearBridgeEndpoints(start, end) after your procedural bridge
///      generator computes an endpoint pair, and use the corrected result.
///   4. Or right-click the component in the Inspector -> "Clear All Bridges In Children"
///      for an edit-time pass over everything tagged "Bridge" under a parent.
/// </summary>
[ExecuteAlways]
public class BridgeCliffClearance : MonoBehaviour
{
    [Header("Ground")]
    [Tooltip("Collider to raycast against for ground height/slope (e.g. the Island mesh's MeshCollider).")]
    public Collider groundCollider;

    [Tooltip("Layer mask used for the raycasts. Leave as Everything if unsure.")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("Ray start height above the probed point, and max ray distance downward.")]
    public float rayStartHeight = 200f;
    public float rayMaxDistance = 500f;

    [Header("Cliff Detection")]
    [Tooltip("Slope angle (degrees) above which terrain counts as a 'cliff wall'.")]
    public float cliffSlopeAngle = 45f;

    [Tooltip("Radius (meters) around a candidate point used to sample for nearby steep slope.")]
    public float cliffDetectionRadius = 6f;

    [Tooltip("How many rays to cast around the point when checking for nearby cliff walls.")]
    public int detectionSamples = 12;

    [Header("Clearance")]
    [Tooltip("Minimum horizontal distance an endpoint must keep from a detected cliff base.")]
    public float minClearance = 4f;

    [Tooltip("Distance to push per iteration when an endpoint is too close.")]
    public float pushStep = 1.5f;

    [Tooltip("Safety cap on push iterations so this can never hang or infinite-loop.")]
    public int maxIterations = 20;

    [Tooltip("Extra vertical offset applied after re-sampling height, to avoid z-fighting with terrain.")]
    public float heightSkin = 0.02f;

    [Header("Debug")]
    public bool drawGizmos = true;

    // ---------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------

    /// <summary>
    /// Clears both endpoints of a bridge transform (assumes first and last
    /// child are the two anchor points — adjust to match your generator's
    /// convention, or use the explicit overload below).
    /// </summary>
    public void ClearBridgeEndpoints(Transform bridgeRoot)
    {
        if (bridgeRoot == null || bridgeRoot.childCount < 2)
        {
            Debug.LogWarning("[BridgeCliffClearance] Bridge root needs at least 2 children (start/end anchors).", bridgeRoot);
            return;
        }

        Transform startAnchor = bridgeRoot.GetChild(0);
        Transform endAnchor = bridgeRoot.GetChild(bridgeRoot.childCount - 1);

        Vector3 newStart = ClearPoint(startAnchor.position, endAnchor.position);
        Vector3 newEnd = ClearPoint(endAnchor.position, startAnchor.position);

        startAnchor.position = newStart;
        endAnchor.position = newEnd;
    }

    /// <summary>
    /// Explicit version: pass in the two endpoints directly, get back the
    /// corrected pair. Use this if your generator works with raw Vector3s
    /// rather than child transforms (most procedural generators do).
    /// </summary>
    public (Vector3 start, Vector3 end) ClearBridgeEndpoints(Vector3 start, Vector3 end)
    {
        Vector3 newStart = ClearPoint(start, end);
        Vector3 newEnd = ClearPoint(end, start);
        return (newStart, newEnd);
    }

    /// <summary>
    /// Core logic: given a candidate point and the "other end" of the bridge
    /// (used to pick a sensible push direction when the slope gradient is
    /// ambiguous), returns a point that clears the nearest cliff base by
    /// at least minClearance, re-sampled onto ground height via raycast.
    /// </summary>
    public Vector3 ClearPoint(Vector3 point, Vector3 otherEnd)
    {
        Vector3 current = point;

        for (int i = 0; i < maxIterations; i++)
        {
            if (!TryFindNearestCliffDirection(current, out Vector3 awayFromCliffDir, out float distanceToCliff))
            {
                // No cliff detected nearby — we're clear.
                break;
            }

            if (distanceToCliff >= minClearance)
            {
                // Already far enough from the steepest nearby point.
                break;
            }

            Vector3 bridgeAxis = (current - otherEnd);
            bridgeAxis.y = 0f;
            bridgeAxis = bridgeAxis.sqrMagnitude > 0.001f ? bridgeAxis.normalized : awayFromCliffDir;

            Vector3 pushDir = (awayFromCliffDir * 0.75f + bridgeAxis * 0.25f);
            pushDir.y = 0f;
            if (pushDir.sqrMagnitude < 0.0001f) pushDir = awayFromCliffDir;
            pushDir.Normalize();

            current += pushDir * pushStep;
        }

        // Re-sample final height from ground raycast + small skin offset.
        if (TryRaycastGround(current, out RaycastHit hit))
        {
            current = hit.point + Vector3.up * heightSkin;
        }

        return current;
    }

    // ---------------------------------------------------------------
    // Cliff detection
    // ---------------------------------------------------------------

    private bool TryFindNearestCliffDirection(Vector3 origin, out Vector3 awayDir, out float nearestDist)
    {
        awayDir = Vector3.zero;
        nearestDist = float.MaxValue;
        bool found = false;

        float slopeThreshold = Mathf.Cos(cliffSlopeAngle * Mathf.Deg2Rad); // dot(normal, up) below this = steep

        if (TryGetSlopeSteepness(origin, out float selfSteepness) && selfSteepness < slopeThreshold)
        {
            awayDir = Vector3.forward;
            nearestDist = 0f;
            found = true;
        }

        for (int i = 0; i < detectionSamples; i++)
        {
            float angle = (360f / detectionSamples) * i;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

            for (float r = 1f; r <= cliffDetectionRadius; r += Mathf.Max(1f, cliffDetectionRadius / 6f))
            {
                Vector3 sample = origin + dir * r;

                if (!TryGetSlopeSteepness(sample, out float steepness))
                    continue; // no ground hit there — skip, don't treat as cliff

                if (steepness < slopeThreshold)
                {
                    if (r < nearestDist)
                    {
                        nearestDist = r;
                        awayDir = -dir;
                        found = true;
                    }
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Raycasts straight down at worldPos (ignoring its Y) and returns
    /// dot(groundNormal, Vector3.up) at the hit point. 1.0 = flat ground,
    /// lower values = steeper slope. Returns false if nothing was hit.
    /// </summary>
    private bool TryGetSlopeSteepness(Vector3 worldPos, out float steepness)
    {
        steepness = 1f;
        if (!TryRaycastGround(worldPos, out RaycastHit hit)) return false;
        steepness = Vector3.Dot(hit.normal, Vector3.up);
        return true;
    }

    private bool TryRaycastGround(Vector3 worldPos, out RaycastHit hit)
    {
        Vector3 origin = new Vector3(worldPos.x, worldPos.y + rayStartHeight, worldPos.z);
        Ray ray = new Ray(origin, Vector3.down);

        if (groundCollider != null)
        {
            // Raycast against everything on the mask, but only accept hits
            // that belong to the assigned ground collider (or its children),
            // so other scene geometry (trees, bridges themselves) can't be
            // mistaken for ground.
            RaycastHit[] hits = Physics.RaycastAll(ray, rayMaxDistance, groundLayerMask);
            float best = float.MaxValue;
            hit = default;
            bool found = false;

            foreach (var h in hits)
            {
                if (h.collider == groundCollider || h.collider.transform.IsChildOf(groundCollider.transform))
                {
                    if (h.distance < best)
                    {
                        best = h.distance;
                        hit = h;
                        found = true;
                    }
                }
            }
            return found;
        }

        // No specific ground collider assigned — fall back to first hit on the mask.
        return Physics.Raycast(ray, out hit, rayMaxDistance, groundLayerMask);
    }

    // ---------------------------------------------------------------
    // Editor convenience
    // ---------------------------------------------------------------

    [ContextMenu("Clear All Bridges In Children (by tag 'Bridge')")]
    private void ClearAllBridgesInChildren()
    {
        int count = 0;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.CompareTag("Bridge"))
            {
                ClearBridgeEndpoints(child);
                count++;
            }
        }
        Debug.Log($"[BridgeCliffClearance] Processed {count} bridge(s).");
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, cliffDetectionRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, minClearance);
    }
}
