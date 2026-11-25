using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Boards the companion, flies the active quest leg, then returns control on dry ground.</summary>
internal sealed class MountainStarRide : MonoBehaviour
{
    Transform star, child, body;
    IslandThirdPersonController player;
    ProceduralFollow follow;
    ChildPerformer performer;
    AmberOrbBody spring;
    StarOrbFX effects;
    CharacterController starController, childController;
    CutsceneDirector cameraDirector;
    Coroutine sequence;
    Vector3 startStar, startChild;
    Quaternion startFacing, startChildFacing, bodyRotation;
    Transform savedLook;
    Vector3? savedLookPoint;
    ChildEmotion savedEmotion;
    bool followEnabled, starControllerEnabled, childControllerEnabled, springEnabled, wasSeated;
    float hipHeight, riderLighting, rideWake;
    float? savedSeatWeight;
    Volume blurVolume;
    VolumeProfile blurProfile;
    MotionBlur motionBlur;
    float cameraDistance, canopyYaw;
    readonly List<Bounds> canopies = new List<Bounds>();
    readonly List<Bounds> nearbyCanopies = new List<Bounds>(16);
    static readonly float[] CameraAlternates = { 0f, 24f, -24f, 49f, -49f };
    float flightYaw, cameraYaw, flightBank;
    bool skip;
    int shotMask;

    public bool Active => cameraDirector;
    public bool Ready => body && player && player.isActiveAndEnabled && follow && performer &&
        performer.isActiveAndEnabled && child.gameObject.activeInHierarchy;

    public void Bind(Transform rider, Transform mount)
    {
        star = mount;
        child = rider;
        player = star.GetComponent<IslandThirdPersonController>();
        follow = child.GetComponent<ProceduralFollow>();
        performer = child.GetComponent<ChildPerformer>();
        spring = star.GetComponentInChildren<AmberOrbBody>();
        body = spring ? spring.body : null;
        effects = star.GetComponentInChildren<StarOrbFX>();
        starController = star.GetComponent<CharacterController>();
        childController = child.GetComponent<CharacterController>();
        shotMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("TreeOcclusion", "Water");
    }

    public bool TryBegin(Vector3 destination, BridgeWalkSurface bridge, Action arrived)
    {
        if (Active || !Ready || CutsceneDirector.Active || !Camera.main) return false;
        Vector3 forward = FlatDirection(child.position, destination);
        Vector3 dock = FindDock(forward);
        if (!TryFindLanding(destination, forward, out Vector3 landing, out Vector3 footfall)) return false;
        canopies.Clear();
        var trees = FindFirstObjectByType<IslandStylizedTrees>();
        if (trees) trees.CollectCanopies(canopies);
        var route = MountainRideRoute.Build(dock, landing, bridge, canopies);
        if (route == null) return false;
        sequence = StartCoroutine(Fly(route, dock, landing, footfall, arrived));
        return true;
    }

