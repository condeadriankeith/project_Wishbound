using System.Collections.Generic;
using UnityEngine;

/// <summary>Curious companion. Following always takes priority over optional play.</summary>
[RequireComponent(typeof(CharacterController))]
public class ProceduralFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    [Header("Spacing (m)")]
    public float followDistance = 2.4f, idleDistance = 1.8f, minSeparation = 1.2f;
    public float arriveRadius = .3f, resumeRadius = .9f;
    [Header("Speed (m/s)")]
    public float minChaseSpeed = 3.5f, maxSpeed = 14f, catchUpGain = 1.5f;
    [Tooltip("Constant walking pace used while simply following (Follow/CatchUp). The child never speeds up to compensate for distance; it can be left behind if the target moves faster than this.")]
    public float constantWalkSpeed = 3.2f;
    public float arriveGain = 4f, smoothTime = .22f;
    [Header("Life")]
    public float swayAmplitude = .45f, swayFrequency = 1.6f, slotTurnRate = 3f;
    public float turnSpeedDegPerSec = 540f, teleportDistance = 45f;
    [Header("Curiosity")]
    public bool playful = true;
    [Range(0,1)] public float curiosity = .75f;
    public Vector2 decisionInterval = new Vector2(7,14);
    public float curiosityLeash = 7f, catchUpDistance = 8f, treeSearchRadius = 5.5f;
    public Vector2 attentionSpan = new Vector2(2.5f,5.5f);
    [Tooltip("0 gives each child an independent personality. Set a seed for repeatable playtests.")]
    public int personalitySeed;
    [Header("Physics")]
    public float gravity = -22f, maxFallSpeed = 60f, hopHeight = 1f, hopCooldown = .6f, respawnBelowY = -3f;
    public LayerMask obstacleMask = ~0;
    public float maxSafeDrop = 1.4f;

    public enum Behaviour { Follow, CatchUp, Watch, Explore, Hide, Peek }
    public Behaviour CurrentBehaviour { get; private set; }
    public Vector3 CurrentGoal { get; private set; }
    public int PlayEpisodes { get; private set; }
    public int RecoveryWarps { get; private set; }

    CharacterController cc;
    System.Random rng;
    Vector3 hVel, hAcc, tVel, lastTargetPos, slotDir, interestGoal, lastPosition;
    float vy, tSpeed, lastHop, nextDecision, stateUntil, nextTreeScan, reactionUntil;
    float personality, pace, lookPhase, stuckTime, avoidUntil, avoidSign;
    bool chasing, placed, targetWasMoving;
    int solidMask;
    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly Collider[] overlaps = new Collider[32];
    readonly List<TreeSpot> trees = new List<TreeSpot>();
    TreeSpot chosenTree;
    int hideStage;
    float peekSide;
    struct TreeSpot { public Vector3 center; public float radius; }
    static Vector3 Flat(Vector3 v) { v.y=0; return v; }
    float RandomRange(float a,float b) => Mathf.Lerp(a,b,(float)rng.NextDouble());

    // Re-entering Follow after being disabled (e.g. during a cutscene) must not produce a burst of speed:
    // reset the stale target-velocity tracking so the next frame doesn't see a huge apparent target jump.
    void OnEnable()
    {
        hVel=hAcc=tVel=Vector3.zero; tSpeed=0; chasing=false; stuckTime=0;
        if(target){lastTargetPos=target.position;}
        ScheduleDecision();
    }
    void Awake()
    {
        cc=GetComponent<CharacterController>(); cc.minMoveDistance=0;
        rng=new System.Random(personalitySeed==0?GetInstanceID() ^ System.Environment.TickCount:personalitySeed);
        personality=RandomRange(0,100); pace=RandomRange(.92f,1.08f); lookPhase=RandomRange(0,6.28f);
        solidMask=obstacleMask.value;
        int treeLayer=LayerMask.NameToLayer("TreeOcclusion");
        if(treeLayer>=0) solidMask &= ~(1<<treeLayer); // canopy proxies are not physical trunks
        if(!target) { var p=GameObject.FindGameObjectWithTag("Player"); if(p)target=p.transform; }
        if(target)
        {
            var other=target.GetComponent<CharacterController>(); if(other)Physics.IgnoreCollision(cc,other,true);
            lastTargetPos=target.position; slotDir=Flat(transform.position-target.position).normalized;
            if(slotDir.sqrMagnitude<.01f)slotDir=-Flat(target.forward).normalized;
            if(slotDir.sqrMagnitude<.01f)slotDir=Vector3.back;
        }
        lastPosition=transform.position; ScheduleDecision();
    }
    void ScheduleDecision() => nextDecision=Time.time+RandomRange(Mathf.Max(2,decisionInterval.x),Mathf.Max(decisionInterval.x,decisionInterval.y));
    bool IsSelf(Collider c) => c==cc || c.transform.IsChildOf(transform) || (target && (c.transform==target || c.transform.IsChildOf(target)));

    bool Ground(Vector3 near,out Vector3 point)
    {
        point=near;
        if (BridgeWalkSurface.TrySupport(near, out _, out var deck))
        {
            point=deck+Vector3.up*.04f;
            return true;
        }
        int n=Physics.RaycastNonAlloc(near+Vector3.up*2.5f,Vector3.down,hits,6f,solidMask,QueryTriggerInteraction.Ignore);
        float best=float.MaxValue;
        for(int i=0;i<n;i++)
        {
            var hit=hits[i];
            if(IsSelf(hit.collider) || hit.normal.y<Mathf.Cos(Mathf.Min(cc.slopeLimit,48)*Mathf.Deg2Rad) || hit.distance>=best)continue;
            best=hit.distance;point=hit.point+Vector3.up*.04f;
        }
        return best<float.MaxValue;
    }
    bool SafeGoal(Vector3 candidate,out Vector3 ground)
    {
        if(!Ground(candidate,out ground))return false;
        if(Mathf.Abs(ground.y-candidate.y)>maxSafeDrop)return false;
        var lower=ground+Vector3.up*(cc.radius+.1f);
        var upper=ground+Vector3.up*Mathf.Max(cc.radius+.1f,cc.height-cc.radius);
        int n=Physics.OverlapCapsuleNonAlloc(lower,upper,cc.radius*.85f,overlaps,solidMask,QueryTriggerInteraction.Ignore);
        for(int i=0;i<n;i++)if(!IsSelf(overlaps[i]))return false;
        return true;
    }
    void ScanTrees()
    {
        if(Time.time<nextTreeScan)return;
        nextTreeScan=Time.time+2.5f;trees.Clear();
        var source=Object.FindFirstObjectByType<IslandStylizedTrees>();
        if(!source || !source.TreeCollidersGO)return;
        // All generated tree proxies share one transform: their centers, not transform.position, locate each tree.
        foreach(var capsule in source.TreeCollidersGO.GetComponents<CapsuleCollider>())
        {
            Vector3 center=capsule.transform.TransformPoint(capsule.center);
            if(Flat(center-transform.position).sqrMagnitude>144)continue;
            center.y=transform.position.y;
            float radius=Mathf.Clamp(capsule.radius*.12f,.3f,.85f);
            trees.Add(new TreeSpot{center=center,radius=radius});
        }
    }
    bool BeginHide()
    {
        float best=float.MaxValue;bool found=false;TreeSpot bestTree=default;Vector3 bestGoal=default;
        foreach(var tree in trees)
        {
            float d=Flat(tree.center-transform.position).magnitude;
            if(d>treeSearchRadius || d<.7f || Flat(tree.center-target.position).magnitude>curiosityLeash-1)continue;
            Vector3 away=Flat(tree.center-target.position).normalized;
            if(away.sqrMagnitude<.1f)continue;
            Vector3 side=Vector3.Cross(Vector3.up,away);
            float sign=Vector3.Dot(transform.position-tree.center,side)<0?-1:1;
            Vector3 flank=tree.center+side*sign*(tree.radius+cc.radius+.65f);
            if(!SafeGoal(flank,out var safe))continue;
            if(!SafeGoal(tree.center+away*(tree.radius+cc.radius+.3f),out _))continue;
            float score=d+RandomRange(0,1.5f);
            if(score>=best)continue;
            best=score;found=true;bestTree=tree;bestGoal=safe;peekSide=sign;
        }
        if(!found)return false;
        chosenTree=bestTree;interestGoal=bestGoal;hideStage=0;
        Enter(Behaviour.Hide,RandomRange(6,9));PlayEpisodes++;return true;
    }
    void Enter(Behaviour state,float duration=0)
    {
        CurrentBehaviour=state;stateUntil=Time.time+duration;chasing=false;
    }
    void EndPlay()
    {
        Enter(Behaviour.Follow);ScheduleDecision();
    }
    void ChooseBehaviour(float separation,bool moving)
    {
        if(separation>catchUpDistance)
        {
            if(CurrentBehaviour!=Behaviour.CatchUp){Enter(Behaviour.CatchUp);ScheduleDecision();}
            return;
        }
        if(CurrentBehaviour==Behaviour.CatchUp)
        {
            if(separation<followDistance+1.5f)EndPlay();
            return;
        }
        if(CurrentBehaviour!=Behaviour.Follow)
        {
            if(!playful || separation>curiosityLeash || tSpeed>3f || Time.time>stateUntil || stuckTime>1.3f){EndPlay();return;}
            if(CurrentBehaviour==Behaviour.Hide && Flat(interestGoal-transform.position).magnitude<.45f)
            {
                Vector3 away=Flat(chosenTree.center-target.position).normalized;
                if(hideStage==0)
                {
                    var behind=chosenTree.center+away*(chosenTree.radius+cc.radius+.3f);
                    if(!SafeGoal(behind,out interestGoal)){EndPlay();return;}
                    hideStage=1;
                }
                else Enter(Behaviour.Peek,RandomRange(2,4));
            }
            return;
        }
        if(Time.time<nextDecision)return;
        ScheduleDecision();
        if(!playful || !cc.isGrounded || tSpeed>1.4f || separation>followDistance+2f || rng.NextDouble()>curiosity)return;
        float roll=RandomRange(0,1);
        if(roll<.5f && BeginHide())return;
        if(roll<.82f)
        {
            for(int i=0;i<5;i++)
            {
                float angle=RandomRange(0,Mathf.PI*2);
                // Pick a spot clearly away from where the child already is, not just from the target,
                // so an idle wander doesn't just nudge back and forth over the same couple of metres.
                var p=target.position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*RandomRange(idleDistance+1.2f,3.8f);
                if(Flat(p-transform.position).sqrMagnitude<1f)continue; // too close to current spot: not worth a trip
                if(!SafeGoal(p,out interestGoal))continue;
                Enter(Behaviour.Explore,RandomRange(attentionSpan.x,attentionSpan.y));PlayEpisodes++;return;
            }
        }
        Enter(Behaviour.Watch,RandomRange(1,2.6f));PlayEpisodes++;
    }
    Vector3 Steer(Vector3 desired,float dt)
    {
        if(desired.sqrMagnitude<.01f)return desired;
        float speed=desired.magnitude; Vector3 direction=desired/speed;
        Vector3 origin=transform.position+Vector3.up*Mathf.Max(.6f,cc.height*.55f);
        int n=Physics.SphereCastNonAlloc(origin,cc.radius*.8f,direction,hits,Mathf.Clamp(speed*.35f,.65f,2),solidMask,QueryTriggerInteraction.Ignore);
        float nearest=float.MaxValue;Vector3 normal=Vector3.zero;
        for(int i=0;i<n;i++)if(!IsSelf(hits[i].collider) && hits[i].normal.y<.65f && hits[i].distance<nearest){nearest=hits[i].distance;normal=hits[i].normal;}
        if(nearest<float.MaxValue)
        {
            Vector3 tangent=Flat(Vector3.Cross(Vector3.up,normal)).normalized;
            if(Time.time>avoidUntil){avoidSign=Vector3.Dot(tangent,direction)>=0?1:-1;avoidUntil=Time.time+1.2f;}
            direction=(tangent*avoidSign+Flat(normal)*.35f).normalized;
            speed*=.8f;
        }
        // The island's tree colliders represent entire canopies. Steer around a small virtual trunk instead.
        foreach(var tree in trees)
        {
            Vector3 away=Flat(transform.position-tree.center);
            float distance=away.magnitude,clearance=tree.radius+cc.radius+.3f;
            if(distance>clearance+1.3f || distance<.01f)continue;
            float approaching=Vector3.Dot(direction,-away/distance);
            if(approaching>.1f)
            {
                Vector3 side=Vector3.Cross(Vector3.up,away/distance);
                float sign=Vector3.Dot(side,Flat(CurrentGoal-transform.position))>=0?1:-1;
                direction=(direction+side*sign*approaching*2.5f+away/distance*Mathf.Max(0,clearance+.35f-distance)*3).normalized;
            }
        }
        if(cc.isGrounded)
        {
            Vector3 probe=transform.position+direction*Mathf.Max(.55f,speed*dt+cc.radius);
            if(!Ground(probe,out var floor) || transform.position.y-floor.y>maxSafeDrop || floor.y-transform.position.y>cc.stepOffset+.65f)
            {
                if(CurrentBehaviour!=Behaviour.Follow && CurrentBehaviour!=Behaviour.CatchUp)EndPlay();
                return Vector3.zero;
            }
        }
        return direction*speed;
    }
    float stepDistance;
    void LateUpdate()
    {
        if(!target || !cc.enabled)return;
        float dt=Mathf.Min(Time.deltaTime,.05f);if(dt<=0)return;
        if(!placed)
        {
            // Wait for the runtime island's ground before placing the companion.
            var p=target.position+slotDir*idleDistance;
            if(SafeGoal(p,out var ground)){Warp(ground);placed=true;lastTargetPos=target.position;}
            return;
        }
        Vector3 targetDelta=Flat(target.position-lastTargetPos);lastTargetPos=target.position;
        Vector3 raw=targetDelta/dt;if(raw.magnitude>40)raw=Vector3.zero;
        tVel=Vector3.Lerp(tVel,raw,1-Mathf.Exp(-8*dt));tSpeed=tVel.magnitude;
        bool moving=tSpeed>.5f;
        if(moving && !targetWasMoving)reactionUntil=Time.time+RandomRange(.18f,.55f);
        targetWasMoving=moving;
        Vector3 away=Flat(transform.position-target.position);float separation=away.magnitude;
        if(separation>teleportDistance || transform.position.y<respawnBelowY)
        {
            if(TryRecover()){RecoveryWarps++;EndPlay();}return;
        }
                // Ground sanity check: if something moved the real terrain out from under the character since it was
        // placed (e.g. the comet crater's collider finishing its rebuild while the child stands nearby), snap
        // back onto the true surface in one step instead of letting gravity fight it frame by frame, which is
        // what produced the visible upward glitch / sink-through.
        bool onBridge=BridgeWalkSurface.TrySupport(transform.position,out var bridge,out var deckSupport);
        if(onBridge)
        {
            float feetY=transform.position.y-cc.height*.5f+cc.center.y-cc.skinWidth;
            // Only recover penetration. A jump above the deck must remain a jump.
            if(feetY<deckSupport.y-.25f)Warp(new Vector3(transform.position.x,deckSupport.y+.05f+cc.height*.5f-cc.center.y,transform.position.z));
        }
        else if(TerrainProbe.TryGetGroundY(transform.position.x,transform.position.z,out float groundY))
        {
            float feetY=transform.position.y-cc.height*.5f+cc.center.y-cc.skinWidth;
            if(Mathf.Abs(feetY-groundY)>.6f)Warp(new Vector3(transform.position.x,groundY+.05f+cc.height*.5f-cc.center.y,transform.position.z));
        }
ScanTrees();ChooseBehaviour(separation,moving);
        if(moving)slotDir=Vector3.Slerp(slotDir,-tVel.normalized,1-Mathf.Exp(-slotTurnRate*dt)).normalized;
        else if(away.sqrMagnitude>.04f)slotDir=away.normalized;
        Vector3 side=Vector3.Cross(Vector3.up,slotDir);
        float weave=(Mathf.PerlinNoise(personality,Time.time*swayFrequency*.22f)-.5f)*2*swayAmplitude;
        Vector3 goal=target.position+slotDir*(moving?followDistance:idleDistance)+side*(moving?weave:0);
        if(onBridge)
        {
            if(CurrentBehaviour!=Behaviour.Follow && CurrentBehaviour!=Behaviour.CatchUp)EndPlay();
            if(bridge.Project(goal,cc.radius+.2f,out var safeDeckGoal))goal=safeDeckGoal;
        }
        else if(BridgeWalkSurface.TrySupport(target.position,out var targetBridge,out _) && targetBridge.Project(goal,cc.radius+.2f,out var entryGoal))goal=entryGoal;
        if(CurrentBehaviour==Behaviour.Explore || CurrentBehaviour==Behaviour.Hide)goal=interestGoal;
        if(CurrentBehaviour==Behaviour.Watch)goal=transform.position;
        if(CurrentBehaviour==Behaviour.Peek)
        {
            Vector3 behind=Flat(chosenTree.center-target.position).normalized;
            Vector3 peek=Vector3.Cross(Vector3.up,behind)*peekSide;
            float amount=Mathf.SmoothStep(0,1,Mathf.Sin((Time.time-stateUntil)*1.9f)*.5f+.5f);
            goal=chosenTree.center+behind*(chosenTree.radius+cc.radius+.3f)+peek*amount*.65f;
        }
        CurrentGoal=goal;
        float d=Flat(goal-transform.position).magnitude;
        bool playMove=CurrentBehaviour==Behaviour.Explore || CurrentBehaviour==Behaviour.Hide || CurrentBehaviour==Behaviour.Peek;
        if(moving || playMove)chasing=d>(playMove?.16f:.12f);
        // Wider hysteresis while the target is stationary: settle into the idle slot once close enough
        // instead of endlessly re-triggering small corrective walks (which reads as pacing back and forth).
        else if(!chasing && d>resumeRadius)chasing=true;
        else if(chasing && d<arriveRadius*1.6f)chasing=false;
        Vector3 desired=Vector3.zero;
        if(chasing && d>.001f && (Time.time>reactionUntil || separation>catchUpDistance || playMove))
        {
            // Constant walk pace: the child never speeds up to chase harder when the gap grows.
            // If the target moves faster than this, the child is simply left behind (CatchUp just keeps walking toward them).
            float cap=constantWalkSpeed*pace*(.94f+.06f*Mathf.Sin(Time.time*2.1f+personality));
            if(CurrentBehaviour==Behaviour.CatchUp)cap=Mathf.Min(cap,maxSpeed); // still never exceeds the walk pace, just keeps heading toward the (possibly distant) goal
            if(playMove)cap=CurrentBehaviour==Behaviour.Peek?.85f:2.1f*pace;
            desired=Flat(goal-transform.position)/d*Mathf.Min(cap,d*arriveGain);
            if(moving && !playMove && CurrentBehaviour!=Behaviour.Watch)desired+=tVel*.8f;
            desired=Vector3.ClampMagnitude(desired,maxSpeed);
        }
        if(separation<minSeparation && separation>.001f)desired+=away/separation*(minSeparation-separation)*5;
        desired=Steer(desired,dt);
        hVel=Vector3.SmoothDamp(hVel,desired,ref hAcc,smoothTime,Mathf.Infinity,dt);
        Vector3 facing=hVel.magnitude>.3f?hVel:Flat(target.position-transform.position);
        if(hVel.magnitude<.3f && CurrentBehaviour==Behaviour.Watch)facing=Quaternion.Euler(0,Mathf.Sin(Time.time*.8f+lookPhase)*48,0)*facing;
        if(facing.sqrMagnitude>.001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(facing),turnSpeedDegPerSec*dt);
        if(cc.isGrounded && vy<0)vy=-2;
        vy=Mathf.Max(vy+gravity*dt,-maxFallSpeed);
        Vector3 before=transform.position;cc.Move((hVel+Vector3.up*vy)*dt);
        float moved = Flat(transform.position-before).magnitude;
        if (cc.isGrounded) stepDistance += moved;
        if (stepDistance >= .73f) { WorldSound.Cue(transform, "ChildStep", 9f); stepDistance = 0; }
        float actualSpeed=moved/dt;
        if(desired.magnitude>1 && actualSpeed<.18f)stuckTime+=dt;else stuckTime=Mathf.Max(0,stuckTime-dt*2);
        if(stuckTime>1){avoidUntil=Time.time+1;hVel=Vector3.Cross(Vector3.up,Flat(goal-transform.position).normalized)*1.2f;}
        // Only hop low obstacles with clear headroom; never repeatedly jump into a trunk or a wall.
        if(stuckTime>.8f && cc.isGrounded && Time.time-lastHop>Mathf.Max(1.2f,hopCooldown) && (cc.collisionFlags&CollisionFlags.Sides)!=0)
        {
            var dir=Flat(goal-transform.position).normalized;
            bool low=Physics.Raycast(transform.position+Vector3.up*.25f,dir,.8f,solidMask,QueryTriggerInteraction.Ignore);
            bool high=Physics.SphereCast(transform.position+Vector3.up*(cc.height+.15f),cc.radius*.8f,dir,out _,.9f,solidMask,QueryTriggerInteraction.Ignore);
            if(low && !high){vy=Mathf.Sqrt(2*Mathf.Min(hopHeight,.65f)*-gravity);lastHop=Time.time;}
        }
        if(stuckTime>7 && separation>catchUpDistance && TryRecover()){RecoveryWarps++;EndPlay();}
        if((cc.collisionFlags&CollisionFlags.Above)!=0 && vy>0)vy=0;
        lastPosition=transform.position;
    }
    bool TryRecover()
    {
        for(int i=0;i<12;i++)
        {
            Vector3 offset=Quaternion.Euler(0,i*30,0)*slotDir*idleDistance;
            if(SafeGoal(target.position+offset,out var ground)){Warp(ground);return true;}
        }
        return false;
    }
    public void Warp(Vector3 pos)
    {
        if(!cc)cc=GetComponent<CharacterController>();
        cc.enabled=false;transform.position=pos;cc.enabled=true;
        hVel=hAcc=Vector3.zero;vy=0;chasing=false;stuckTime=0;lastPosition=pos;
        if(target)lastTargetPos=target.position;
    }
    void OnDrawGizmosSelected()
    {
        if(!target)return;
        Gizmos.color=new Color(.4f,.9f,1,.7f);Gizmos.DrawWireSphere(target.position,followDistance);
        if(Application.isPlaying){Gizmos.color=Color.yellow;Gizmos.DrawLine(transform.position,CurrentGoal);Gizmos.DrawWireSphere(CurrentGoal,.15f);}
    }
}
