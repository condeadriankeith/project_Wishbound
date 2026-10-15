using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds complete, gap-free houses out of the real "Fantasy Medieval Town LITE"
/// modular kit (Assets/FantasyMedievalTown_LITE/Prefabs/).
///
/// All placement math below was derived by measuring the kit's own
/// hand-built reference prefab (House_01_LITE) directly — not guessed —
/// so a generated house is geometrically identical in construction technique
/// to the asset author's own example, just tiled to arbitrary width/depth.
///
/// Grid: every module is sized to a 2.5m x 2.5m horizontal cell.
///   - Cell (col, row) center, in the building's local space (building origin
///     at the footprint's horizontal center, Y=0 at ground level), is:
///       x = (col - (cols-1)/2) * CellSize
///       z = (row - (rows-1)/2) * CellSize
///   - Floor tiles sit one per cell at y = FloorY (0.6, on top of the base).
///   - Perimeter walls sit on the 4 outer cell edges at y = WallY (0.7, on
///     top of the base skirting), centered along each edge segment.
///   - HouseBase_03 (skirting) sits one per perimeter wall segment, at the
///     SAME horizontal position as the wall above it, y = 0. It is a
///     directional piece (like a wall), not a uniform floor tile.
///   - Corner_01 posts sit at the 4 true outer corners of the footprint,
///     y = 0, one rotation per corner (0/90/180/270 going around).
///   - The roof is a single piece pre-sized for a 2x2 cell building
///     (5.7m x 6.3m, matching 2 cells + eave overhang). For footprints
///     larger than 2x2 cells we tile multiple roof caps across 2x2 blocks
///     (see BuildRoof) rather than stretching the mesh, since stretching a
///     hand-modeled pitched roof would visibly distort it.
///   - Stairs are placed just outside the single door wall, along that
///     wall's outward normal.
///
/// Known, deliberate limitation: this LITE kit has no second-story wall
/// pieces and only one roof shape. A "building" taller than 1 story (per
/// BuildingSystem.Building.floors) is still built as a single story here —
/// floors only affects a mild per-building Y jitter / roof pairing for
/// scale variety, it does not stack extra wall rings. Trying to force
/// multi-story stacking out of a kit that doesn't include the pieces for
/// it is exactly how the previous implementation ended up glitchy.
/// </summary>
public static class ModularHouseBuilder
{
    public const float CellSize = 2.5f;
    public const float WallThickness = 0.3f;
    public const float BaseHeight = 0.7f;      // HouseBase_03 top surface = wall bottom
    public const float WallHeight = 3.0f;
    public const float FloorY = 0.6f;          // Floor_01 local Y in the reference house
    public const float WallY = 0.7f;           // wall pivot Y in the reference house
    public const float RoofFootprintCells = 2; // HouseRoof_02 covers exactly a 2x2 cell block

    const string Root = "Assets/FantasyMedievalTown_LITE/Prefabs/";
    const string PBase = Root + "HouseBase_03_LITE.prefab";
    const string PCorner = Root + "Corner_01_LITE.prefab";
    const string PFloor = Root + "Floor_01_LITE.prefab";
    const string PRoof = Root + "HouseRoof_02_LITE.prefab";
    const string PWallPlain = Root + "HouseWall_02_LITE.prefab";
    const string PWallDoor = Root + "HouseWall_08_LITE.prefab";
    const string PWallWindow = Root + "HouseWall_11_LITE.prefab";
    const string PStairs = Root + "Stairs_01_LITE.prefab";

    static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

#if UNITY_EDITOR
    static GameObject LoadViaAssetDatabase(string path)
    {
        if (!prefabCache.TryGetValue(path, out var go) || go == null)
        {
            go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            prefabCache[path] = go;
        }
        return go;
    }
#endif