    Vector3 FindDock(Vector3 forward)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 direction = Quaternion.Euler(0, i * 45f, 0) * forward;
            if (TryStand(child.position + direction * 1.05f, starController, out Vector3 dock) &&
                Mathf.Abs(dock.y - child.position.y) < .6f)
                return dock;
        }
        return child.position;
    }

    bool TryFindLanding(Vector3 goal, Vector3 forward, out Vector3 landing, out Vector3 footfall)
    {
        foreach (float radius in new[] { 2.25f, 1.5f, .75f, 3.5f, 5f, 7f, 9f })
        {
            for (int i = 0; i < 12; i++)
            {
                Vector3 outward = Quaternion.Euler(0, i * 30f, 0) * -forward;
                if (!TryStand(goal + outward * radius, starController, out landing)) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 offset = Quaternion.Euler(0, side * 65f, 0) * outward * 1.55f;
                    if (TryStand(landing + offset, childController, out footfall) &&
                        Mathf.Abs(footfall.y - landing.y) < .65f) return true;
                }
            }
        }
        landing = footfall = default;
        return false;
    }

    bool TryStand(Vector3 near, CharacterController controller, out Vector3 position)
    {
        position = near;
        if (!TerrainProbe.TryGetGroundY(near.x, near.z, out float floor) || floor < TerrainProbe.SeaLevel) return false;
        position.y = floor + .10f;
        position = CutsceneDirector.SupportPoint(position);
        float radius = controller.radius;
        Vector3 low = position + Vector3.up * (radius + .10f);
        Vector3 high = position + Vector3.up * Mathf.Max(radius + .10f, controller.height - radius);
        foreach (var collider in Physics.OverlapCapsule(low, high, radius, shotMask, QueryTriggerInteraction.Ignore))
        {
            if (TerrainProbe.IsTerrain(collider) || collider.transform.IsChildOf(star) || collider.transform.IsChildOf(child))
                continue;
            return false;
        }
        return true;
    }

    IEnumerator Fly(MountainRideRoute route, Vector3 dock, Vector3 landing, Vector3 footfall, Action arrived)
    {
        Capture();
        CutsceneSkip.Begin(() => skip = true);

        var camera = Camera.main;
        Vector3 oldCamera = camera.transform.position;
        Vector3 oldFocus = oldCamera + camera.transform.forward * 8f;
        float oldFov = camera.fieldOfView;
        Vector3 boardDirection = FlatDirection(startChild, dock);
        Vector3 launchDirection = route.Heading(0f);
        Vector3 endDirection = route.Heading(route.Length);
        Quaternion faceMount = Quaternion.LookRotation(boardDirection);
        Quaternion startRideFacing = Quaternion.LookRotation(launchDirection);
        Vector3 boardingFocus = startChild + Vector3.up * .9f + Vector3.ClampMagnitude(dock - startChild, 2f) * .35f;
        float dockDistance = Vector3.Distance(startStar, dock);
        float dockTime = Mathf.Clamp(dockDistance / 23f, 1.05f, 3.5f);

        // The star keeps its shape and dips slightly under the child's weight.
        yield return Animate(dockTime, t =>
        {
            float eased = Ease(t);
            Vector3 position = Vector3.Lerp(startStar, dock, eased);
            float lift = Mathf.Sin(eased * Mathf.PI) * Mathf.Clamp(dockDistance * .18f, .12f, 15f);
            position.y = Mathf.Max(position.y, TerrainProbe.GroundY(position, position.y)) + lift;
            star.position = position;
            child.rotation = Quaternion.Slerp(startChildFacing, faceMount, eased);
            if (effects) effects.RiderLighting = Mathf.Lerp(riderLighting, 1f, eased);
            Frame(boardingFocus, launchDirection, Boarding, 0f, oldCamera, oldFocus, oldFov,
                dockDistance < 18f ? Ease(t / .8f) : 1f);
        });

        performer.Seated = true;
        yield return Animate(1.22f, t =>
        {
            float step = Ease((t - .07f) / .79f);
            float settle = Ease((t - .35f) / .54f);
            performer.RideSeatWeight = settle;
            star.position = dock - Vector3.up * (.20f * Mathf.Sin(settle * Mathf.PI));
            Vector3 seat = Seat(startRideFacing);
            float hop = .24f * Mathf.Sin(step * Mathf.PI);
            child.SetPositionAndRotation(Vector3.Lerp(startChild, seat, step) + Vector3.up * hop,
                Quaternion.Slerp(faceMount, startRideFacing, Ease((t - .18f) / .63f)));
            Frame(Vector3.Lerp(boardingFocus, RiderFocus, step), launchDirection,
                Shot.Lerp(Boarding, LiftOff, Ease(t)), 0f);
        });

        Vector3 cruiseStart = route.At(0f);
        Vector3 cruiseEnd = route.At(route.Length);
        flightYaw = cameraYaw = startRideFacing.eulerAngles.y;
        flightBank = 0f;
        BeginBlur(camera);
        float duration = Mathf.Clamp(route.FlightTime + 4.4f, 5f, 25f);
        yield return Animate(duration, t =>
        {
            // One continuous arc: climb starts before forward motion and ends during acceleration.
            float travel = Mathf.InverseLerp(.035f, .965f, t);
            float distance = route.DistanceAtTime(Ease(travel), out float cornerPace);
            float speed = 16f * travel * travel * (1f - travel) * (1f - travel) * cornerPace;
            float climb = Ease(t / .24f);
            float descent = Ease((t - .81f) / .19f) * Ease(1f - (route.Length - distance) / 32f);
            Vector3 position = route.At(distance);
            position.y -= (cruiseStart.y - dock.y) * (1f - climb) + (cruiseEnd.y - landing.y) * descent;
            float ground = TerrainProbe.GroundY(position, Mathf.Min(dock.y, landing.y) - 1f);
            position.y = Mathf.Max(position.y, ground + .05f * (1f - descent) + 3f * climb * (1f - descent));

            Vector3 horizon = route.Heading(distance);
            Vector3 ahead = route.Heading(distance + 10f);
            float elapsed = Mathf.Max(Time.deltaTime, .001f);
            flightYaw = Mathf.LerpAngle(flightYaw, Yaw(horizon), 1f - Mathf.Exp(-9f * elapsed));
            cameraYaw = Mathf.LerpAngle(cameraYaw, Yaw(route.Heading(distance, 22f)), 1f - Mathf.Exp(-3.4f * elapsed));
            float bank = Mathf.Clamp(-Vector3.SignedAngle(horizon, ahead, Vector3.up) * .65f, -16f, 16f);
            flightBank = Mathf.Lerp(flightBank, bank * speed, 1f - Mathf.Exp(-6f * elapsed));
            float finish = Ease((t - .88f) / .12f);
            flightYaw = Mathf.LerpAngle(flightYaw, Yaw(endDirection), finish);
            cameraYaw = Mathf.LerpAngle(cameraYaw, Yaw(endDirection), finish);

            Vector3 forward = Quaternion.Euler(0f, flightYaw, 0f) * Vector3.forward;
            Quaternion attitude = Quaternion.Euler(0f, flightYaw, flightBank);
            star.SetPositionAndRotation(position, Quaternion.Euler(0f, flightYaw, 0f));
            body.localRotation = Quaternion.Euler(0f, 0f, flightBank) * bodyRotation;
            child.SetPositionAndRotation(Seat(attitude), Quaternion.Euler(0f, flightYaw, flightBank * .7f));
            performer.lookPoint = position + forward * 20f + Vector3.up * 2f;
            if (effects) effects.RideWake = Mathf.Max(rideWake, speed);
            if (motionBlur) motionBlur.intensity.value = .34f * Mathf.SmoothStep(0f, 1f, speed);
            Frame(RiderFocus, Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward, FlightShot(t), -flightBank * .24f);
        });
        EndBlur();

        star.position = landing;
        body.localRotation = bodyRotation;
        child.SetPositionAndRotation(Seat(Quaternion.LookRotation(endDirection)), Quaternion.LookRotation(endDirection));
        Vector3 dismount = child.position;
        Quaternion dismountFacing = Quaternion.LookRotation(FlatDirection(dismount, footfall));
        Quaternion landedFacing = child.rotation;
        Vector3 landedFocus = RiderFocus;
        yield return Animate(1.1f, t =>
        {
            float step = Ease(t);
            performer.RideSeatWeight = 1f - Ease((t - .20f) / .67f);
            child.SetPositionAndRotation(Vector3.Lerp(dismount, footfall, step) +
                Vector3.up * (.20f * Mathf.Sin(step * Mathf.PI)), Quaternion.Slerp(landedFacing, dismountFacing, step));
            if (effects) effects.RiderLighting = Mathf.Lerp(1f, riderLighting, step);
            Vector3 focus = Vector3.Lerp(landedFocus, (body.position + footfall + Vector3.up * .9f) * .5f, step);
            Frame(focus, endDirection, Shot.Lerp(Arrival, Dismount, step), 0f);
        });

        child.SetPositionAndRotation(footfall, dismountFacing);
        Restore(true, landing, endDirection);
        arrived();
    }

    void Capture()
    {
        startStar = star.position;
        startFacing = star.rotation;
        startChild = child.position;
        startChildFacing = child.rotation;
        bodyRotation = body.localRotation;
        hipHeight = performer.StandingHipHeight;
        wasSeated = performer.Seated;
        savedSeatWeight = performer.RideSeatWeight;
        performer.RideSeatWeight = 0f;
        cameraDistance = canopyYaw = 0f;
        savedEmotion = performer.emotion;
        savedLook = performer.lookTarget;
        savedLookPoint = performer.lookPoint;
        performer.lookTarget = null;
        performer.lookPoint = null;
        performer.emotion = ChildEmotion.Warm;
        followEnabled = follow.enabled;
        starControllerEnabled = starController.enabled;
        childControllerEnabled = childController.enabled;
        springEnabled = spring.enabled;
        if (effects)
        {
            rideWake = effects.RideWake;
            riderLighting = effects.RiderLighting;
        }
        cameraDirector = CutsceneDirector.Begin();
        follow.enabled = childController.enabled = starController.enabled = spring.enabled = false;
    }

    void Restore(bool landed, Vector3 position, Vector3 forward)
    {
        if (!Active) return;
        CutsceneSkip.End();
        EndBlur();
        if (body) body.localRotation = bodyRotation;
        if (effects)
        {
            effects.RideWake = rideWake;
            effects.RiderLighting = riderLighting;
        }
        if (star && player)
        {
            float yaw = landed ? Quaternion.LookRotation(forward).eulerAngles.y : startFacing.eulerAngles.y;
            player.Teleport(position, yaw);
        }
        if (!landed && child) child.SetPositionAndRotation(startChild, startChildFacing);
        if (starController) starController.enabled = starControllerEnabled;
        if (childController) childController.enabled = childControllerEnabled;
        if (follow) follow.enabled = followEnabled;
        if (spring) spring.enabled = springEnabled;
        if (performer)
        {
            performer.Seated = wasSeated;
            performer.RideSeatWeight = savedSeatWeight;
            performer.emotion = savedEmotion;
            performer.lookTarget = savedLook;
            performer.lookPoint = savedLookPoint;
        }
        cameraDirector.End(landed ? 1.35f : 0f);
        cameraDirector = null;
        sequence = null;
        skip = false;
    }

    void OnDisable() => Cancel();

    public void Cancel()
    {
        if (!Active) return;
        if (sequence != null) StopCoroutine(sequence);
        Restore(false, startStar, startFacing * Vector3.forward);
    }

    Vector3 RiderFocus => body.position + Vector3.up * .47f;

    Vector3 Seat(Quaternion attitude) =>
        body.position + attitude * new Vector3(0f, body.lossyScale.y * .5f + .04f - hipHeight, .16f);

    static float Yaw(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

    readonly struct Shot
    {
        public readonly float Yaw, Radius, Height, Lens, Lead;

        public Shot(float yaw, float radius, float height, float lens, float lead = 0f)
        {
            Yaw = yaw; Radius = radius; Height = height; Lens = lens; Lead = lead;
        }

        public static Shot Lerp(Shot a, Shot b, float t) => new Shot(
            Mathf.Lerp(a.Yaw, b.Yaw, t), Mathf.Lerp(a.Radius, b.Radius, t),
            Mathf.Lerp(a.Height, b.Height, t), Mathf.Lerp(a.Lens, b.Lens, t), Mathf.Lerp(a.Lead, b.Lead, t));
    }

    // Clockwise orbit: face the child, sweep behind for launch, then swing wide across the valley.
    static readonly Shot Boarding = new Shot(54f, 4.1f, 1.05f, 39f);
    static readonly Shot LiftOff = new Shot(86f, 4.8f, 1.35f, 40f);
    static readonly Shot Chase = new Shot(152f, 11.5f, 4f, 51f, 2.4f);
    static readonly Shot Landscape = new Shot(235f, 18f, 8.5f, 52f, 2.15f);
    static readonly Shot FlyBy = new Shot(277f, 10.5f, 5.25f, 44f, 1f);
    static readonly Shot Arrival = new Shot(310f, 5.5f, 1.75f, 41f);
    static readonly Shot Dismount = new Shot(329f, 4.7f, 1.4f, 40f);

    static Shot FlightShot(float t)
    {
        if (t < .26f) return Shot.Lerp(LiftOff, Chase, Ease(t / .26f));
        if (t < .52f) return Shot.Lerp(Chase, Landscape, Ease((t - .26f) / .26f));
        if (t < .76f) return Shot.Lerp(Landscape, FlyBy, Ease((t - .52f) / .24f));
        return Shot.Lerp(FlyBy, Arrival, Ease((t - .76f) / .24f));
    }

    void Frame(Vector3 focus, Vector3 forward, Shot shot, float roll,
        Vector3 oldCamera = default, Vector3 oldFocus = default, float oldFov = 42f, float reveal = 1f)
    {
        Vector3 offset = Quaternion.AngleAxis(shot.Yaw, Vector3.up) * forward * shot.Radius + Vector3.up * shot.Height;
        float radius = offset.magnitude;
        nearbyCanopies.Clear();
        foreach (var canopy in canopies)
            if (canopy.SqrDistance(focus) < radius * radius && !canopy.Contains(focus)) nearbyCanopies.Add(canopy);
        float best = float.NegativeInfinity;
        float goal = 0f;
        foreach (float alternate in CameraAlternates)
        {
            Vector3 candidate = Quaternion.AngleAxis(alternate, Vector3.up) * offset.normalized;
            float fraction = AvailableCameraDistance(focus, candidate, radius) / radius;
            if (alternate == 0f && fraction >= .999f) break;
            float score = fraction
                - .002f * Mathf.Abs(alternate) - .001f * Mathf.Abs(alternate - canopyYaw);
            if (score <= best) continue;
            best = score;
            goal = alternate;
        }
        canopyYaw = Mathf.Lerp(canopyYaw, goal, 1f - Mathf.Exp(-8f * Time.deltaTime));
        Vector3 direction = Quaternion.AngleAxis(canopyYaw, Vector3.up) * offset.normalized;
        float allowed = AvailableCameraDistance(focus, direction, radius);
        // Retract immediately for an obstruction, then ease back out to the intended framing.
        if (cameraDistance <= 0f) cameraDistance = allowed;
        cameraDistance = Mathf.Min(allowed, Mathf.Lerp(cameraDistance, allowed, 1f - Mathf.Exp(-7f * Time.deltaTime)));
        Vector3 desired = focus + direction * cameraDistance;
        desired.y = Mathf.Max(desired.y, TerrainProbe.GroundY(desired, desired.y - .5f) + .45f);
        cameraDirector.TrackFrame(Vector3.Lerp(oldCamera, desired, reveal),
            Vector3.Lerp(oldFocus, focus + forward * shot.Lead, reveal),
            Mathf.Lerp(oldFov, shot.Lens, reveal), roll);
    }

    float AvailableCameraDistance(Vector3 focus, Vector3 direction, float distance)
    {
        var ray = new Ray(focus, direction);
        foreach (var canopy in nearbyCanopies)
            if (canopy.IntersectRay(ray, out float hit) && hit < distance) distance = Mathf.Max(.65f, hit - .45f);
        if (Physics.SphereCast(focus, .25f, direction, out RaycastHit solid, distance, shotMask, QueryTriggerInteraction.Ignore))
            distance = Mathf.Max(.65f, solid.distance - .25f);
        return distance;
    }

    void BeginBlur(Camera camera)
    {
        if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) || !data.renderPostProcessing) return;
        // Use a ride-only override on a layer seen by this camera.
        int mask = data.volumeLayerMask.value;
        if (mask == 0) return;
        var volumeObject = new GameObject("Star ride motion blur");
        volumeObject.transform.SetParent(transform, false);
        for (int layer = 0; layer < 32; layer++)
        {
            if ((mask & (1 << layer)) == 0) continue;
            volumeObject.layer = layer;
            break;
        }
        blurVolume = volumeObject.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 60f;
        blurProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        blurVolume.sharedProfile = blurProfile;
        motionBlur = blurProfile.Add<MotionBlur>(true);
        motionBlur.mode.Override(MotionBlurMode.CameraAndObjects);
        motionBlur.quality.Override(MotionBlurQuality.Medium);
        motionBlur.clamp.Override(.019f);
    }

    void EndBlur()
    {
        if (!blurVolume) return;
        blurVolume.enabled = false;
        Destroy(blurVolume.gameObject);
        Destroy(motionBlur);
        Destroy(blurProfile);
        blurVolume = null;
        motionBlur = null;
        blurProfile = null;
    }

    IEnumerator Animate(float seconds, Action<float> frame)
    {
        float elapsed = 0f;
        while (!skip && elapsed < seconds)
        {
            frame(elapsed / seconds);
            yield return null;
            elapsed += Time.deltaTime;
        }
        frame(1f);
    }

    static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    static Vector3 FlatDirection(Vector3 a, Vector3 b)
    {
        Vector3 direction = b - a;
        direction.y = 0;
        return direction.sqrMagnitude > .001f ? direction.normalized : Vector3.forward;
    }
}

