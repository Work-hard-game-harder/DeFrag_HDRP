using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeFrag.B1F
{
    [Serializable]
    public sealed class CutsceneShot
    {
        public string label;
        [Min(0f)] public float start;
        public Transform from;
        [Tooltip("비워 두면 고정 샷입니다.")]
        public Transform to;
        [Range(10f, 110f)] public float fovFrom = 50f;
        [Range(10f, 110f)] public float fovTo = 50f;
        [Range(0f, 2f)] public float handheld = 0.4f;
        [Tooltip("CCTV 화면 오버레이를 씌웁니다.")]
        public bool cctv;
    }

    [Serializable]
    public sealed class DoorBreachTimings
    {
        [Tooltip("복도 조명이 순서대로 꺼지는 시각 (몬스터가 지나감).")]
        public float[] corridorLightsOff = { 1.2f, 1.8f, 2.4f, 3.0f };
        public float signalLoss = 3.2f;
        public float impact1 = 5.2f;
        public float relief = 8.9f;
        public float impact2 = 10.4f;
        public float creepStart = 11.0f;
        public float lightsSettle = 13.8f;
        public float silence = 14.4f;
        public float breach = 15.7f;
        public float cameraDrop = 16.0f;
        public float backlight = 16.9f;
        public float monsterStep = 18.9f;
        public float monsterNotice = 20.4f;
        public float cutToBlack = 21.1f;
        public float end = 22.6f;
    }

    /// <summary>
    /// "Door breach 2": realtime cutscene for the BreachVideo stage. CCTV sees something pass,
    /// two blows shake the control-room door, the players creep closer, the door explodes, the
    /// camera is knocked to the floor and the TV monster is revealed against red backlight.
    /// Everything here is local presentation; the story stage is advanced by the server.
    /// </summary>
    public sealed class B1FDoorBreachCutscene : B1FStoryCutscene
    {
        [Header("View")]
        [SerializeField] private CutsceneCameraRig cameraRig;
        [SerializeField] private CutsceneShot[] shots = Array.Empty<CutsceneShot>();
        [SerializeField] private Transform dropRest;
        [SerializeField, Min(0.05f)] private float dropSeconds = 0.6f;
        [SerializeField, Range(-45f, 45f)] private float dropRoll = 16f;
        [SerializeField] private string cctvLabel = "CAM 07  //  B1F 남측 복도";
        [SerializeField] private int overlaySortingOrder = 32000;

        [Header("Timing (seconds from start)")]
        [SerializeField] private DoorBreachTimings timing = new();

        [Header("Door")]
        [SerializeField] private BreachableDoor door;
        [Tooltip("문 중앙. 앞(파란 축)이 관제실 안쪽을 향해야 합니다.")]
        [SerializeField] private Transform breachOrigin;
        [SerializeField, Min(0f)] private float firstDent = 0.025f;
        [SerializeField, Min(0f)] private float secondDent = 0.08f;

        [Header("Actors (stand-ins for the two players)")]
        [SerializeField] private GameObject actorModel;
        [SerializeField] private RuntimeAnimatorController actorController;
        [SerializeField] private Material[] actorMaterials;
        [SerializeField] private Transform actorADesk;
        [SerializeField] private Transform actorBDesk;
        [SerializeField] private Transform[] actorAPath;
        [SerializeField] private Transform[] actorBPath;
        [SerializeField] private Transform monitorFocus;
        [SerializeField, Min(0.05f)] private float creepSpeed = 0.95f;

        [Header("Monster")]
        [SerializeField] private GameObject monsterModel;
        [SerializeField] private RuntimeAnimatorController monsterController;
        [SerializeField, Min(0.1f)] private float monsterScale = 1f;
        [SerializeField] private string monsterWalkState = "Walking";
        [SerializeField] private string monsterIdleState = "Idle";
        [SerializeField, Min(0.1f)] private float monsterNominalSpeed = 1.4f;
        [SerializeField] private Transform monsterCorridorStart;
        [SerializeField] private Transform monsterCorridorEnd;
        [SerializeField] private Transform monsterDoorway;
        [SerializeField] private Transform monsterStepTarget;
        [SerializeField] private Transform[] monsterScanPoints;
        [SerializeField] private Light monsterGlow;
        [SerializeField, ColorUsage(false, true)] private Color screenGlow = new(4f, 0.35f, 0.3f);

        [Header("Lights")]
        [Tooltip("관제실 조명. 켜져 있는 것만 깜빡이고 끝나면 원래대로 돌아갑니다.")]
        [SerializeField] private Light[] roomLights;
        [Tooltip("컷씬 전용 복도 조명 (몬스터가 지나가는 순서).")]
        [SerializeField] private Light[] corridorLights;
        [Tooltip("파손 후 복도 쪽에서 방 안을 비추는 붉은 역광들.")]
        [SerializeField] private Light[] redBacklights;
        [SerializeField] private Light sparkLight;

        [Header("Effects")]
        [SerializeField] private ParticleSystem impactDust;
        [SerializeField] private ParticleSystem ceilingDust;
        [SerializeField] private ParticleSystem breachBurst;
        [SerializeField] private ParticleSystem breachSparks;
        [SerializeField] private ParticleSystem lightSparks;
        [SerializeField] private ParticleSystem floatingMotes;
        [SerializeField] private DoorBreachDebris debris;
        [Tooltip("먼지는 조명을 받지 않으므로 장면 조명에 맞춰 색을 입힙니다.")]
        [SerializeField] private Color dustRoomTint = new(1.9f, 2.05f, 2.15f, 0.8f);
        [SerializeField] private Color dustFlashTint = new(1.6f, 1.45f, 1.3f, 1f);
        [SerializeField] private Color dustDarkTint = new(0.18f, 0.16f, 0.16f, 0.7f);
        [SerializeField] private Color dustRedTint = new(1.2f, 0.26f, 0.2f, 0.8f);

        [Header("Audio")]
        [SerializeField] private AudioSource sfx;
        [SerializeField] private AudioSource loop;
        [SerializeField] private AudioSource bed;
        [SerializeField] private AudioSource ringing;
        [SerializeField] private AudioLowPassFilter muffle;
        [SerializeField] private AudioClip impactLightClip;
        [SerializeField] private AudioClip impactHeavyClip;
        [SerializeField] private AudioClip explodeClip;
        [SerializeField] private AudioClip monsterStepClip;
        [SerializeField] private AudioClip lightBuzzClip;
        [SerializeField] private AudioClip sparkClip;
        [SerializeField] private AudioClip signalLossClip;
        [SerializeField] private AudioClip tvStaticClip;
        [SerializeField] private AudioClip heartbeatClip;
        [SerializeField] private AudioClip tinnitusClip;
        [SerializeField] private AudioClip cameraDropClip;
        [SerializeField] private AudioClip doorCreakClip;
        [SerializeField] private AudioClip tensionBedClip;
        [SerializeField] private AudioClip dreadPadClip;

        [Header("Preview (Play Mode only)")]
        [Tooltip("미리보기 시작 시각. 앞의 이벤트는 즉시 적용됩니다.")]
        [SerializeField, Min(0f)] private float previewStartTime;

        private readonly List<(float time, Action action)> events = new();
        private readonly List<float> roomLightBase = new();
        private readonly List<float> corridorLightBase = new();
        private readonly List<float> backlightBase = new();
        private readonly List<ParticleSystemRenderer> dustRenderers = new();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock dustBlock;
        private float redLevel;
        private float glowBase;
        private float sparkBase;

        private Action finished;
        private LocalCutsceneStage stage;
        private CutsceneOverlay overlay;
        private CutsceneActor actorA;
        private CutsceneActor actorB;
        private CutsceneActor monster;
        private Material monsterScreen;
        private bool playing;
        private float elapsed;
        private int nextEvent;
        private int shotIndex = -1;

        private float doorShake;
        private float doorDent;
        private float doorTilt;
        private Vector3 panelPosition;
        private Quaternion panelRotation;
        private float glowFlare;
        private float glitchSpike;

        public override float Duration => timing.end;
        public override bool IsPlaying => playing;

        private Vector3 DoorPoint => breachOrigin != null ? breachOrigin.position : door != null ? door.transform.position + Vector3.up * 1.6f : transform.position;
        private Vector3 IntoRoom => breachOrigin != null ? breachOrigin.forward : Vector3.forward;

        public float Elapsed => elapsed;

        [ContextMenu("Preview (Play Mode)")]
        private void Preview() => PlayPreview(previewStartTime);

        /// <summary>Editor/testing entry: play locally without the story, optionally skipping ahead.</summary>
        public void PlayPreview(float fromTime)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Door Breach Cutscene] 플레이 모드에서만 미리볼 수 있습니다.", this); return; }
            Play(null);
            if (fromTime > 0f) FastForward(fromTime);
        }

        public override void Play(Action onFinished)
        {
            if (playing) Stop();
            playing = true;
            finished = onFinished;
            elapsed = 0f;
            nextEvent = 0;
            shotIndex = -1;
            doorShake = doorDent = doorTilt = glowFlare = glitchSpike = 0f;

            overlay = CutsceneOverlay.Create(transform, overlaySortingOrder);
            stage = new LocalCutsceneStage(cameraRig != null ? cameraRig.View : null, overlay.transform);
            stage.Enter();
            if (cameraRig != null) cameraRig.ResetClock();

            CaptureLights();
            CollectDustRenderers();
            if (door != null)
            {
                door.SetStandInActive(true);
                if (door.StandInPanel != null)
                {
                    panelPosition = door.StandInPanel.localPosition;
                    panelRotation = door.StandInPanel.localRotation;
                }
            }
            if (muffle != null) muffle.cutoffFrequency = 22000f;
            SpawnActors();
            BuildEvents();
        }

        public override void Stop()
        {
            if (!playing) return;
            playing = false;
            RestoreLights();
            ClearDustTint();
            if (door != null)
            {
                if (door.StandInPanel != null)
                {
                    door.StandInPanel.localPosition = panelPosition;
                    door.StandInPanel.localRotation = panelRotation;
                }
                door.RestoreFromState();
            }
            foreach (ParticleSystem system in new[] { impactDust, ceilingDust, breachBurst, breachSparks, lightSparks, floatingMotes })
                if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (AudioSource source in new[] { sfx, loop, bed, ringing })
                if (source != null) source.Stop();
            if (debris != null) debris.Freeze();
            if (monsterScreen != null) Destroy(monsterScreen);
            foreach (CutsceneActor actor in new[] { actorA, actorB, monster })
                if (actor != null) Destroy(actor.gameObject);
            actorA = actorB = monster = null;
            stage?.Exit();
            stage = null;
            if (overlay != null) Destroy(overlay.gameObject);
            overlay = null;
            events.Clear();
        }

        private void OnDisable() => Stop();

        private void Update()
        {
            if (!playing) return;
            elapsed += Time.deltaTime;
            Step();
            if (elapsed >= timing.end) Finish();
        }

        private void Step()
        {
            while (nextEvent < events.Count && events[nextEvent].time <= elapsed)
                events[nextEvent++].action();
            UpdateShot();
            UpdateDoor();
            UpdateLights();
            UpdateMonster();
            UpdateOverlayAndAudio();
        }

        private void FastForward(float time)
        {
            elapsed = time;
            UpdateShot();
            while (nextEvent < events.Count && events[nextEvent].time <= elapsed)
                events[nextEvent++].action();
        }

        private void Finish()
        {
            Action callback = finished;
            finished = null;
            Stop();
            callback?.Invoke();
        }

        // ---------------------------------------------------------------- setup

        private void SpawnActors()
        {
            Material[] materials = actorMaterials != null && actorMaterials.Length > 0 ? actorMaterials : PlayerMaterials();
            if (actorModel != null && actorController != null)
            {
                if (actorADesk != null) actorA = CutsceneActor.Spawn(actorModel, actorController, transform, actorADesk.position, actorADesk.eulerAngles.y, materials);
                if (actorBDesk != null) actorB = CutsceneActor.Spawn(actorModel, actorController, transform, actorBDesk.position, actorBDesk.eulerAngles.y, materials);
                Vector3 focus = monitorFocus != null ? monitorFocus.position : DoorPoint;
                if (actorA != null) actorA.LookAt(focus, 0.7f, 100f);
                if (actorB != null) actorB.LookAt(focus + Vector3.forward * 0.8f, 0.6f, 100f);
            }
            if (monsterModel != null && monsterController != null && monsterCorridorStart != null)
            {
                monster = CutsceneActor.Spawn(monsterModel, monsterController, transform, monsterCorridorStart.position,
                    monsterCorridorStart.eulerAngles.y, null, monsterScale);
                monster.UseStates(monsterWalkState, monsterIdleState, monsterNominalSpeed);
                monster.SetLookLimits(70f, 40f);
                foreach (Renderer renderer in monster.GetComponentsInChildren<Renderer>())
                {
                    Material[] shared = renderer.sharedMaterials;
                    for (int i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] == null || !shared[i].name.Contains("Screen")) continue;
                        monsterScreen = new Material(shared[i]);
                        Material[] copy = renderer.materials;
                        copy[i] = monsterScreen;
                        renderer.materials = copy;
                    }
                }
            }
        }

        private static Material[] PlayerMaterials()
        {
            foreach (var player in LocalCutsceneStage.Players())
            {
                var skin = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin != null) return skin.sharedMaterials;
            }
            return null;
        }

        private void At(float time, Action action) => events.Add((time, action));

        private void BuildEvents()
        {
            events.Clear();
            var t = timing;
            Vector3 doorPoint = DoorPoint;
            Vector3 monitor = monitorFocus != null ? monitorFocus.position : doorPoint;

            At(0f, () =>
            {
                overlay.FadeTo(0f, 3f);
                PlayBed(tensionBedClip, 0.75f);
                SetActive(corridorLights, true);
                if (monster != null && monsterCorridorEnd != null)
                {
                    float travel = Vector3.Distance(monsterCorridorStart.position, monsterCorridorEnd.position);
                    monster.Walk(new[] { monsterCorridorEnd.position }, travel / Mathf.Max(0.5f, t.signalLoss - 0.1f), 1f, 1f);
                }
            });
            // Heavy footsteps coming closer (heard from the control room).
            for (float s = 0.25f, v = 0.3f; s < t.signalLoss + 0.2f; s += 0.58f, v = Mathf.Min(0.9f, v + 0.14f))
            {
                float volume = v;
                At(s, () => Sfx(monsterStepClip, volume));
            }
            for (int i = 0; i < t.corridorLightsOff.Length; i++)
            {
                int index = i;
                At(t.corridorLightsOff[i] - 0.18f, () => { if (index == 0) Sfx(lightBuzzClip, 0.35f); });
            }
            At(t.signalLoss, () => { Sfx(signalLossClip, 0.8f); glitchSpike = 0.8f; });
            At(t.signalLoss + 0.3f, () => overlay.ShowNoSignal(true));

            float roomStart = shots.Length > 1 ? shots[1].start : t.signalLoss + 0.6f;
            At(roomStart, () =>
            {
                overlay.ShowNoSignal(false);
                if (monster != null)
                {
                    monster.Halt();
                    monster.gameObject.SetActive(false);
                }
            });

            // Blow one: startle.
            At(t.impact1, () =>
            {
                Sfx(impactLightClip, 1f);
                Trauma(0.55f);
                doorShake = 1f;
                doorDent = firstDent;
                Emit(impactDust);
                Emit(ceilingDust);
                ForActors(a => { a.Flinch(doorPoint, 1f); a.LookAt(doorPoint, 0.85f, 10f); });
            });
            At(t.impact1 + 0.45f, () => { if (actorB != null) actorB.FacePoint(doorPoint, 150f); });
            At(t.impact1 + 0.7f, () => { if (actorA != null) actorA.FacePoint(doorPoint, 130f); ForActors(a => a.LookAt(doorPoint, 1f, 3f)); });
            At(t.impact1 + 1.1f, () => Sfx(doorCreakClip, 0.45f));
            At(t.relief - 1.1f, () => Sfx(heartbeatClip, 0.35f));
            // A nervous glance at each other, then back to work.
            At(t.relief, () => { if (actorA != null && actorB != null) actorA.LookAt(actorB.Head.position, 1f, 4f); });
            At(t.relief + 0.35f, () => { if (actorA != null && actorB != null) actorB.LookAt(actorA.Head.position, 0.8f, 4f); });
            At(t.relief + 0.8f, () => ForActors(a => { a.FacePoint(monitor, 85f); a.LookAt(monitor, 0.6f, 2f); }));

            // Blow two: the door buckles and the lights fail.
            At(t.impact2, () =>
            {
                Sfx(impactHeavyClip, 1f);
                Sfx(sparkClip, 0.7f);
                Trauma(0.85f);
                doorShake = 1.7f;
                doorDent = secondDent;
                doorTilt = 2.2f;
                Emit(impactDust);
                Emit(ceilingDust);
                Emit(lightSparks);
                glowFlare = 0f;
                ForActors(a => { a.Flinch(doorPoint, 1.5f); a.FacePoint(doorPoint, 420f); a.LookAt(doorPoint, 1f, 12f); });
                PlayLoop(lightBuzzClip, 0.45f);
            });
            At(t.creepStart, () =>
            {
                if (actorA != null) actorA.Walk(Positions(actorAPath), creepSpeed, 1.4f, 0.75f);
                if (actorB != null) actorB.Walk(Positions(actorBPath), creepSpeed * 0.92f, 1.4f, 0.75f);
                ForActors(a => a.LookAt(doorPoint, 1f, 2f));
            });
            At(t.lightsSettle, () => { if (loop != null) loop.Stop(); });
            // Dead silence before the breach.
            At(t.silence, () => { if (bed != null) bed.Stop(); });
            At(t.silence + 0.25f, () => Sfx(heartbeatClip, 0.5f));
            At(t.silence + 0.85f, () => Sfx(heartbeatClip, 0.55f));

            // Breach.
            At(t.breach, () =>
            {
                Sfx(explodeClip, 1f);
                overlay.Flash(0.95f);
                Trauma(1f);
                glitchSpike = 0.6f;
                if (door != null) door.ShowBrokenLocally();
                if (debris != null && breachOrigin != null) debris.Burst(breachOrigin, IntoRoom, LocalCutsceneStage.PlayerColliders());
                Emit(breachBurst);
                Emit(breachSparks);
                ForActors(a => { a.Flinch(doorPoint, 2.2f); a.Shove(doorPoint, 2.4f); });
                if (monster != null && monsterDoorway != null)
                {
                    monster.transform.SetPositionAndRotation(monsterDoorway.position, monsterDoorway.rotation);
                    monster.FaceYaw(monsterDoorway.eulerAngles.y, 0f);
                    monster.gameObject.SetActive(true);
                    monster.Halt();
                }
            });
            At(t.breach + 0.08f, () => { ForActors(a => a.SetCrouch(true)); PlayRinging(); });
            At(t.cameraDrop, () => { if (cameraRig != null) cameraRig.Drop(dropRest, dropSeconds, dropRoll); });
            At(t.cameraDrop + dropSeconds * 0.62f, () => Sfx(cameraDropClip, 0.8f));
            At(t.backlight - 0.4f, () => PlayBed(dreadPadClip, 0.7f));
            At(t.backlight, () => Emit(floatingMotes));
            // The monster scans the room, steps in, then notices the fallen camera.
            if (monsterScanPoints != null)
                for (int i = 0; i < monsterScanPoints.Length; i++)
                {
                    Transform point = monsterScanPoints[i];
                    At(t.backlight + 0.5f + i * 0.85f, () => { if (point != null) if (monster != null) monster.LookAt(point.position, 0.9f, 2.5f); });
                }
            At(t.monsterStep, () =>
            {
                if (monster != null && monsterStepTarget != null) monster.Walk(new[] { monsterStepTarget.position }, 0.7f, 1f, 1f, false);
                Sfx(monsterStepClip, 0.9f);
            });
            At(t.monsterStep + 0.62f, () => Sfx(monsterStepClip, 1f));
            At(t.monsterNotice, () =>
            {
                if (monster != null && cameraRig != null) monster.LookAt(cameraRig.transform.position, 1f, 30f);
                Sfx(tvStaticClip, 1f);
                glowFlare = 1f;
                glitchSpike = 1f;
                Trauma(0.35f);
            });
            At(t.cutToBlack, () =>
            {
                overlay.CutToBlack();
                overlay.SetGlitch(0f);
                if (bed != null) bed.Stop();
                glitchSpike = 0f;
            });

            events.Sort((a, b) => a.time.CompareTo(b.time));
        }

        private static IEnumerable<Vector3> Positions(Transform[] points)
        {
            if (points == null) yield break;
            foreach (Transform point in points) if (point != null) yield return point.position;
        }

        private void ForActors(Action<CutsceneActor> action)
        {
            if (actorA != null) action(actorA);
            if (actorB != null) action(actorB);
        }

        // ---------------------------------------------------------------- per-frame

        private void UpdateShot()
        {
            int index = -1;
            for (int i = 0; i < shots.Length; i++) if (shots[i].start <= elapsed) index = i;
            if (index == shotIndex || index < 0) return;
            shotIndex = index;
            CutsceneShot shot = shots[index];
            float end = index + 1 < shots.Length ? shots[index + 1].start : timing.end;
            if (cameraRig != null) cameraRig.Cut(shot.from, shot.to, shot.fovFrom, shot.fovTo, end - shot.start, shot.handheld);
            overlay.SetCctv(shot.cctv, cctvLabel);
            overlay.SetLetterbox(shot.cctv ? 0f : 0.1f);
            overlay.SetVignette(shot.cctv ? 0.55f : 0.4f);
            overlay.SetStatic(0f);
        }

        private void UpdateDoor()
        {
            if (door == null || door.StandInPanel == null || !door.StandInPanel.gameObject.activeSelf) return;
            doorShake = Mathf.MoveTowards(doorShake, 0f, Time.deltaTime * 1.6f);
            float wobble = doorShake * doorShake;
            Vector3 inward = door.StandInPanel.parent != null ? door.StandInPanel.parent.InverseTransformDirection(IntoRoom) : IntoRoom;
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(elapsed * 30f, 1f) - 0.5f, Mathf.PerlinNoise(elapsed * 30f, 4f) - 0.5f, Mathf.PerlinNoise(elapsed * 30f, 9f) - 0.5f) * 0.05f * wobble;
            door.StandInPanel.localPosition = panelPosition + inward * (doorDent + Mathf.Sin(elapsed * 45f) * 0.015f * wobble) + jitter;
            door.StandInPanel.localRotation = panelRotation * Quaternion.Euler(doorTilt + (Mathf.PerlinNoise(elapsed * 25f, 2f) - 0.5f) * 3f * wobble, 0f, (Mathf.PerlinNoise(elapsed * 25f, 7f) - 0.5f) * 2f * wobble);
        }

        private void UpdateLights()
        {
            var t = timing;
            float room;
            if (elapsed >= t.breach) room = elapsed < t.breach + 0.12f ? 2.2f : 0f;                // flare, then blackout
            else if (elapsed >= t.impact2 && elapsed < t.lightsSettle) room = Strobe(elapsed, 0.75f);
            else if (elapsed >= t.impact1 && elapsed < t.impact1 + 0.45f) room = Strobe(elapsed, 0.3f);
            else if (elapsed >= t.silence) room = 0.55f;                                              // dimmed, holding its breath
            else room = 1f;
            for (int i = 0; i < roomLights.Length && i < roomLightBase.Count; i++)
                if (roomLights[i] != null) roomLights[i].intensity = roomLightBase[i] * room;

            for (int i = 0; i < corridorLights.Length && i < corridorLightBase.Count; i++)
            {
                Light light = corridorLights[i];
                if (light == null) continue;
                float off = i < t.corridorLightsOff.Length ? t.corridorLightsOff[i] : float.MaxValue;
                float level = elapsed < off - 0.2f ? 1f : elapsed < off ? Strobe(elapsed, 0.8f) : elapsed < off + 0.07f ? 0f : elapsed < off + 0.12f ? 0.5f : 0f;
                light.intensity = corridorLightBase[i] * level;
            }

            redLevel = elapsed < t.breach ? 0f
                : elapsed < t.breach + 0.25f ? 1.3f
                : elapsed < t.backlight ? 0.08f
                : Mathf.Clamp01((elapsed - t.backlight) / 0.8f) * (0.8f + 0.2f * Mathf.PerlinNoise(elapsed * 3f, 0f)) * (Mathf.PerlinNoise(elapsed * 14f, 3f) > 0.18f ? 1f : 0.35f);
            for (int i = 0; i < redBacklights.Length && i < backlightBase.Count; i++)
            {
                Light red = redBacklights[i];
                if (red == null) continue;
                red.gameObject.SetActive(redLevel > 0f && elapsed < t.cutToBlack);
                red.intensity = backlightBase[i] * redLevel;
            }
            UpdateDustTint();
            if (sparkLight != null)
            {
                bool spark = elapsed >= t.impact2 && elapsed < t.impact2 + 0.6f && UnityEngine.Random.value < 0.6f;
                sparkLight.gameObject.SetActive(spark);
                sparkLight.intensity = sparkBase * UnityEngine.Random.Range(0.4f, 1.2f);
            }
        }

        // Particles are unlit, so their tint follows the scene: room light, blackout, then red backlight.
        private void UpdateDustTint()
        {
            if (dustRenderers.Count == 0) return;
            var t = timing;
            Color tint = elapsed < t.breach ? dustRoomTint
                : elapsed < t.breach + 0.2f ? dustFlashTint
                : Color.Lerp(dustDarkTint, dustRedTint, Mathf.Clamp01(redLevel));
            dustBlock ??= new MaterialPropertyBlock();
            dustBlock.SetColor(ColorId, tint);
            foreach (ParticleSystemRenderer renderer in dustRenderers)
                if (renderer != null) renderer.SetPropertyBlock(dustBlock);
        }

        private void CollectDustRenderers()
        {
            dustRenderers.Clear();
            foreach (ParticleSystem system in new[] { impactDust, ceilingDust, breachBurst, floatingMotes })
                if (system != null) dustRenderers.AddRange(system.GetComponentsInChildren<ParticleSystemRenderer>(true));
        }

        private void ClearDustTint()
        {
            foreach (ParticleSystemRenderer renderer in dustRenderers)
                if (renderer != null) renderer.SetPropertyBlock(null);
            dustRenderers.Clear();
        }

        private static float Strobe(float time, float depth)
        {
            float n = Mathf.PerlinNoise(time * 17f, 0.37f);
            return n > 0.42f ? 1f - depth * 0.25f * UnityEngine.Random.value : 1f - depth;
        }

        private void UpdateMonster()
        {
            if (monster == null || !monster.gameObject.activeSelf) { if (monsterGlow != null) monsterGlow.gameObject.SetActive(false); return; }
            glowFlare = Mathf.MoveTowards(glowFlare, 0f, Time.deltaTime * 0.9f);
            bool revealed = elapsed >= timing.breach;
            float flicker = Mathf.PerlinNoise(elapsed * 9f, 5f) > 0.3f ? 1f : 0.25f;
            float glow = revealed ? (0.35f + 0.65f * Mathf.Clamp01((elapsed - timing.backlight) / 1.5f)) * flicker + glowFlare * 2.5f : 0f;
            if (monsterGlow != null)
            {
                monsterGlow.gameObject.SetActive(glow > 0f && elapsed < timing.cutToBlack);
                Transform head = monster.Head;
                monsterGlow.transform.position = head.position + monster.transform.forward * 0.55f;
                monsterGlow.intensity = glowBase * glow;
            }
            if (monsterScreen != null) monsterScreen.SetColor("_EmissiveColor", screenGlow * (glow * 300f));
        }

        private void UpdateOverlayAndAudio()
        {
            var t = timing;
            glitchSpike = Mathf.MoveTowards(glitchSpike, 0f, Time.deltaTime * 1.4f);
            float glitch = elapsed < t.signalLoss ? 0.04f
                : elapsed < (shots.Length > 1 ? shots[1].start : t.signalLoss + 0.6f) ? 0.5f
                : elapsed >= t.breach && elapsed < t.cutToBlack ? 0.08f + 0.1f * Mathf.Clamp01((elapsed - t.backlight) / 3f)
                : 0f;
            if (elapsed < t.cutToBlack) overlay.SetGlitch(Mathf.Max(glitch, glitchSpike));

            float roomStart = shots.Length > 1 ? shots[1].start : t.signalLoss + 0.6f;
            if (elapsed >= t.signalLoss && elapsed < roomStart) overlay.SetStatic(Mathf.Clamp01((elapsed - t.signalLoss) / 0.25f));
            // The monster's own interference bleeds into the picture when it notices the camera.
            if (elapsed >= t.monsterNotice && elapsed < t.cutToBlack) overlay.SetStatic(0.08f + 0.45f * glitchSpike);

            float settle = elapsed - (t.cameraDrop + dropSeconds * 0.6f);
            overlay.SetLensDust(settle < 0f ? 0f : Mathf.Lerp(0.45f, 0.14f, Mathf.Clamp01(settle / 2.5f)));
            if (elapsed >= t.breach) overlay.SetVignette(0.62f);

            if (muffle != null)
                muffle.cutoffFrequency = elapsed < t.breach + 0.05f ? 22000f : Mathf.Lerp(650f, 7000f, Mathf.Clamp01((elapsed - t.breach - 0.6f) / 3.5f));
        }

        // ---------------------------------------------------------------- helpers

        private void CaptureLights()
        {
            roomLightBase.Clear();
            foreach (Light light in roomLights) roomLightBase.Add(light != null ? light.intensity : 0f);
            corridorLightBase.Clear();
            foreach (Light light in corridorLights) corridorLightBase.Add(light != null ? light.intensity : 0f);
            backlightBase.Clear();
            foreach (Light light in redBacklights) backlightBase.Add(light != null ? light.intensity : 0f);
            glowBase = monsterGlow != null ? monsterGlow.intensity : 0f;
            sparkBase = sparkLight != null ? sparkLight.intensity : 0f;
        }

        private void RestoreLights()
        {
            for (int i = 0; i < roomLights.Length && i < roomLightBase.Count; i++)
                if (roomLights[i] != null) roomLights[i].intensity = roomLightBase[i];
            for (int i = 0; i < corridorLights.Length && i < corridorLightBase.Count; i++)
                if (corridorLights[i] != null) corridorLights[i].intensity = corridorLightBase[i];
            SetActive(corridorLights, false);
            for (int i = 0; i < redBacklights.Length && i < backlightBase.Count; i++)
                if (redBacklights[i] != null) { redBacklights[i].intensity = backlightBase[i]; redBacklights[i].gameObject.SetActive(false); }
            if (monsterGlow != null) { monsterGlow.intensity = glowBase; monsterGlow.gameObject.SetActive(false); }
            if (sparkLight != null) { sparkLight.intensity = sparkBase; sparkLight.gameObject.SetActive(false); }
        }

        private static void SetActive(Light[] lights, bool active)
        {
            if (lights == null) return;
            foreach (Light light in lights) if (light != null) light.gameObject.SetActive(active);
        }

        private void Trauma(float amount)
        {
            if (cameraRig != null) cameraRig.AddTrauma(amount);
        }

        private static void Emit(ParticleSystem system)
        {
            if (system == null) return;
            system.gameObject.SetActive(true);
            system.Play(true);
        }

        private void Sfx(AudioClip clip, float volume)
        {
            if (sfx != null && clip != null) sfx.PlayOneShot(clip, volume);
        }

        private void PlayLoop(AudioClip clip, float volume)
        {
            if (loop == null || clip == null) return;
            loop.clip = clip;
            loop.volume = volume;
            loop.loop = true;
            loop.Play();
        }

        private void PlayBed(AudioClip clip, float volume)
        {
            if (bed == null || clip == null) return;
            bed.clip = clip;
            bed.volume = volume;
            bed.loop = false;
            bed.Play();
        }

        private void PlayRinging()
        {
            if (ringing == null || tinnitusClip == null) return;
            ringing.clip = tinnitusClip;
            ringing.Play();
        }
    }
}
