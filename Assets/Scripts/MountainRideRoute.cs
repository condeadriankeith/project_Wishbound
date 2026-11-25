using System.Collections.Generic;
using UnityEngine;

/// <summary>Distance sampling over the rendered road centrelines and their junctions.</summary>
internal sealed class MountainRideRoute
{
    readonly List<Vector3> points = new List<Vector3>();
    readonly List<float> distances = new List<float>();
    Vector3[] tangents;
    float[] travelTimes, paces;
    public float Length => distances[distances.Count - 1];
    public float FlightTime => travelTimes[travelTimes.Length - 1];

    public static MountainRideRoute Build(Vector3 from, Vector3 to, BridgeWalkSurface bridge, IReadOnlyList<Bounds> canopies)
    {
        var route = new MountainRideRoute();
        route.Add(from);
        if (bridge && bridge.Centerline != null && bridge.Centerline.Length > 1)
        {
            var deck = bridge.Centerline;
            bool forward = FlatDistance(to, deck[deck.Length - 1]) <= FlatDistance(to, deck[0]);
            for (int i = 0; i < deck.Length; i++)
                route.Connect(deck[forward ? i : deck.Length - 1 - i]);
        }
        else
        {
            var road = RoadWaypoints(from, to);
            if (road != null)
                foreach (var point in road) route.Connect(point);
        }
        route.Connect(to);
        if (route.points.Count < 2) return null;
        route.PrepareFlight(bridge ? 15f : 27f, bridge ? 1.5f : 70f, bridge ? 8 : 26, canopies);
        route.PreparePace();
        return route;
    }

    public static List<Vector3> RoadWaypoints(Vector3 from, Vector3 to)
    {
        var roads = RoadBuild.Roads;
        if (roads.Count == 0) return null;
        var network = new List<Vector3>();
        var neighbors = new List<List<int>>();
        var firstNodes = new List<int>(roads.Count);
        foreach (var road in roads)
        {
            int first = network.Count;
            int junction = first == 0 ? -1 : FindJunction(road, network, firstNodes);
            firstNodes.Add(first);
            for (int i = 0; i < road.x.Length; i++)
            {
                network.Add(TerrainMath.U(road.x[i], road.prof[i], road.z[i]));
                var links = new List<int>(3);
                neighbors.Add(links);
                int previous = i == 0 ? junction : network.Count - 2;
                if (previous < 0) continue;
                neighbors[previous].Add(network.Count - 1);
                links.Add(previous);
            }
        }

        int start = Nearest(network, from);
        int end = Nearest(network, to);
        var parent = new int[network.Count];
        System.Array.Fill(parent, -1);
        parent[end] = end;
        var queue = new Queue<int>();
        queue.Enqueue(end);
        while (queue.Count > 0 && parent[start] < 0)
        {
            int at = queue.Dequeue();
            foreach (int next in neighbors[at])
            {
                if (parent[next] >= 0) continue;
                parent[next] = at;
                queue.Enqueue(next);
            }
        }
        if (parent[start] < 0) return null;
        var result = new List<Vector3>();
        for (int at = start; ; at = parent[at])
        {
            result.Add(network[at]);
            if (at == end) break;
        }
        return result;
    }

    static int FindJunction(RoadBuild.GradedRoad branch, List<Vector3> network, List<int> firstNodes)
    {
        Vector3 entrance = TerrainMath.U(branch.x[0], 0, branch.z[0]);
        var rawBranch = RoadNet.Paths.Find(road => road.name == branch.name);
        if (rawBranch == null) return Nearest(network, entrance);
        // Grading smooths the branches, so use the original shared cell to identify the parent road.
        for (int i = 0; i < firstNodes.Count; i++)
        {
            var parent = RoadNet.Paths.Find(road => road.name == RoadBuild.Roads[i].name);
            if (parent == null) continue;
            double x = RoadNet.X(rawBranch.startCell);
            double z = RoadNet.Z(rawBranch.startCell);
            for (int j = 0; j < parent.x.Length; j++)
            {
                if (parent.x[j] != x || parent.z[j] != z) continue;
                int end = i + 1 < firstNodes.Count ? firstNodes[i + 1] : network.Count;
                return Nearest(network, entrance, firstNodes[i], end);
            }
        }
        return Nearest(network, entrance);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        var delta = a - b;
        return delta.x * delta.x + delta.z * delta.z;
    }

