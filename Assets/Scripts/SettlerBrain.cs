using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A settler's everyday life. Each settler has a home (a house door), a role and a temperament, and keeps choosing
/// something sensible to do next:
///   Farmer / Gardener  - walks to a farm plot and hoes, waters, picks; fetches water from the well
///   Fisher             - sits on the pond bank with a rod
///   Wanderer           - strolls the paths, visits neighbours' doors, stops at the well, looks at the view
///   Elder              - potters slowly, sweeps the doorstep, sits and watches the world
///   Kid                - plays tag with other kids, skips around, tags along after grown-ups, cheers
/// Anyone may stop to chat when they meet a neighbour (a few words above their heads). When the Star comes close they
/// notice it: curious ones come over and wave, puzzled ones scratch their heads, timid ones back away, kids get
/// excited - and they get more used to it over time.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SettlerBrain : MonoBehaviour
{
    public enum Role { Farmer, Gardener, Fisher, Wanderer, Elder, Kid }
    public enum Temperament { Curious, Puzzled, Timid }

    public Role role; public Temperament temperament;
    public string settlerName;
    public Vector3 home;          // doorstep
    public float walkSpeed = 1.6f, runSpeed = 3.6f;
    public float height = 1.7f;   // for bubbles

    [System.NonSerialized] public SettlerRig rig;
    [System.NonSerialized] public SettlerCommunity community;
    CharacterController cc;
    Transform star;
    Vector3 vel; float vy;
    float starReactCooldown, familiarity, nextChatOk;
    public bool Busy { get; set; }      // in a conversation or reacting
    public bool Chatting { get; private set; }
    public Job CurrentJob { get; private set; }   // village job board claim (haul / cart / farm chore / chop)
    public SettlerCommunity.Group group;          // the family, couple or circle of friends this settler belongs to
    public SettlerCommunity.Group friends;
    public SettlerBrain guardian;                  // a kid in a family: the grown-up they tag along after
    public HandCart riding;                        // cart currently hitched to this settler, if any
    Coroutine life;
    GameObject tool;
    System.Random rng;
    float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

    void Start()
    {
        cc = GetComponent<CharacterController>();
        rng = new System.Random(GetInstanceID());
        var p = GameObject.FindGameObjectWithTag("Player"); star = p ? p.transform : null;
        if (star) { var sc = star.GetComponent<CharacterController>(); if (sc) Physics.IgnoreCollision(cc, sc, true); }
        rig.energy = role == Role.Kid ? .95f : role == Role.Elder ? .25f : R(.4f, .75f);
        if (role == Role.Elder) walkSpeed *= .7f;
        if (role == Role.Kid) walkSpeed *= 1.25f;
        nextChatOk = Time.time + R(5, 20);
        memoryKey="VillageLearning."+settlerName+"."+role+"."+home.ToString();
        for(int i=0;i<learnedValue.Length;i++) learnedValue[i]=PlayerPrefs.GetFloat(memoryKey+i,0);
        life = StartCoroutine(Life());
    }

    // ================================================================== movement
    readonly RaycastHit[] obstacleHits = new RaycastHit[24];
    Vector3 moveTarget; bool moving; float moveSpeed; float stuckTime; Vector3 lastPos;

    bool arrived, jobFailed;
    readonly Dictionary<Job, float> retryAfter = new Dictionary<Job, float>();
    readonly float[] learnedValue = new float[9];
    [Range(0,1)] public float stamina = .9f, hunger = .15f, loneliness = .2f;
    public int completedJobs, failedJobs;
    string memoryKey;
    Vector3 farmWorkAccess;
    IEnumerator WalkTo(Vector3 target, float speed, float arrive = .6f, float timeout = 70f)
    {
        yield return Navigate(target, speed, arrive, timeout, false);
    }
    IEnumerator Navigate(Vector3 target, float speed, float radius, float timeout, bool allowBusy)
    {
        arrived = false;
        var world = VillageWorld.I;
        if (!world || !world.NavReady) yield break;
        for (int attempt=0;attempt<2;attempt++) {
            var route = world.FindPath(transform.position,target);
            if(route.Count==0) break;
            float elapsed=0;
            foreach(var waypoint in route) {
                moveTarget=VillageKit.Ground(waypoint); moveSpeed=speed*(riding?.75f:1); stuckTime=0; moving=true;
                while((allowBusy || !Busy) && Flat(moveTarget-transform.position).magnitude>radius && elapsed<timeout && stuckTime<2.5f) {
                    elapsed+=Time.deltaTime; yield return null;
                }
                if((!allowBusy && Busy) || stuckTime>=2.5f || elapsed>=timeout) break;
            }
            moving=false;
            arrived=Flat(target-transform.position).magnitude<radius+.65f;
            if(arrived || (!allowBusy && Busy)) break;
            yield return new WaitForSeconds(.4f);
        }
        moving=false;
        if(CurrentJob!=null && !arrived) jobFailed=true;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        hunger=Mathf.Clamp01(hunger+dt/400f); loneliness=Mathf.Clamp01(loneliness+dt/240f);
        stamina=Mathf.Clamp01(stamina-dt*(moving?.0018f:CurrentJob!=null?.0012f:.00015f));
        if (!cc || !cc.enabled || !rig) return;
        Vector3 desired = Vector3.zero;
        if (moving)
        {
            Vector3 to = Flat(moveTarget - transform.position);
            float d = to.magnitude;
            if (d > .05f)
            {
                Vector3 dir = to / d;
                // simple obstacle steering: probe ahead, slide around whatever is there
                int hitCount=Physics.SphereCastNonAlloc(transform.position+Vector3.up*.8f,.26f,dir,obstacleHits,1f,~0,QueryTriggerInteraction.Ignore);
                float nearest=float.MaxValue; Vector3 normal=Vector3.zero;
                for(int i=0;i<hitCount;i++) {
                    var hit=obstacleHits[i];
                    if(hit.collider.transform.IsChildOf(transform) || (riding && hit.collider.transform.IsChildOf(riding.transform)) || hit.normal.y>.6f) continue;
                    if(hit.distance<nearest) {nearest=hit.distance;normal=hit.normal;}
                }
                if(nearest<.7f) {
                    Vector3 side=Vector3.Cross(Vector3.up,dir); if(Vector3.Dot(side,normal)<0) side=-side;
                    Vector3 steer=(dir+side*.9f).normalized;
                    Vector3 probe = transform.position + steer * .45f;
                    bool canSteer = tradeCart
                        ? Mathf.Abs(TerrainProbe.GroundY(probe, transform.position.y) - transform.position.y) < .5f
                        : !VillageWorld.I || VillageWorld.I.Walkable(probe);
                    if (canSteer) dir = steer;
                }
                desired = dir * moveSpeed * Mathf.Lerp(.78f,1f,Mathf.PerlinNoise(GetInstanceID()%199,Time.time*.42f)) * Mathf.Clamp01(d / .8f + .3f);
            }
        }
        // keep a little personal space from other settlers
        if (community) desired += community.Separation(this) * 1.2f;
        vel = Vector3.Lerp(vel, desired, 1 - Mathf.Exp(-6 * dt));
        vy = cc.isGrounded ? -2f : vy - 20f * dt;
        cc.Move((vel + Vector3.up * vy) * dt);
        float spd = Flat(cc.velocity).magnitude;
        if (spd > .15f && Flat(vel).sqrMagnitude>.001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Flat(vel)), 1 - Mathf.Exp(-8 * dt));
        rig.speed = Mathf.Lerp(rig.speed, spd, 1f - Mathf.Exp(-8f * dt));
        // stuck detection
        if (moving) { stuckTime = Flat(transform.position - lastPos).magnitude < .2f * dt * moveSpeed ? stuckTime + dt : Mathf.Max(0, stuckTime - dt); }
        lastPos = transform.position;
        if (transform.position.y < -10) { cc.enabled = false; transform.position = home + Vector3.up; cc.enabled = true; }

        UpdateStarAwareness(dt);
    }

    void Face(Vector3 p) { Vector3 d = Flat(p - transform.position); if (d.sqrMagnitude > .01f) StartCoroutine(TurnTo(Quaternion.LookRotation(d))); }
    IEnumerator TurnTo(Quaternion q)
    {
        Quaternion start = transform.rotation;
        for (float t = 0; t < .5f; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(start, q, Mathf.SmoothStep(0,1,t/.5f));
            yield return null;
        }
        transform.rotation = q;
    }

    IEnumerator Do(SettlerRig.Pose pose, float seconds)
    {
        rig.pose = pose;
        float nextSound = 0;
        for (float t = 0; t < seconds && (!Busy || CurrentJob != null); t += Time.deltaTime)
        {
            if (t >= nextSound)
            {
                string cue = pose switch {
                    SettlerRig.Pose.Dig or SettlerRig.Pose.Sow => "Dig",
                    SettlerRig.Pose.Water => "Water",
                    SettlerRig.Pose.Sweep => "Sweep",
                    _ => null
                };
                if (cue != null) WorldSound.Cue(transform, cue, 24f);
                nextSound = t + .8f;
            }
            yield return null;
        }
        if (!Busy || CurrentJob != null) rig.pose = tool != null ? SettlerRig.Pose.ShoulderCarry : (riding ? SettlerRig.Pose.PullCart : SettlerRig.Pose.Idle);
    }

    // ================================================================== daily life
    Stockpile tradeOrigin, tradeDestination;
    List<Vector3> tradeRoute;
    int tradeIndex;
    HandCart tradeCart;

    internal void AssignTrade(Stockpile origin, Stockpile destination, List<Vector3> route, int index, HandCart cart)
    {
        tradeOrigin = origin;
        tradeDestination = destination;
        tradeRoute = route;
        tradeIndex = index;
        tradeCart = cart;
        cart.Hitch(this);
        riding = cart;
        rig.pose = SettlerRig.Pose.PullCart;
    }

    IEnumerator TradeCircuit()
    {
        int direction = 1;
        LoadTradeCart(tradeOrigin);
        while (tradeCart && tradeOrigin && tradeDestination)
        {
            Vector3 target = tradeRoute[tradeIndex];
            moveTarget = target;
            moveSpeed = walkSpeed * .84f;
            stuckTime = 0;
            moving = true;
            while (Flat(target - transform.position).sqrMagnitude > .85f * .85f)
            {
                if (stuckTime > 3f)
                {
                    // Give passing villagers room, then resume on the same road sample.
                    moving = false;
                    yield return new WaitForSeconds(1f);
                    stuckTime = 0;
                    moving = true;
                }
                yield return null;
            }
            if (tradeIndex != (direction > 0 ? tradeRoute.Count - 1 : 0))
            {
                tradeIndex += direction;
                continue;
            }

            moving = false;
            Stockpile depot = direction > 0 ? tradeDestination : tradeOrigin;
            if (tradeCart.HasCargo)
            {
                int qty = tradeCart.Unload(out var resource);
                int room = Mathf.Max(0, 12 - depot.Count(resource));
                if (room > 0) depot.Add(resource, Mathf.Min(qty, room));
            }
            completedJobs++;
            Say(direction > 0 ? "Supplies from the lower road." : "Back with goods from up the trail.");
            rig.pose = SettlerRig.Pose.HandOver;
            yield return new WaitForSeconds(2.8f);
            LoadTradeCart(depot);
            rig.pose = SettlerRig.Pose.PullCart;
            direction = -direction;
            tradeIndex += direction;
        }
        moving = false;
    }

    void LoadTradeCart(Stockpile depot)
    {
        if (depot.TakeMost(out var resource, out int qty, 4)) tradeCart.SetCargo(resource, qty);
    }

    IEnumerator Life()
    {
        if (tradeCart) { yield return TradeCircuit(); yield break; }
        while (!VillageWorld.I || !VillageWorld.I.NavReady) yield return null;
        // Imported house prefabs can spawn a family inside a closed fence. Place them
        // on a connected doorstep once, before simulation starts, rather than retrying forever.
        var world=VillageWorld.I;
        Vector3 hub=world.Storehouse && world.SharesNavigationArea(home, world.Storehouse.Dock) ? world.Storehouse.Dock : home;
        if(world.FindPath(transform.position,hub).Count==0) {
            for(int i=0;i<60;i++) {
                if(!world.RandomWalkable(home,3+i*.3f,rng,out var spot) || world.FindPath(spot,hub).Count==0) continue;
                cc.enabled=false; transform.position=spot+Vector3.up*.1f; cc.enabled=true; home=spot; break;
            }
        }
        yield return new WaitForSeconds(R(0, 3));
        while (true)
        {
            while (Busy) yield return null;
            IEnumerator act = hunger>.7f ? Eat() : stamina<.3f ? Rest() : loneliness>.7f ? SocialVisit() : (role != Role.Kid && role != Role.Elder && TryClaimJob(out var job)) ? RunJob(job) : PickActivity();
            yield return act;
            while (Busy) yield return null;
            HoldTool(null);
            rig.lookPoint = null; rig.lookTarget = null;
            yield return Idle(R(1, 4));
        }
    }

    IEnumerator Idle(float s)
    {
        rig.pose = SettlerRig.Pose.Idle;
        // glance around now and then
        for (float t = 0; t < s && !Busy; t += Time.deltaTime)
        {
            if (rng.NextDouble() < Time.deltaTime * .4f) rig.lookPoint = transform.position + Quaternion.Euler(0, R(-100, 100), 0) * transform.forward * 6 + Vector3.up * R(0, 2);
            yield return null;
        }
        rig.lookPoint = null;
    }

    float nextJourney;
    IEnumerator PickActivity()
    {
        if (loneliness > .45f) return SocialVisit();
        var company = friends != null && (group == null || rng.NextDouble() < .35) ? friends : group;
        if (company != null && role != Role.Kid && rng.NextDouble() < company.Togetherness && community.NearestMate(this, company, 80f) != null) return GroupTime(company);
        if (stamina < .55f) return Rest();
        if (role == Role.Fisher && community.Pond) return Fish();
        if (role == Role.Elder) return stamina < .8f ? SitAndWatch() : SweepDoorstep();
        if (role == Role.Kid) return guardian != null && rng.NextDouble() < .7 ? TagAlong() : community.NearestKid(this,20) ? PlayTag() : TagAlong();
        if (role == Role.Wanderer && Time.time > nextJourney) return Journey();
        return Stroll();
    }
    IEnumerator Journey()
    {
        nextJourney=Time.time+90;
        var w=VillageWorld.I;
        Say("Stopping at the market before my journey.");
        if(w && w.Market) {
            yield return WalkTo(w.Market.Dock,walkSpeed);
            if(arrived && !Busy) {
                foreach(Res r in new[]{Res.Carrot,Res.Tomato,Res.Cabbage,Res.Pumpkin,Res.Beans})
                    if(w.Market.Take(r,1)) {yield return Do(SettlerRig.Pose.HandOver,2);w.coins+=2;break;}
            }
        }
        for(int i=0;i<3 && !Busy;i++) {
            if(w && w.RandomWalkable(transform.position,20,rng,out var stop)) {
                yield return WalkTo(stop,walkSpeed);
                if(arrived && !Busy) yield return Do(SettlerRig.Pose.Point,1.5f);
            }
        }
        if(!Busy) yield return WalkTo(home,walkSpeed,1.2f);
    }

    // ================================================================== village job board
    bool TryClaimJob(out Job job)
    {
        job = null;
        var vw = VillageWorld.I; if (!vw) return false;
        Job best = null; float bestP = 0;
        foreach (var j in vw.jobs)
        {
            if (!j.Open || (retryAfter.TryGetValue(j,out var until) && Time.time<until)) continue;
            if (!vw.SharesNavigationArea(transform.position, j.where) ||
                (j.to && !vw.SharesNavigationArea(transform.position, j.to.Dock))) continue;
            if (j.kind == Job.Kind.Haul || j.kind == Job.Kind.ContinueHaul || j.kind == Job.Kind.Restock || j.kind == Job.Kind.Chop || j.kind == Job.Kind.FetchCart) { /* anyone */ }
            else if (role != Role.Farmer && role != Role.Gardener) continue;   // farm chores go to farmers/gardeners first
            float distance=Flat(j.where-transform.position).magnitude;
            float score=j.priority + Mathf.Min(.6f,(Time.time-j.posted)/120f)+learnedValue[(int)j.kind]*.35f-distance*.018f;
            if(score>bestP) { bestP=score; best=j; }
        }
        if (best == null) return false;
        Vector3 access = best.cart ? best.cart.HitchPoint : best.where;
        bool reachable = best.plot ? best.plot.TryWorkAccess(vw, transform.position, out farmWorkAccess)
            : vw.FindPath(transform.position,access).Count != 0;
        if (!reachable) { retryAfter[best]=Time.time+30; return false; }
        if(best.cart && (best.cart.reserved || !best.cart.Parked)) return false;
        if(best.cart) best.cart.reserved=true;
        best.claimedBy = this; CurrentJob = best; job = best; return true;
    }

    IEnumerator RunJob(Job job)
    {
        Busy = true; jobFailed=false;
        IEnumerator routine;
        switch (job.kind)
        {
            case Job.Kind.Till: routine = JobTill(job); break;
            case Job.Kind.Sow: routine = JobSow(job); break;
            case Job.Kind.Water: routine = JobWater(job); break;
            case Job.Kind.Harvest: routine = JobHarvest(job); break;
            case Job.Kind.Haul: routine = JobHaul(job); break;
            case Job.Kind.ContinueHaul: routine = JobHaul(job); break;
            case Job.Kind.FetchCart: routine = JobFetchCart(job); break;
            case Job.Kind.Restock: routine = JobRestock(job); break;
            case Job.Kind.Chop: routine = JobChop(job); break;
            default: routine = null; break;
        }
        if (routine != null) yield return routine;
        if(jobFailed) { failedJobs++; retryAfter[job]=Time.time+30; VillageWorld.I?.Abandon(job); }
        else { completedJobs++; VillageWorld.I?.Finish(job); }
        int action=(int)job.kind;
        learnedValue[action]=Mathf.Lerp(learnedValue[action],jobFailed?-1:1,.18f);
        PlayerPrefs.SetFloat(memoryKey+action,learnedValue[action]);
        if(job.pickup) job.pickup.claimed=false;
        if(job.cart) job.cart.reserved=false;
        if(riding) { riding.Park(); riding=null; }
        CurrentJob = null; Busy = false; HoldTool(null);
    }

    IEnumerator JobTill(Job job)
    {
        if (!job.plot) yield break;
        Face(job.plot.transform.position); HoldTool("hoe");   // carry hoe on shoulder while approaching
        yield return WalkToBusy(farmWorkAccess);
        if (!arrived) { jobFailed=true; yield break; }
        Face(job.plot.transform.position);
        yield return Do(SettlerRig.Pose.Dig, R(3, 5));
        HoldTool(null);
        if (job.plot) job.plot.Till();
    }
    IEnumerator JobSow(Job job)
    {
        if (!job.plot) yield break;
        Face(job.plot.transform.position); HoldTool("hoe");
        yield return WalkToBusy(farmWorkAccess);
        if (!arrived) { jobFailed=true; yield break; }
        Face(job.plot.transform.position);
        yield return Do(SettlerRig.Pose.Sow, R(2, 3.5f));
        HoldTool(null);
        if (job.plot) job.plot.Sow();
    }
    IEnumerator JobWater(Job job)
    {
        if (!job.plot) yield break;
        var vw = VillageWorld.I;
        if (vw && vw.Well && vw.SharesNavigationArea(transform.position, vw.Well.transform.position))
        {
            var w = vw.Well.transform.position;
            yield return WalkToBusy(w + Flat(transform.position - w).normalized * 1.7f);
        if (!arrived) { jobFailed=true; yield break; }
            Face(w); HoldTool("bucket"); vw.Well.DrawWater();
            yield return Do(SettlerRig.Pose.Crouch, 2.2f);
            while (vw.Well.Drawing) yield return null;
        }
        Face(job.plot.transform.position); HoldTool("can");   // carry watering can while approaching
        yield return WalkToBusy(farmWorkAccess);
        if (!arrived) { jobFailed=true; yield break; }
        Face(job.plot.transform.position);
        yield return Do(SettlerRig.Pose.Water, R(3, 5));
        HoldTool(null);
        if (job.plot) job.plot.Water(.6f);
    }
    IEnumerator JobHarvest(Job job)
    {
        if (!job.plot) yield break;
        Face(job.plot.transform.position); HoldTool("hoe");
        yield return WalkToBusy(farmWorkAccess);
        if (!arrived) { jobFailed=true; yield break; }
        Face(job.plot.transform.position);
        yield return Do(SettlerRig.Pose.PickUp, R(2, 4));
        HoldTool(null);
        if (!job.plot) yield break;
        int q = job.plot.Harvest();
        if (q > 0 && community)
        {
            var vw = VillageWorld.I;
            if (vw) vw.SpawnPickup(job.plot.YieldRes, q, job.plot.transform.position + Flat(transform.position - job.plot.transform.position).normalized * (job.plot.size.x * .5f + .6f));
        }
        if (rng.NextDouble() < .35) { Say(Pick("good crop!", "that'll do", "nice harvest")); yield return Do(SettlerRig.Pose.Cheer, 1.2f); }
    }

    /// <summary>Carry (small amounts on foot) or push a cart (larger loads) from a pickup/stockpile to its
    /// destination. If interrupted or the cart is left parked mid-route, another settler's FetchCart/ContinueHaul
    /// job will pick it up and carry on - the cargo stays on the cart.</summary>
    IEnumerator JobHaul(Job job)
    {
        var vw=VillageWorld.I; if(!vw || !job.to) { jobFailed=true; yield break; }
        var cart=job.cart;
        if(!cart && job.pickup && job.qty>3) { cart=vw.NearestFreeCart(transform.position,40); if(cart) {cart.reserved=true; job.cart=cart;} }
        if(cart) {
            yield return WalkToBusy(cart.HitchPoint);
            if(!arrived || !cart || !cart.Parked) {jobFailed=true; yield break;}
            cart.Hitch(this); riding=cart; rig.pose=SettlerRig.Pose.PullCart;
        }
        int carried=0; Res resource=job.res;
        if(job.pickup) {
            job.pickup.claimed=true;
            yield return WalkToBusy(job.pickup.transform.position);
            if(!arrived || !job.pickup) {jobFailed=true; yield break;}
            yield return Do(SettlerRig.Pose.PickUp,1.4f);
            carried=job.pickup.qty; resource=job.pickup.res;
            if(cart) { cart.SetCargo(resource,carried); carried=0; }
            Destroy(job.pickup.gameObject);
        } else if(!cart || !cart.HasCargo) {jobFailed=true; yield break;}
        if(!cart) {HoldCargo(resource,carried); rig.pose=SettlerRig.Pose.Carry;}
        else rig.pose=SettlerRig.Pose.PullCart;
        var route=vw.FindPath(transform.position,job.to.Dock);
        if(route.Count==0) jobFailed=true;
        foreach(var wp in route) {
            yield return WalkToBusy(wp);
            if(!arrived) {jobFailed=true; break;}
            if(cart && stamina<.4f && Flat(job.to.Dock-transform.position).magnitude>6) {
                cart.Park(); riding=null; Say("Leaving this load for the next shift.");
                yield break;
            }
        }
        if(jobFailed) { if(carried>0) vw.SpawnPickup(resource,carried,transform.position); yield break; }
        Face(job.to.transform.position); yield return Do(SettlerRig.Pose.HandOver,1.3f);
        if(cart) {int amount=cart.Unload(out var kind);job.to.Add(kind,amount);cart.Park();riding=null;}
        else job.to.Add(resource,carried);
        rig.pose=SettlerRig.Pose.Idle;
    }

    IEnumerator JobFetchCart(Job job)
    {
        if (!job.cart) yield break;
        yield return WalkToBusy(job.cart.HitchPoint);
        if (!arrived) { jobFailed=true; yield break; }
        if (!job.cart) yield break;
        job.cart.Hitch(this); riding = job.cart;
        var vw = VillageWorld.I;
        Vector3 dest = vw ? vw.CartShed : job.cart.transform.position;
        rig.pose=SettlerRig.Pose.PullCart;
        yield return WalkToBusy(dest);
        if (!arrived) { jobFailed=true; yield break; }
        if (riding) { riding.Park(); riding = null; }
    }

    IEnumerator JobChop(Job job)
    {
        var vw = VillageWorld.I; if (!vw || !vw.ChopBlock) yield break;
        yield return WalkToBusy(vw.ChopBlock.position + Flat(transform.position - vw.ChopBlock.position).normalized * 1.1f);
        if (!arrived) { jobFailed=true; yield break; }
        Face(vw.ChopBlock.position); HoldTool("hoe");
        int rounds = rng.Next(3, 6);
        for (int i = 0; i < rounds; i++) yield return Do(SettlerRig.Pose.Dig, R(1.5f, 2.5f));
        HoldTool(null);
        if (job.to) vw.SpawnPickup(Res.Wood, rng.Next(2, 4), vw.ChopBlock.position + Flat(transform.position - vw.ChopBlock.position).normalized * .6f);
    }

    IEnumerator JobRestock(Job job)
    {
        if(!job.from || !job.to) {jobFailed=true; yield break;}
        yield return WalkToBusy(job.from.Dock);
        if(!arrived) {jobFailed=true; yield break;}
        yield return Do(SettlerRig.Pose.PickUp,1.2f);
        if(!job.from.TakeMost(out var res,out var qty,5)) {jobFailed=true;yield break;}
        HoldCargo(res,qty); rig.pose=SettlerRig.Pose.Carry;
        yield return WalkToBusy(job.to.Dock);
        if(!arrived) {VillageWorld.I.SpawnPickup(res,qty,transform.position);jobFailed=true;yield break;}
        Face(job.to.transform.position); yield return Do(SettlerRig.Pose.HandOver,1.2f); job.to.Add(res,qty);
    }

    IEnumerator Farm(Transform plot)
    {
        Vector3 spot = plot.position + plot.right * R(-1.5f, 1.5f) + plot.forward * R(-1.2f, 1.2f);
        Vector3 edge = spot + (Flat(spot - plot.position).sqrMagnitude < .01f ? plot.right : Flat(spot - plot.position).normalized) * 1.2f;
        yield return WalkTo(edge, walkSpeed);
        Face(spot);
        int rounds = rng.Next(2, 5);
        for (int i = 0; i < rounds && !Busy; i++)
        {
            double k = rng.NextDouble();
            if (k < .45) { HoldTool("hoe"); yield return Do(SettlerRig.Pose.Dig, R(4, 8)); }
            else if (k < .75) { HoldTool("can"); yield return Do(SettlerRig.Pose.Water, R(3, 6)); }
            else { HoldTool(null); yield return Do(SettlerRig.Pose.Crouch, R(3, 5)); }
            HoldTool(null);
            if (rng.NextDouble() < .3) { yield return Do(SettlerRig.Pose.Scratch, 1.2f); }   // wipe the brow
            if (Busy) yield break;
            // shuffle along the row
            yield return WalkTo(edge + plot.right * R(-1.5f, 1.5f), walkSpeed * .7f, .3f, 6);
            Face(spot);
        }
    }

    IEnumerator FetchWater()
    {
        var w = community.Well.position;
        yield return WalkTo(w + Flat(transform.position - w).normalized * 1.7f, walkSpeed);
        Face(w); yield return Do(SettlerRig.Pose.Crouch, 2.5f);
        HoldTool("bucket");
        rig.pose = SettlerRig.Pose.Carry;
        if (community.Plots.Count > 0)
        {
            var plot = community.Plots[rng.Next(community.Plots.Count)];
            yield return WalkTo(plot.position + plot.right * 2.2f, walkSpeed * .85f);
            Face(plot.position); HoldTool("can"); yield return Do(SettlerRig.Pose.Water, R(4, 7));
        }
    }

    IEnumerator Fish()
    {
        var pond = community.Pond;
        Vector3 bank = pond.position + Quaternion.Euler(0, R(0, 360), 0) * Vector3.forward * (community.PondRadius + 1.1f);
        yield return WalkTo(bank, walkSpeed);
        Face(pond.position); HoldTool("rod");
        yield return Do(rng.NextDouble() < .5 ? SettlerRig.Pose.Sit : SettlerRig.Pose.Fish, R(18, 35));
        if (!Busy && arrived && VillageWorld.I) VillageWorld.I.SpawnPickup(Res.Fish,rng.Next(1,4),transform.position+transform.right);
        if (!Busy && rng.NextDouble() < .4) { HoldTool(null); Say(Pick("got one!", "a little one...", "nothing today", "ha!")); yield return Do(SettlerRig.Pose.Cheer, 1.5f); }
    }

    IEnumerator Stroll(float range = 1f)
    {
        Vector3 p = community.RandomPathPoint(transform.position, 18 * range, rng);
        yield return WalkTo(p, walkSpeed);
        rig.lookPoint = p + Quaternion.Euler(0, R(0, 360), 0) * Vector3.forward * 10 + Vector3.up * 2;
        yield return Idle(R(2, 5));
    }

    IEnumerator VisitNeighbour()
    {
        var door = community.RandomHome(rng);
        yield return WalkTo(door, walkSpeed, 1.2f);
        yield return Idle(R(2, 5));
        if (!Busy && rng.NextDouble() < .5) { yield return Do(SettlerRig.Pose.Wave, 1.5f); }
    }

    IEnumerator LeanAtWell()
    {
        var w = community.Well.position;
        yield return WalkTo(w + Quaternion.Euler(0, R(0, 360), 0) * Vector3.forward * 1.8f, walkSpeed);
        Face(w); yield return Idle(R(5, 10));
    }

    IEnumerator SweepDoorstep()
    {
        yield return WalkTo(home + Quaternion.Euler(0, R(0, 360), 0) * Vector3.forward * 1.2f, walkSpeed);
        HoldTool("broom");
        yield return Do(SettlerRig.Pose.Sweep, R(6, 12));
    }

    IEnumerator SitAndWatch()
    {
        yield return WalkTo(home + Quaternion.Euler(0, R(0, 360), 0) * Vector3.forward * 1.6f, walkSpeed);
        rig.lookPoint = transform.position + transform.forward * 12 + Vector3.up;
        yield return Do(SettlerRig.Pose.Sit, R(12, 25));
    }

    IEnumerator PlayTag()
    {
        var other=community.NearestKid(this,25);
        if(!other) {yield return Skip();yield break;}
        Say("You're it!");
        for(int i=0;i<4 && !Busy;i++) {
            Vector3 target=i%2==0?other.transform.position:transform.position+Flat(transform.position-other.transform.position).normalized*4;
            yield return WalkTo(target,runSpeed,.9f,5);
        }
        if(!Busy) yield return Do(SettlerRig.Pose.Cheer,1.2f);
    }

    /// <summary>Time with the people you belong with (a family, a couple or a circle of friends): walk up beside the
    /// nearest of them and, if you are both free, share a few words. Otherwise you stand or amble together.</summary>
    IEnumerator GroupTime(SettlerCommunity.Group company)
    {
        var mate = community.NearestMate(this, company, 80f);
        if (mate == null) yield break;
        Vector3 side = mate.transform.right * (rng.NextDouble() < .5 ? 1.4f : -1.4f);
        yield return WalkTo(mate.transform.position + side, walkSpeed, .8f, 14f);
        if (!arrived || Busy) yield break;
        if (!CanChat || !mate.CanChat)
        {
            yield return Idle(R(2, 4));
            if (rng.NextDouble() < .5) yield return Stroll(.6f);
            yield break;
        }
        var lines = community.GroupLines(company, rng);
        mate.StartCoroutine(mate.Chat(this, lines, false));
        yield return Chat(mate, lines, true);
    }

    IEnumerator Skip()
    {
        Vector3 p = community.RandomPathPoint(transform.position, 12, rng);
        rig.energy = 1;
        yield return WalkTo(p, runSpeed * .8f);
        yield return Do(SettlerRig.Pose.Cheer, R(.8f, 1.6f));
    }

    IEnumerator TagAlong()
    {
        var adult = guardian != null ? guardian : community.NearestAdult(this,30);
        if(!adult) {yield return Skip();yield break;}
        for(int i=0;i<4 && !Busy;i++) {
            if(!adult) break;
            rig.lookTarget=adult.transform;
            yield return WalkTo(adult.transform.position+adult.transform.right*1.5f,walkSpeed*1.2f,.8f,5);
        }
        rig.lookTarget=null;
    }

    // ================================================================== chatting
    public bool CanChat => !tradeCart && !Busy && Time.time > nextChatOk && (role != Role.Fisher || rig.pose == SettlerRig.Pose.Idle);

    public IEnumerator Chat(SettlerBrain other, string[] lines, bool first)
    {
        Busy = true; Chatting = true;
        moving = false; HoldTool(null);
        Vector3 meet = (transform.position + other.transform.position) * .5f;
        Vector3 spot = meet + Flat(transform.position - meet).normalized * .9f;
        yield return WalkToBusy(spot);
        if(!arrived || !other) { Chatting=false; Busy=false; nextChatOk=Time.time+15; yield break; }
        Face(other.transform.position);
        rig.lookTarget = other.transform;
        for (int i = 0; i < lines.Length; i++)
        {
            bool mine = (i % 2 == 0) == first;
            if (mine) { rig.pose = SettlerRig.Pose.Talk; Say(lines[i], 2.6f); }
            else rig.pose = SettlerRig.Pose.Idle;
            yield return new WaitForSeconds(2.8f);
        }
        rig.pose = rng.NextDouble() < .4 ? SettlerRig.Pose.Wave : SettlerRig.Pose.Idle;
        yield return new WaitForSeconds(1f);
        rig.pose = SettlerRig.Pose.Idle; rig.lookTarget = null;
        nextChatOk = Time.time + R(25, 60);
        loneliness=Mathf.Max(0,loneliness-.65f);
        Chatting = false; Busy = false;
    }

    IEnumerator WalkToBusy(Vector3 p) { yield return Navigate(p,walkSpeed,.45f,80f,true); }

    IEnumerator Rest() {
        yield return WalkTo(home,walkSpeed,1.2f);
        if(Busy) yield break;
        yield return Do(SettlerRig.Pose.Sleep,12);
        if(!Busy) stamina=Mathf.Clamp01(stamina+.65f);
    }
    IEnumerator Eat() {
        var w=VillageWorld.I; var pile=w ? (w.Market && w.Market.Total>0 ? w.Market:w.Storehouse):null;
        if (w && pile && !w.SharesNavigationArea(home, pile.Dock))
        {
            // Outlying households eat at home rather than routing to the hamlet market.
            yield return WalkTo(home,walkSpeed,1.2f);
            if (!Busy)
            {
                yield return Do(SettlerRig.Pose.Eat,5);
                hunger=Mathf.Max(0,hunger-.65f);
                stamina=Mathf.Clamp01(stamina+.12f);
            }
            yield break;
        }
        if(!pile) {yield return Rest();yield break;}
        yield return WalkTo(pile.Dock,walkSpeed);
        if(!arrived || Busy) {yield return Rest();yield break;}
        foreach(Res food in new[]{Res.Bread,Res.Cabbage,Res.Beans,Res.Pumpkin,Res.Turnip,Res.Fish,Res.Carrot,Res.Tomato}) {
            if(!pile.Take(food,1)) continue;
            yield return Do(SettlerRig.Pose.Eat,5); hunger=Mathf.Max(0,hunger-.65f); stamina=Mathf.Clamp01(stamina+.12f); yield break;
        }
        yield return Rest();
    }
    IEnumerator SocialVisit() {
        SettlerBrain friend=null; float best=25;
        foreach(var other in community.settlers) { if(!other || other==this || !other.CanChat) continue;
            float d=Flat(other.transform.position-transform.position).magnitude; if(d<best) {best=d;friend=other;} }
        if(!friend) {yield return Stroll();loneliness=Mathf.Max(0,loneliness-.15f);yield break;}
        yield return WalkTo(friend.transform.position+friend.transform.right*1.8f,walkSpeed);
        if(!arrived || !CanChat || !friend.CanChat) yield break;
        var lines=new[]{"How is your day going?","Let's rest here a moment.","I'll check the village supplies next."};
        friend.StartCoroutine(friend.Chat(this,lines,false)); yield return Chat(friend,lines,true);
    }
    void OnDisable() {
        moving=false;
        if(CurrentJob!=null) { if(CurrentJob.pickup) CurrentJob.pickup.claimed=false; if(CurrentJob.cart) CurrentJob.cart.reserved=false; VillageWorld.I?.Abandon(CurrentJob); CurrentJob=null; }
        if(riding) {riding.Park();riding=null;}
    }

    // ================================================================== the star
    void UpdateStarAwareness(float dt)
    {
        if (tradeCart) return;
        if (!star || WishboundIntro.IsPlaying) return;
        if (HouseEntrances.Instance && HouseEntrances.Instance.Inside) return;
        float d = Flat(star.position - transform.position).magnitude;
        starReactCooldown -= dt;
        // anyone nearby keeps an eye on it
        if (d < 12f && !Chatting && rig.lookTarget == null) rig.lookPoint = star.position + Vector3.up * .3f;
        else if (d >= 12f && rig.lookPoint.HasValue && Vector3.Distance(rig.lookPoint.Value, star.position + Vector3.up * .3f) < 1f) rig.lookPoint = null;
        if (d < 7f && starReactCooldown <= 0 && !Busy) StartCoroutine(ReactToStar());
    }

    IEnumerator ReactToStar()
    {
        Busy = true; starReactCooldown = Mathf.Lerp(18, 60, Mathf.Clamp01(familiarity / 4f));
        moving = false; HoldTool(null);
        Face(star.position);
        rig.lookTarget = star;
        // continuous alignment: +100 = full Tether (blinding, unsettling), -100 = full Roots (warm, trusted)
        float spec = StoryFlags.Spectrum;
        float tetherAmt = Mathf.Clamp01(Mathf.InverseLerp(10f, 100f, spec));    // 0 until clearly celestial
        float rootsAmt  = Mathf.Clamp01(Mathf.InverseLerp(-10f, -100f, spec));  // 0 until clearly earthly
        bool tether = tetherAmt > .25f;
        var mood = temperament;
        if (mood == Temperament.Curious && rng.NextDouble() < tetherAmt) mood = Temperament.Puzzled;   // a blinding star unsettles people
        if (mood == Temperament.Puzzled && rng.NextDouble() < tetherAmt * .6f) mood = Temperament.Timid;
        float fam = familiarity + rootsAmt * 2f;                                  // earthly stars win trust faster
        familiarity += 1 + rootsAmt;

        if (role == Role.Kid && !tether)
        {
            Say(Pick("a star!!", "whoa!", "it's glowing!", "come play!"));
            for (float t = 0; t < 3.5f; t += Time.deltaTime)
            {
                Vector3 orbit = star.position + Quaternion.Euler(0, t * 120, 0) * Vector3.forward * 2.4f;
                moveTarget = VillageKit.Ground(orbit); moving = true; moveSpeed = runSpeed; rig.pose = SettlerRig.Pose.Idle;
                yield return null;
            }
            moving = false; yield return Do2(SettlerRig.Pose.Cheer, 1.6f);
        }
        else if (fam >= 3)
        {
            // they're used to it now - a friendly nod or wave
            yield return Do2(SettlerRig.Pose.Wave, 1.6f);
            if (rng.NextDouble() < .4) Say(Pick("evening, little light", "hello again", "you're back"));
            yield return new WaitForSeconds(.8f);
        }
        else switch (mood)
        {
            case Temperament.Curious:
                Say(Pick("hello there...?", "what are you?", "oh! hello"));
                yield return WalkToBusy(star.position + Flat(transform.position - star.position).normalized * 2.6f);
                Face(star.position);
                yield return Do2(SettlerRig.Pose.Wave, 1.8f);
                yield return Do2(SettlerRig.Pose.Point, 1.2f);
                break;
            case Temperament.Puzzled:
                Say(Pick("?", "huh?", "...a star?", "hmm?"), 2.2f);
                yield return Do2(SettlerRig.Pose.Scratch, 2.4f);
                rig.pose = SettlerRig.Pose.Idle; yield return new WaitForSeconds(1f);
                break;
            default:
                Say(Pick("eek!", "stay back!", "oh my!", "!!"), 1.8f);
                rig.pose = SettlerRig.Pose.Cower;
                Vector3 away = transform.position + Flat(transform.position - star.position).normalized * 6f;
                moveTarget = VillageKit.Ground(away); moving = true; moveSpeed = runSpeed;
                for (float t = 0; t < 2.2f; t += Time.deltaTime) { yield return null; }
                moving = false; Face(star.position);
                yield return Do2(SettlerRig.Pose.Cower, 1.6f);
                break;
        }
        rig.pose = SettlerRig.Pose.Idle; rig.lookTarget = null;
        Busy = false;
    }

    IEnumerator Do2(SettlerRig.Pose pose, float s) { rig.pose = pose; yield return new WaitForSeconds(s); rig.pose = SettlerRig.Pose.Idle; }

    // ================================================================== helpers
    public void Say(string line, float seconds = 2.4f) { WhisperLine.Show(transform, line, seconds, height + .35f).hideBeyond = 22f; }
    string Pick(params string[] a) => a[rng.Next(a.Length)];

    void HoldTool(string kind)
    {
        if (tool) { tool.SetActive(false); Destroy(tool); tool = null; }
        if (kind == null) { rig.pose = riding ? SettlerRig.Pose.PullCart : SettlerRig.Pose.Idle; return; }
        Transform hand = null; foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == "hand.R") hand = t;
        if (!hand) return;
        tool = SettlerTools.Make(kind, hand);
        // idle carry: a hoe rests on the shoulder (Images 1 & 2), anything else is held at the chest
        rig.pose = kind == "hoe" ? SettlerRig.Pose.ShoulderCarry : SettlerRig.Pose.Carry;
    }

    void HoldCargo(Res resource, int quantity)
    {
        HoldTool("crate"); if(!tool) return;
        for(int i=0;i<Mathf.Min(quantity,4);i++)
            VillageKit.Piece(tool.transform,ResInfo.Name(resource),VillageKit.Blob(i+41,.12f),VillageKit.Mat(ResInfo.Colour(resource)),
                new Vector3(-.28f+(i%2)*.22f,.04f,.0f+(i/2)*.14f),Vector3.zero,Vector3.one*.09f);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
