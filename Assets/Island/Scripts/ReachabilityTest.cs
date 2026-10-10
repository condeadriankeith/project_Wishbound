using UnityEngine;
using UnityEngine.AI;

public class ReachabilityTest : MonoBehaviour
{
    public Transform from, to;     // from = spawn, to = summit (positions printed by the validator)
    [ContextMenu("Run")]
    void Run()
    {
        NavMesh.SamplePosition(from.position, out var a, 10f, NavMesh.AllAreas);
        NavMesh.SamplePosition(to.position, out var b, 10f, NavMesh.AllAreas);
        var path = new NavMeshPath();
        NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path);
        Debug.Log($"Path status: {path.status} | corners: {path.corners.Length}");
    }
}