    static int Nearest(List<Vector3> points, Vector3 position, int start = 0, int end = -1)
    {
        int closest = start;
        if (end < 0) end = points.Count;
        float best = float.PositiveInfinity;
        for (int i = start; i < end; i++)
        {
            float distance = FlatDistance(position, points[i]);
            if (distance >= best) continue;
            best = distance;
            closest = i;
        }
        return closest;
    }

    void Connect(Vector3 target)
    {
        Vector3 from = points[points.Count - 1];
        int samples = Mathf.CeilToInt(Mathf.Sqrt(FlatDistance(from, target)) / 2f);
        for (int i = 1; i <= samples; i++) Add(Vector3.Lerp(from, target, (float)i / samples));
    }

    void Add(Vector3 point)
    {
        distances.Add(points.Count == 0 ? 0f : Length + Mathf.Sqrt(FlatDistance(point, points[points.Count - 1])));
        points.Add(point);
    }

    void PrepareFlight(float clearance, float maxCornerCut, int radius, IReadOnlyList<Bounds> canopies)
    {
        // Round junctions in metres so short segments and long connectors get the same treatment.
        int count = Mathf.Max(2, Mathf.CeilToInt(Length / 2f) + 1);
        var original = new Vector3[count];
        for (int i = 0; i < count; i++) original[i] = At(Length * i / (count - 1));
        points.Clear();
        distances.Clear();
        Add(original[0]);
        for (int i = 1; i < count - 1; i++)
        {
            Vector3 average = Vector3.zero;
            for (int offset = -radius; offset <= radius; offset++)
                average += original[Mathf.Clamp(i + offset, 0, count - 1)] * (radius + 1 - Mathf.Abs(offset));
            average /= (radius + 1) * (radius + 1);
            Vector3 lateral = average - original[i];
            lateral.y = 0f;
            Vector3 point = original[i] + Vector3.ClampMagnitude(lateral, maxCornerCut);
            point.y = average.y;
            Add(point);
        }
        Add(original[count - 1]);

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            p.y = Mathf.Max(p.y, TerrainProbe.SeaLevel, TerrainProbe.GroundY(p, p.y)) + clearance;
            for (int j = 0; j < canopies.Count; j++)
            {
                Bounds canopy = canopies[j];
                Vector3 difference = canopy.center - p;
                Vector3 extents = canopy.extents;
                if (Mathf.Abs(difference.x) > extents.x || Mathf.Abs(difference.z) > extents.z) continue;
                p.y = Mathf.Max(p.y, canopy.max.y + 3f);
            }
            points[i] = p;
        }
        for (int i = 1; i < points.Count; i++) RaiseToward(i, i - 1);
        for (int i = points.Count - 2; i >= 0; i--) RaiseToward(i, i + 1);

