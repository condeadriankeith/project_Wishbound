using UnityEngine;

/// <summary>Ambient village animals travel within their own patch and pause to graze, peck or watch.</summary>
public sealed class VillageAnimalMotion : MonoBehaviour
{
    public enum Species { Hen, Cat, Cow, Sheep }
    Species species;
    Transform model, body, head, tail;
    Transform[] legs;
    Quaternion bodyRest, headRest, tailRest;
    Quaternion[] legRest;
    Vector3 home, target, modelRest;
    Vector2 limits;
    float nextChoice, phase, walk, idle, gait;
    int motionSeed;
    System.Random random;
    bool resting, openRange;
    Renderer[] renderers;
    bool visible = true;

    public void Configure(Species type, Transform visual, Vector3 localHome, Vector2 roamingRange, int seed, bool outdoors=false)
    {
        species = type;
        WorldSound.Repeat(transform, type.ToString(), 38f);
        openRange=outdoors;
        model = visual;
        home = target = localHome;
        limits = roamingRange;
        motionSeed = seed;
        random = new System.Random(seed);
        phase = (float)random.NextDouble() * 6.28f;
        nextChoice = Time.time + 1 + phase * .4f;
        body = visual.Find("Body");
        head = visual.Find("Head");
        tail = visual.Find("Tail");
        legs = type == Species.Hen
            ? new[] { visual.Find("Leg_L"), visual.Find("Leg_R") }
            : new[] { visual.Find("Leg_FL"), visual.Find("Leg_FR"), visual.Find("Leg_BL"), visual.Find("Leg_BR") };
        legRest = new Quaternion[legs.Length];
        for (int i = 0; i < legs.Length; i++) legRest[i] = legs[i].localRotation;
        bodyRest = body.localRotation;
        headRest = head.localRotation;
        tailRest = tail.localRotation;
        modelRest = visual.localPosition;
        renderers = visual.GetComponentsInChildren<Renderer>();
    }

    static bool OpenGround(Vector3 point,Vector3 from)
    {
        if (!TerrainProbe.TryGetGroundY(point.x,point.z,out float y) || y<TerrainProbe.SeaLevel+.6f) return false;
        float z=TerrainMath.MirrorZ?-point.z:point.z;
        if (TerrainMath.NVm(point.x,z).y<.80f || Mathf.Abs(y-from.y)>2f ||
            GrassExclusions.Contains(point.x,z,y) || WaterProbe.Near(new Vector3(point.x,y,point.z),.45f,.8f)) return false;
        return !Physics.CheckSphere(new Vector3(point.x,y+.65f,point.z),.31f,~0,QueryTriggerInteraction.Ignore);
    }

    float stepDistance;
    void Update()
    {
        if (!model) return;
        var camera = Camera.main;
        bool near = !camera || (camera.transform.position - transform.position).sqrMagnitude < 135 * 135;
        if (near != visible)
        {
            visible = near;
            foreach (var renderer in renderers) renderer.enabled = visible;
        }
        if (!near && !openRange) return;

        float dt = Time.deltaTime, now = Time.time;
        if (now >= nextChoice)
        {
            // Unity can reload the script during a playtest without serializing System.Random.
            random ??= new System.Random(motionSeed);
            resting = !resting;
            nextChoice = now + (resting ? 1.2f : openRange ? 9f : 4f) + (float)random.NextDouble() * 3;
            if (!resting)
            {
                float x = (float)random.NextDouble() * 2 - 1;
                float z = (float)random.NextDouble() * 2 - 1;
                var candidate=home+new Vector3(x*limits.x,0,z*limits.y);
                if (!openRange || OpenGround(transform.parent.TransformPoint(candidate),transform.position)) target=candidate;
            }
        }

        Vector3 delta = target - transform.localPosition;
        delta.y = 0;
        bool moving = !resting && delta.sqrMagnitude > .025f;
        if (moving)
        {
            Vector3 worldForward = transform.parent.TransformDirection(delta);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(worldForward), dt * 120);
            float speed = species == Species.Cat ? 1.05f : species == Species.Cow ? .63f : .76f;
            Vector3 step = Vector3.MoveTowards(transform.localPosition, target, speed * dt);
            Vector3 position=VillageKit.Ground(transform.parent.TransformPoint(step));
            if (!openRange || OpenGround(position,transform.position))
            {
                stepDistance += Vector3.ProjectOnPlane(position - transform.position, Vector3.up).magnitude;
                transform.position = position;
                if (stepDistance >= (species == Species.Cow ? .85f : .5f))
                {
                    WorldSound.Cue(transform, "AnimalStep", 7f);
                    stepDistance = 0;
                }
            }
            else { target=transform.localPosition; nextChoice=now+.7f; }
        }

        walk = Mathf.MoveTowards(walk, moving ? 1 : 0, dt * 5);
        idle = Mathf.MoveTowards(idle, resting ? 1 : 0, dt * 2);
        gait += dt * (species == Species.Hen ? 12 : 7);
        float swing = Mathf.Sin(gait);
        float t = now + phase;
        float bob = Mathf.Abs(swing) * walk * (species == Species.Hen ? .038f : .025f);
        model.localPosition = modelRest + Vector3.up * bob;
        // FBX pivots retain Blender's axes at import: X is lateral, Z is up.
        body.localRotation = bodyRest * Quaternion.Euler(0, swing * walk * 2.5f, 0);
        for (int i = 0; i < legs.Length; i++)
        {
            float direction = i == 0 || i == 3 ? 1 : -1;
            legs[i].localRotation = legRest[i] * Quaternion.Euler(direction * swing * walk * 22, 0, 0);
        }
        float dip = species == Species.Hen ? Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * 5)), 3) * 49
            : species == Species.Cat ? -4 : 22 + Mathf.Sin(t * 2) * 8;
        head.localRotation = headRest * Quaternion.Euler(idle * dip - swing * walk * 4, 0, Mathf.Sin(t * 1.5f) * 7);
        tail.localRotation = tailRest * Quaternion.Euler(0, 0, Mathf.Sin(t * (species == Species.Cat ? 3 : 2)) * 14);
    }
}
