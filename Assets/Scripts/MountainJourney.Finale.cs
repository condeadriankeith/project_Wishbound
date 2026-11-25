using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class MountainJourney
{
    IEnumerator SummitFarewell()
    {
        Busy = true;
        director = CutsceneDirector.Begin();
        var follower = child.GetComponent<ProceduralFollow>();
        var performer = child.GetComponent<ChildPerformer>();
        if (follower) follower.enabled = false;
        var childCollider = child.GetComponent<CharacterController>();
        if (childCollider) childCollider.enabled = false;
        var starCollider = star.GetComponent<CharacterController>();
        if (starCollider) starCollider.enabled = false;
        CutsceneSkip.Begin(() => skipCut = true);

        Vector3 from = star.position;
        Vector3 side = Vector3.Cross(Vector3.up, benchForward);
        Vector3 childMark = child.position;
        Vector3 core = from + Vector3.up * 1.12f;
        Vector3 faceChild = childMark + Vector3.up * 1.03f;
        Vector3 sky = summit + Vector3.up * 139f + benchForward * 37f;
        var night = FindFirstObjectByType<LivingDayNight>();
        float atHour = night ? night.CurrentHour : 19f;
        if (night) { night.scripted = true; night.scriptedHour = atHour; }

        director.Cut(core + side * 2.7f + benchForward * 2.35f + Vector3.up * .75f,
            (core + lastFragment.position) * .5f, 41);
        Speak("Child: When you're up there, will the pond still look this blue?");
        if (performer) performer.Act(ChildEmotion.Warm,star);
        yield return Beat(4.8f);
        yield return GlowReply(4.3f);
        Speak("Child: Wait. There's a light under my seat. I thought it was the water.");
        if (performer) { performer.lookTarget = null; performer.lookPoint = lastFragment.position; performer.emotion = ChildEmotion.Reach; }
        yield return Beat(4.8f);
        yield return PondReflection(side);
        ClearSpeech();
        yield return StarFragment.Merge(lastFragment, star, k =>
        {
            if (night) night.scriptedHour = Mathf.Lerp(atHour,22.45f,k);
        }, () => skipCut);
        StoryFlags.Set("SummitFragmentRecovered");
        StoryFlags.Nudge(12f, "Recovered the last fragment");

        if (!skipCut)
        {
            var gate = WishSkyAperture.Create(transform, sky, sky - core);
            var intro = WishboundIntro.Instance;
            var music = MusicDirector.Instance;
            MagicCue.NightSkyReveal();
            var radiance = StarRadiance.Instance;
            var fx = star.GetComponentInChildren<StarOrbFX>();
            Vector3 lookout = summit - side * 4.3f - benchForward * 4.6f + Vector3.up * 1.3f;
            Vector3 lookAtSky = (sky-core).normalized;
            director.Cut(lookout, core + benchForward * 1.1f, 48f);
            yield return StoryShot(3.4f, t =>
            {
                float tilt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f,.58f,t));
                gate.SetOpen(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f,.94f,t)));
                star.rotation = Quaternion.Slerp(Quaternion.LookRotation(benchForward),
                    Quaternion.LookRotation(lookAtSky), t);
                if (performer) { performer.emotion = ChildEmotion.Awe; performer.lookPoint = sky; performer.lookTarget = null; }
                director.TrackFrame(lookout + side * .6f * t,
                    Vector3.Lerp(core + benchForward*1.1f,sky,tilt), Mathf.Lerp(48,58,t));
            });

            star.rotation = Quaternion.LookRotation(FlatDirection(from,childMark));
            if (performer) { performer.emotion = ChildEmotion.Wave; performer.lookPoint = core; }
            Speak("Child: I tried to remember the exact colour of you. Then I shut my eyes and forgot.");
            var reply = core + side * 3.1f + benchForward * 2.8f + Vector3.up * .5f;
            director.Cut(reply, (faceChild + core) * .5f, 39f);
            yield return StoryShot(5.6f, t =>
            {
                if (radiance) radiance.flare = Mathf.Sin(t * Mathf.PI) * .58f;
                director.TrackFrame(reply - side * .65f * t,Vector3.Lerp((faceChild+core)*.5f, faceChild,t*.42f),39f);
            });
            yield return GlowReply(3.3f);
            if (performer) performer.Act(ChildEmotion.Warm,star);
            Speak("Child: You waited for me on the hill. Even when you could already fly.");
            yield return Beat(5.5f);
            yield return GlowReply(5.1f);
            Speak("Child: I'll bring a story. You bring the light.");
            yield return Beat(4.2f);
            ClearSpeech();

            if (performer) { performer.emotion = ChildEmotion.Awe; performer.lookPoint = sky; }
            Vector3 gathering = core + Vector3.up * 2.45f;
            director.Cut(core + side * 3.15f - benchForward * 2.4f + Vector3.up * .9f, core, 46f);
            yield return StoryShot(4.8f, t =>
            {
                star.position = from + Vector3.up * (t * 2.45f);
                star.rotation = Quaternion.LookRotation(lookAtSky);
                if (radiance) radiance.flare = t * .55f;
                if (fx) fx.cometBlend = t * .48f;
                gate.Shimmer(t);
                director.TrackFrame(core + side * (3.15f + t * .6f) - benchForward * 2.4f +
                    Vector3.up * (.9f + 1.6f*t), Vector3.Lerp(core, gathering, t), Mathf.Lerp(46f,51f,t), -4f*t);
            });

            MagicCue.Ascent();
            var ascent = WishCometVisual.Create(star);
            ascent.autoBurstAt = 2;
            ascent.Atmosphere = 0f;
            ascent.PrimeFlight(lookAtSky);
            Vector3 liftStart = star.position;
            Vector3 chase = summit + side * 6.5f - benchForward * 8f + Vector3.up * 1.3f;
            director.Cut(chase, gathering, 53f);

            // lightspeed streaks that pour past the star as it accelerates
            const int StreakCount = 18;
            var streaks = new LineRenderer[StreakCount];
            var streakOffset = new Vector3[StreakCount];
            var streakLen = new float[StreakCount];
            Vector3 flightDir = (sky - liftStart).normalized;
            Vector3 perpA = Vector3.Cross(flightDir, Vector3.up).normalized;
            Vector3 perpB = Vector3.Cross(flightDir, perpA).normalized;
            for (int i = 0; i < StreakCount; i++)
            {
                streaks[i] = gate.MakeWake(liftStart, true);
                float ang = Random.value * Mathf.PI * 2f, rad = Mathf.Lerp(.35f, 3.4f, Mathf.Sqrt(Random.value));
                streakOffset[i] = (perpA * Mathf.Cos(ang) + perpB * Mathf.Sin(ang)) * rad;
                streakLen[i] = Mathf.Lerp(.45f, 1f, Random.value);
            }
            float headNow = 0f, ionNow = 0f, dustNow = 0f;
            yield return StoryShot(9.5f, t =>
            {
                float speed = Mathf.Pow(t, 2.3f);
                star.position = Vector3.Lerp(liftStart, sky, speed);
                if (fx) fx.cometBlend = Mathf.Lerp(.48f, 1f, Mathf.Min(1f, t * 1.6f));
                if (radiance) radiance.flare = Mathf.Lerp(.55f, 1.1f, speed);
                ascent.Progress = 1f - t;
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.9f, 1f, t));
                headNow = Mathf.Lerp(.035f, .26f, speed) * Mathf.Lerp(1f, .35f, fade);
                ascent.headScale = headNow;
                ionNow = Mathf.Min(320f * headNow, .6f + Vector3.Distance(liftStart, star.position));
                dustNow = ionNow * .75f;
                ascent.ionLength = ionNow; ascent.dustLength = dustNow;
                float energy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.12f, .7f, t));
                for (int i = 0; i < StreakCount; i++)
                {
                    Vector3 head = star.position + streakOffset[i] * Mathf.Lerp(.6f, 1.5f, speed) - flightDir * (.5f + 1.5f * streakLen[i]);
                    Vector3 tail = head - flightDir * (streakLen[i] * Mathf.Lerp(.5f, 70f, energy));
                    streaks[i].SetPosition(0, tail); streaks[i].SetPosition(1, head);
                    streaks[i].widthMultiplier = Mathf.Lerp(.02f, .14f, energy) * (1f - fade * .4f);
                    streaks[i].endColor = new Color(.65f, 1.2f, 2.1f, .75f * energy);
                }
                float tilt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.30f, .9f, t));
                float fov = Mathf.Lerp(53f, 82f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.15f, .8f, t)));
                fov = Mathf.Lerp(fov, 46f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.82f, 1f, t)));
                Vector3 jitter = Random.insideUnitSphere * .06f * energy * (1f - fade);
                director.TrackFrame(chase + (star.position - liftStart) * .38f + Vector3.up * (t * 5f) + jitter,
                    Vector3.Lerp(star.position, sky, tilt * .7f), fov, -6f * (1f - tilt));
            });

            // the star dissolves into the gate: trails drain into it and fade, instead of cutting out
            MagicCue.Arrival();
            Vector3 endPos = star.position;
            Vector3 scale0 = star.localScale;
            yield return StoryShot(2.8f, t =>
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                float rest = 1f - k;
                ascent.Progress = 0f;
                ascent.headScale = headNow * rest;
                ascent.ionLength = ionNow * rest * rest; ascent.dustLength = dustNow * rest * rest;
                star.localScale = scale0 * Mathf.Lerp(1f, .03f, k);
                if (radiance) radiance.flare = Mathf.Lerp(1.1f, 0f, k);
                for (int i = 0; i < StreakCount; i++)
                {
                    Vector3 head = endPos + streakOffset[i] * rest - flightDir * (.5f + 1.5f * streakLen[i]) * rest;
                    Vector3 tail = head - flightDir * (streakLen[i] * 70f * rest * rest);
                    streaks[i].SetPosition(0, tail); streaks[i].SetPosition(1, head);
                    streaks[i].widthMultiplier = .14f * rest;
                    streaks[i].endColor = new Color(.65f, 1.2f, 2.1f, .75f * rest);
                }
                gate.SetGlint(Mathf.Sin(Mathf.Clamp01(t * 1.7f) * Mathf.PI));
                gate.Shimmer(rest);
                director.TrackFrame(chase + (endPos - liftStart) * .38f + Vector3.up * 5f, Vector3.Lerp(endPos, sky, .7f), Mathf.Lerp(46f, 42f, k));
            });
            gate.SetGlint(0f);
            foreach (var line in streaks) if (line) Destroy(line.gameObject);
            star.localScale = scale0;
            Destroy(ascent.gameObject);
            var visual = WishboundIntro.Instance ? WishboundIntro.Instance.starVisual : null;
            if (visual) visual.gameObject.SetActive(false);
            if (radiance) radiance.flare = 0f;
            yield return StoryShot(3.8f, t =>
            {
                gate.SetOpen(1f-t);

                director.TrackFrame(chase + Vector3.up*5f, sky, Mathf.Lerp(39f,42f,t));
            });

            yield return Beat(.40f);
            MagicCue.Layer("Magic/CrystalTwinkle", .4f, 0f, .9f, .05f, 2f);
            yield return StoryShot(.95f,t => gate.SetGlint(Mathf.Sin(t*Mathf.PI)));
            gate.SetGlint(0f);
            yield return Beat(.35f);
        }

        ClearSpeech();
        CutsceneSkip.End();
        StoryFlags.Set("TetherEnding");
        Advance(Step.Complete);
        yield return FadeFinale();
        WishboundIntro.QueueFinaleTitle();
        StoryFlags.NewDemoRun();
        yield return SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
    }

    IEnumerator PondReflection(Vector3 side)
    {
        Vector3 under = lastFragment.position;
        director.Cut(under + benchForward*2.1f + side*.9f + Vector3.up*.2f,under,36);
        Speak("Follow the reflection. Press Space each time the two lights meet.");
        var reflection = Part("Pond reflection",VillageKit.Blob(83,.06f,3,8),glow,under,Vector3.one*.10f);
        Vector3 mark = summit + benchForward*2.6f + Vector3.up*.48f;
        var echo = Part("Reflected star",reflection.GetComponent<MeshFilter>().sharedMesh,glow,mark,Vector3.one*.16f);
        director.Cut(mark-benchForward*2.2f+Vector3.up*1.1f,mark,36);
        for (int note=0;note<3 && !skipCut;note++)
        {
            float phase = -1.57f;
            while (!skipCut)
            {
                phase += Time.deltaTime*(2f+note*.3f);
                float offset = Mathf.Sin(phase);
                reflection.transform.position = mark+side*offset;
                if (WishboundInput.Pressed(UnityEngine.InputSystem.Key.Space,KeyCode.Space))
                {
                    if (Mathf.Abs(offset)<.28f) break;
                    Speak("Watch the moving light cross the still one. Press Space as they touch.");
                }
                yield return null;
            }
            mark += benchForward*.32f;
            echo.transform.position = mark;
            echo.transform.localScale *= 1.25f;
        }
        Destroy(reflection); Destroy(echo);
    }

    IEnumerator FadeFinale()
    {
        var overlay = new GameObject("Return to title fade", typeof(Canvas));
        overlay.transform.SetParent(transform, false);
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;
        var plane = new GameObject("Black", typeof(RectTransform), typeof(Image));
        plane.transform.SetParent(overlay.transform, false);
        var rect = (RectTransform)plane.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = plane.GetComponent<Image>();
        image.raycastTarget = false;
        for (float t = 0; t < 1.45f; t += Time.unscaledDeltaTime)
        {
            image.color = new Color(0,0,0,Mathf.SmoothStep(0,1,t/1.45f));
            yield return null;
        }
        image.color = Color.black;
    }
}