        // Average the altitude envelope, preserving the clearance checks underneath each sample.
        var heights = points.ToArray();
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 point = points[i];
            float sum = 0f;
            for (int offset = -6; offset <= 6; offset++)
                sum += heights[Mathf.Clamp(i + offset, 0, heights.Length - 1)].y;
            point.y = Mathf.Max(point.y, sum / 13f);
            points[i] = point;
        }
        for (int i = 1; i < points.Count; i++)
            distances[i] = distances[i - 1] + Mathf.Max(.001f, Vector3.Distance(points[i - 1], points[i]));

        tangents = new Vector3[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            int before = Mathf.Max(0, i - 1);
            int after = Mathf.Min(points.Count - 1, i + 1);
            tangents[i] = (points[after] - points[before]) / (distances[after] - distances[before]);
        }
    }

    // Pinned-end triangular smoothing of the plan view (heights are rebuilt afterwards from clearance).
    void SmoothPlan(int radius)
    {
        var src = points.ToArray(); int n = src.Length;
        for (int i = 1; i < n - 1; i++)
        {
            Vector3 sum = Vector3.zero; float w = 0f;
            for (int o = -radius; o <= radius; o++) { float k = radius + 1 - Mathf.Abs(o); sum += src[Mathf.Clamp(i + o, 0, n - 1)] * k; w += k; }
            Vector3 a = sum / w; a.y = src[i].y; points[i] = a;
        }
        distances.Clear(); distances.Add(0f);
        for (int i = 1; i < n; i++) distances.Add(distances[i - 1] + Mathf.Sqrt(FlatDistance(points[i], points[i - 1])));
    }

    void PreparePace()
    {
        const float cruiseSpeed = 105f;
        paces = new float[points.Count];
        travelTimes = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            float d = distances[i];
            Vector3 before = At(d) - At(d - 16f);
            Vector3 after = At(d + 16f) - At(d);
            before.y = after.y = 0f;
            float bend = Mathf.InverseLerp(18f, 100f, Vector3.Angle(before, after));
            paces[i] = Mathf.SmoothStep(1f, .34f, bend);
        }
        for (int i = 1; i < points.Count; i++) LimitPace(i, i - 1);
        for (int i = points.Count - 2; i >= 0; i--) LimitPace(i, i + 1);
        for (int i = 1; i < points.Count; i++)
            travelTimes[i] = travelTimes[i - 1] + (distances[i] - distances[i - 1]) /
                (cruiseSpeed * (paces[i] + paces[i - 1]) * .5f);
    }

    void LimitPace(int index, int neighbor)
    {
        float distance = Mathf.Abs(distances[index] - distances[neighbor]);
        paces[index] = Mathf.Min(paces[index], paces[neighbor] + distance / 44f);
    }

    public float DistanceAtTime(float fraction, out float pace)
    {
        float time = Mathf.Clamp01(fraction) * FlightTime;
        int next = System.Array.BinarySearch(travelTimes, time);
        if (next >= 0)
        {
            pace = paces[next];
            return distances[next];
        }

        next = ~next;
        int previous = next - 1;
        float t = Mathf.InverseLerp(travelTimes[previous], travelTimes[next], time);
        float start = paces[previous], end = paces[next];
        pace = Mathf.Lerp(start, end, t);
        // Integrate the linearly changing pace. Position and speed agree across sample boundaries.
        float progress = (2f * start * t + (end - start) * t * t) / (start + end);
        return Mathf.Lerp(distances[previous], distances[next], progress);
    }

    void RaiseToward(int index, int neighbor)
    {
        Vector3 p = points[index];
        p.y = Mathf.Max(p.y, points[neighbor].y - .50f * Mathf.Abs(distances[index] - distances[neighbor]));
        points[index] = p;
    }

    public Vector3 At(float distance)
    {
        distance = Mathf.Clamp(distance, 0f, Length);
        int next = distances.BinarySearch(distance);
        if (next >= 0) return points[next];
        next = ~next;
        int previous = next - 1;
        float span = distances[next] - distances[previous];
        float t = (distance - distances[previous]) / span;
        Vector3 a = points[previous];
        Vector3 b = points[next];
        if (tangents == null) return Vector3.Lerp(a, b, t);
        return Vector3.LerpUnclamped(a, b, t * t * (3f - 2f * t)) +
            span * (1f - t) * t * (tangents[previous] * (1f - t) - tangents[next] * t);
    }

    public Vector3 Heading(float distance, float look = 8f)
    {
        Vector3 direction = At(distance + look) - At(distance - look);
        direction.y = 0;
        return direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
    }
}