    static GameObject Instantiate(string prefabPath, Transform parent, Vector3 localPos, float yawDeg)
    {
#if UNITY_EDITOR
        var prefab = LoadViaAssetDatabase(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[ModularHouseBuilder] Missing prefab: {prefabPath}");
            return null;
        }
        GameObject inst = UnityEditor.PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (inst == null) inst = GameObject.Instantiate(prefab);
#else
        var prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null) return null;
        GameObject inst = GameObject.Instantiate(prefab);
#endif
        inst.transform.SetParent(parent, false);
        inst.transform.localPosition = localPos;
        inst.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
        inst.transform.localScale = Vector3.one;
        // Glass panes are zero-thickness quads (localScale.z = 0) and decorative only; walls already
        // provide collision. Their BoxColliders spam "negative scale or size" warnings and cost physics.
        foreach (var bc in inst.GetComponentsInChildren<BoxCollider>(true))
        {
            if (bc.gameObject.name.StartsWith("Glass"))
                UnityEngine.Object.DestroyImmediate(bc);
        }
        return inst;
    }

    public enum WallKind { Plain, Door, Window }

    public struct HouseParams
    {
        public int cols;
        public int rows;
        public int doorEdge;          // 0=south(-Z) 1=east(+X) 2=north(+Z) 3=west(-X)
        public int doorIndexOnEdge;
        public int windowCount;
        public int seed;
    }

    public static GameObject Build(string name, Vector3 worldPos, float yawDeg, HouseParams hp)
    {
        hp.cols = Mathf.Max(2, hp.cols);
        hp.rows = Mathf.Max(2, hp.rows);
        hp.doorEdge = ((hp.doorEdge % 4) + 4) % 4;

        var root = new GameObject(name);
        root.transform.position = worldPos;
        root.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

        var rng = new System.Random(hp.seed);

        float edgeX = hp.cols * 0.5f * CellSize;
        float edgeZ = hp.rows * 0.5f * CellSize;

        float CellCenterX(int col) => (col - (hp.cols - 1) * 0.5f) * CellSize;
        float CellCenterZ(int row) => (row - (hp.rows - 1) * 0.5f) * CellSize;

        // 1. Floor tiles
        for (int c = 0; c < hp.cols; c++)
            for (int r = 0; r < hp.rows; r++)
                Instantiate(PFloor, root.transform, new Vector3(CellCenterX(c), FloorY, CellCenterZ(r)), 0f);

        // 2. Corners
        Instantiate(PCorner, root.transform, new Vector3(edgeX, 0f, edgeZ), 0f);
        Instantiate(PCorner, root.transform, new Vector3(-edgeX, 0f, edgeZ), 270f);
        Instantiate(PCorner, root.transform, new Vector3(edgeX, 0f, -edgeZ), 90f);
        Instantiate(PCorner, root.transform, new Vector3(-edgeX, 0f, -edgeZ), 180f);

        // 3. Decide wall kinds per edge slot
        int[] edgeLengths = { hp.cols, hp.rows, hp.cols, hp.rows };
        var wallKinds = new WallKind[4][];
        for (int e = 0; e < 4; e++) wallKinds[e] = new WallKind[edgeLengths[e]];

        int doorLen = edgeLengths[hp.doorEdge];
        int doorIdx = Mathf.Clamp(hp.doorIndexOnEdge, 0, doorLen - 1);
        wallKinds[hp.doorEdge][doorIdx] = WallKind.Door;

        var allSlots = new List<(int edge, int idx)>();
        for (int e = 0; e < 4; e++)
            for (int i = 0; i < edgeLengths[e]; i++)
                if (!(e == hp.doorEdge && i == doorIdx))
                    allSlots.Add((e, i));
        for (int i = allSlots.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (allSlots[i], allSlots[j]) = (allSlots[j], allSlots[i]);
        }
        int windows = Mathf.Clamp(hp.windowCount, 0, allSlots.Count);
        for (int i = 0; i < windows; i++)
            wallKinds[allSlots[i].edge][allSlots[i].idx] = WallKind.Window;

        // 4. Walls + base skirting
        Vector3 doorLocalPos = Vector3.zero;
        float doorFacingYaw = 0f;
        bool doorFound = false;

        for (int e = 0; e < 4; e++)
        {
            int len = edgeLengths[e];
            for (int i = 0; i < len; i++)
            {
                float along = (i - (len - 1) * 0.5f) * CellSize;
                Vector3 pos; float yaw;
                switch (e)
                {
                    case 0: pos = new Vector3(along, 0f, -edgeZ); yaw = 0f; break;
                    case 1: pos = new Vector3(edgeX, 0f, along); yaw = 90f; break;
                    case 2: pos = new Vector3(along, 0f, edgeZ); yaw = 180f; break;
                    default: pos = new Vector3(-edgeX, 0f, along); yaw = 270f; break;
                }

                string wallPrefab = wallKinds[e][i] switch
                {
                    WallKind.Door => PWallDoor,
                    WallKind.Window => PWallWindow,
                    _ => PWallPlain,
                };

                Instantiate(PBase, root.transform, pos, yaw);
                Instantiate(wallPrefab, root.transform, new Vector3(pos.x, WallY, pos.z), yaw);

                if (wallKinds[e][i] == WallKind.Door)
                {
                    doorLocalPos = pos;
                    doorFacingYaw = yaw;
                    doorFound = true;
                }
            }
        }

        // 5. Stairs outside the door
        if (doorFound)
        {
            Vector3 outward = Quaternion.Euler(0f, doorFacingYaw, 0f) * Vector3.back;
            Vector3 stairsPos = doorLocalPos + outward * (WallThickness * 0.5f + 0.125f);
            Instantiate(PStairs, root.transform, new Vector3(stairsPos.x, 0f, stairsPos.z), doorFacingYaw);
        }

        // 6. Roof
        BuildRoof(root.transform, hp.cols, hp.rows, CellCenterX, CellCenterZ);

        return root;
    }

    static void BuildRoof(Transform root, int cols, int rows, Func<int, float> cellCenterX, Func<int, float> cellCenterZ)
    {
        for (int c = 0; c < cols; c += 2)
        {
            for (int r = 0; r < rows; r += 2)
            {
                int cEnd = Mathf.Min(c + 1, cols - 1);
                int rEnd = Mathf.Min(r + 1, rows - 1);
                float cx = (cellCenterX(c) + cellCenterX(cEnd)) * 0.5f;
                float cz = (cellCenterZ(r) + cellCenterZ(rEnd)) * 0.5f;
                Instantiate(PRoof, root, new Vector3(cx, 0f, cz), 0f);
            }
        }
    }

    public static (int cols, int rows) SnapFootprintToGrid(Vector2 footprint)
    {
        int cols = Mathf.Clamp(Mathf.RoundToInt(footprint.x / CellSize), 2, 4);
        int rows = Mathf.Clamp(Mathf.RoundToInt(footprint.y / CellSize), 2, 4);
        return (cols, rows);
    }
}
