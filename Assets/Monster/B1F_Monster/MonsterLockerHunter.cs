using System;
using System.Collections.Generic;
using DeFrag.Monsters.Common;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public enum LockerHuntStage : byte { None, Approach, Lurk, ForceOpen }

/// <summary>
/// Server-authoritative "monster checks the locker" beat for the TV monster.
/// The server decides when to investigate, tracks suspicion from the hidden player's
/// breathing and either rips the door open or wanders off. Clients only receive
/// replicated stage/pose values and one-shot audio cues.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class MonsterLockerHunter : NetworkBehaviour
{
    private enum Cue : byte
    {
        Sniff, Growl, Breath, WhisperThere, WhisperBreath, WhisperComeOut, Leave, ForceOpen
    }

    public static readonly List<MonsterLockerHunter> Active = new();

    [SerializeField] private MonsterAI monster;

    [Header("Trigger")]
    [Tooltip("추격 중이거나 이 거리 안에서 시야에 둔 플레이어가 락커에 들어가면 '목격'으로 판정합니다.")]
    [SerializeField, Min(1f)] private float witnessRange = 12f;
    [Tooltip("목격하지 못했어도 이 거리 안의 숨은 락커는 지나가다 확인합니다.")]
    [SerializeField, Min(0.5f)] private float sniffRadius = 4.5f;
    [Tooltip("숨이 차 헐떡이는 소리를 듣고 락커로 오는 거리입니다.")]
    [SerializeField, Min(1f)] private float gaspHearRadius = 15f;
    [SerializeField, Min(0f)] private float lockerCooldown = 20f;
    [SerializeField, Min(0f)] private float globalCooldown = 5f;

    [Header("Approach")]
    [SerializeField, Min(0.1f)] private float slowDownDistance = 4f;
    [SerializeField, Min(0.1f)] private float creepSpeedMultiplier = 0.65f;
    [SerializeField, Min(0.1f)] private float arrivalDistance = 0.5f;
    [SerializeField, Min(1f)] private float approachTimeout = 15f;

    [Header("Lurk")]
    [SerializeField] private Vector2 witnessedLurkSeconds = new(9f, 12f);
    [SerializeField] private Vector2 sniffLurkSeconds = new(5f, 7f);
    [SerializeField] private Vector2 cueInterval = new(2.2f, 3.8f);
    [Tooltip("의심도가 이 값 이상이면 노크 / 문 흔들기를 섞습니다.")]
    [SerializeField, Range(0f, 100f)] private float knockSuspicion = 45f;
    [SerializeField, Range(0f, 1f)] private float knockChance = 0.35f;

    [Header("Suspicion (100 = door is ripped open)")]
    [SerializeField, Range(0f, 100f)] private float witnessedStartSuspicion = 55f;
    [SerializeField, Range(0f, 100f)] private float sniffStartSuspicion = 15f;
    [SerializeField, Range(0f, 100f)] private float gaspStartSuspicion = 45f;
    [Tooltip("숨을 참지 않을 때 초당 증가량입니다.")]
    [SerializeField, Min(0f)] private float breathingPerSecond = 14f;
    [Tooltip("숨을 참는 동안 초당 감소량입니다.")]
    [SerializeField, Min(0f)] private float holdingDecayPerSecond = 5f;
    [SerializeField, Min(0f)] private float gaspSuspicion = 60f;
    [SerializeField, Min(0.5f)] private float breathHearDistance = 3f;

    [Header("Force open")]
    [SerializeField, Min(0f)] private float forceOpenWindup = 0.6f;
    [SerializeField, Min(0f)] private float forceOpenHold = 0.45f;

    [Header("Distraction (co-op)")]
    [Tooltip("수색 중 숨지 않은 플레이어가 이 거리 안에서 보이면 그쪽으로 추격을 바꿉니다.")]
    [SerializeField, Min(0f)] private float distractionSightRange = 7f;
    [SerializeField, Min(0f)] private float distractionNoiseRange = 30f;

    [Header("Audio (3D on the monster)")]
    [SerializeField] private AudioClip[] sniffClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] growlClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] breathClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip whisperThereClip;
    [SerializeField] private AudioClip whisperBreathClip;
    [SerializeField] private AudioClip whisperComeOutClip;
    [SerializeField] private AudioClip[] leaveClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip shriekClip;
    [SerializeField] private AudioClip foundWhisperClip;
    [SerializeField] private AudioClip[] staticClips = Array.Empty<AudioClip>();
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float staticVolume = 0.55f;
    [SerializeField, Min(1f)] private float audioMaxDistance = 22f;

    private readonly NetworkVariable<LockerHuntStage> stage = new(LockerHuntStage.None);
    private readonly NetworkVariable<ulong> targetLockerId = new(0);
    private readonly NetworkVariable<float> suspicion = new(0f);
    private readonly NetworkVariable<MonsterAI.ScriptedPose> pose = new(MonsterAI.ScriptedPose.Idle);

    // Server state
    private readonly Dictionary<LockerHiding, LockerPhase> observedPhases = new();
    private readonly HashSet<LockerHiding> witnessedLockers = new();
    private readonly Dictionary<LockerHiding, float> lockerReadyAt = new();
    private LockerHiding target;
    private bool targetWitnessed;
    private float stageStarted;
    private float lurkDuration;
    private float nextCueAt;
    private float globalReadyAt;
    private int cueCount;
    private AudioSource voice;
    private Animator clientAnimator;

    public LockerHuntStage Stage => stage.Value;
    public ulong TargetLockerId => targetLockerId.Value;
    public float Suspicion01 => suspicion.Value / 100f;

    private void Reset() => monster = GetComponent<MonsterAI>();

    private void Awake()
    {
        if (monster == null) monster = GetComponent<MonsterAI>();
    }

    public override void OnNetworkSpawn()
    {
        Active.Add(this);
        pose.OnValueChanged += OnPoseChanged;
        if (IsServer)
        {
            LockerHiding.ServerOccupantGasped += OnOccupantGasped;
            WorldNoiseSystem.UrgentNoiseEmitted += OnUrgentNoise;
        }
    }

    public override void OnNetworkDespawn()
    {
        Active.Remove(this);
        pose.OnValueChanged -= OnPoseChanged;
        LockerHiding.ServerOccupantGasped -= OnOccupantGasped;
        WorldNoiseSystem.UrgentNoiseEmitted -= OnUrgentNoise;
        if (IsServer) StopHunt(null);
    }

    // ───────────────────────────── Server ─────────────────────────────

    private void Update()
    {
        if (!IsSpawned || !IsServer || monster == null) return;
        TrackLockerEntries();

        if (Stage == LockerHuntStage.None)
        {
            TryStartHunt();
            return;
        }

        if (!monster.IsScriptedControlActive || target == null || !target.IsSpawned)
        {
            StopHunt(null);
            return;
        }

        NetworkObject occupant = target.Occupant;
        if (!target.IsConcealing && Stage != LockerHuntStage.ForceOpen)
        {
            // The player climbed out in front of the monster: chase immediately.
            StopHunt(occupant != null && IsAlive(occupant) ? occupant.transform : null);
            return;
        }

        if (Stage != LockerHuntStage.ForceOpen && TryFindDistraction(out Transform distraction))
        {
            lockerReadyAt[target] = Time.time + lockerCooldown;
            StopHunt(distraction);
            return;
        }

        switch (Stage)
        {
            case LockerHuntStage.Approach: TickApproach(); break;
            case LockerHuntStage.Lurk: TickLurk(); break;
            case LockerHuntStage.ForceOpen: TickForceOpen(occupant); break;
        }
    }

    private void TrackLockerEntries()
    {
        foreach (LockerHiding locker in LockerHiding.All)
        {
            if (locker == null || !locker.IsSpawned) continue;
            observedPhases.TryGetValue(locker, out LockerPhase previous);
            LockerPhase current = locker.Phase;
            if (current == previous) continue;
            observedPhases[locker] = current;

            if (current == LockerPhase.Entering && WasEntryWitnessed(locker))
                witnessedLockers.Add(locker);
            else if (current == LockerPhase.Empty)
                witnessedLockers.Remove(locker);
        }
    }

    private bool WasEntryWitnessed(LockerHiding locker)
    {
        NetworkObject occupant = locker.Occupant;
        if (occupant == null || monster.IsStoryDebugFrozen) return false;

        Transform chased = monster.CurrentTarget;
        bool chasingThisPlayer = chased != null && chased.IsChildOf(occupant.transform) || chased == occupant.transform;
        bool activelyChasing = monster.CurrentState == MonsterAI.MonsterState.Chase ||
                               monster.CurrentState == MonsterAI.MonsterState.Attack;
        if (activelyChasing && chasingThisPlayer &&
            Vector3.Distance(transform.position, occupant.transform.position) <= witnessRange * 1.5f)
            return true;

        return MonsterPerceptionUtility.CanSeeTarget(
            transform, occupant.transform, witnessRange, monster.fieldOfView, monster.obstacleMask, 1f, 1f);
    }

    private void TryStartHunt()
    {
        if (Time.time < globalReadyAt || monster.IsStoryDebugFrozen || !monster.IsReadyForScriptedControl) return;
        if (IsChasingVisiblePlayer()) return;

        LockerHiding best = null;
        bool bestWitnessed = false;
        float bestDistance = float.MaxValue;
        foreach (LockerHiding locker in LockerHiding.All)
        {
            if (locker == null || !locker.IsSpawned || !locker.IsConcealing || !IsLockerReady(locker)) continue;
            bool witnessed = witnessedLockers.Contains(locker);
            float distance = Vector3.Distance(transform.position, locker.MonsterStandPoint);
            if (!witnessed && distance > sniffRadius) continue;
            if (witnessed && !bestWitnessed || (witnessed == bestWitnessed && distance < bestDistance))
            {
                best = locker; bestWitnessed = witnessed; bestDistance = distance;
            }
        }

        if (best != null)
            BeginHunt(best, bestWitnessed, bestWitnessed ? witnessedStartSuspicion : sniffStartSuspicion);
    }

    private void BeginHunt(LockerHiding locker, bool witnessed, float startSuspicion)
    {
        if (!monster.TryBeginScriptedControl(this)) return;
        target = locker;
        targetWitnessed = witnessed;
        targetLockerId.Value = locker.NetworkObjectId;
        suspicion.Value = Mathf.Clamp(startSuspicion, 0f, 99f);
        cueCount = 0;
        EnterStage(LockerHuntStage.Approach);

        NavMeshAgent agent = monster.Agent;
        Vector3 destination = locker.MonsterStandPoint;
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            destination = hit.position;
        agent.SetDestination(destination);
        bool far = Vector3.Distance(transform.position, destination) > slowDownDistance;
        SetPose(witnessed && far ? MonsterAI.ScriptedPose.Run : MonsterAI.ScriptedPose.Walk,
            witnessed && far ? monster.runSpeed : monster.walkSpeed * creepSpeedMultiplier);
    }

    private void TickApproach()
    {
        NavMeshAgent agent = monster.Agent;
        Vector3 stand = target.MonsterStandPoint;
        Vector3 flat = stand - transform.position; flat.y = 0f;
        float distance = flat.magnitude;

        if (distance <= slowDownDistance && pose.Value == MonsterAI.ScriptedPose.Run)
            SetPose(MonsterAI.ScriptedPose.Walk, monster.walkSpeed * creepSpeedMultiplier);

        bool arrived = distance <= arrivalDistance ||
                       (!agent.pathPending && agent.hasPath && agent.remainingDistance <= arrivalDistance);
        if (arrived || (!agent.pathPending && !agent.hasPath && distance <= arrivalDistance * 3f))
        {
            agent.ResetPath();
            SetPose(MonsterAI.ScriptedPose.Idle, 0f);
            lurkDuration = UnityEngine.Random.Range(
                targetWitnessed ? witnessedLurkSeconds.x : sniffLurkSeconds.x,
                targetWitnessed ? witnessedLurkSeconds.y : sniffLurkSeconds.y);
            nextCueAt = Time.time + 0.4f;
            EnterStage(LockerHuntStage.Lurk);
            return;
        }

        if (Time.time - stageStarted > approachTimeout)
            StopHunt(null);
    }

    private void TickLurk()
    {
        monster.FaceScripted(this, target.DoorCenter);

        bool inEarshot = Vector3.Distance(transform.position, target.DoorCenter) <= breathHearDistance;
        float delta = target.OccupantHoldingBreath
            ? -holdingDecayPerSecond
            : inEarshot ? breathingPerSecond : 0f;
        suspicion.Value = Mathf.Clamp(suspicion.Value + delta * Time.deltaTime, 0f, 100f);

        if (suspicion.Value >= 100f)
        {
            BeginForceOpen();
            return;
        }

        if (Time.time >= nextCueAt)
        {
            PlayLurkCue();
            nextCueAt = Time.time + UnityEngine.Random.Range(cueInterval.x, cueInterval.y);
        }

        if (Time.time - stageStarted >= lurkDuration)
        {
            CueClientRpc(Cue.Leave);
            if (target != null) lockerReadyAt[target] = Time.time + lockerCooldown;
            StopHunt(null);
        }
    }

    private void PlayLurkCue()
    {
        cueCount++;
        bool breathing = !target.OccupantHoldingBreath;
        Cue cue;
        if (cueCount == 1) cue = Cue.Sniff;
        else if (cueCount == 2 && targetWitnessed) cue = Cue.WhisperThere;
        else if (breathing && suspicion.Value > 60f && UnityEngine.Random.value < 0.6f) cue = Cue.WhisperBreath;
        else if (Time.time - stageStarted > lurkDuration * 0.6f && UnityEngine.Random.value < 0.35f) cue = Cue.WhisperComeOut;
        else cue = UnityEngine.Random.value < 0.5f ? Cue.Growl : UnityEngine.Random.value < 0.5f ? Cue.Breath : Cue.Sniff;
        CueClientRpc(cue);

        if (suspicion.Value >= knockSuspicion && UnityEngine.Random.value < knockChance)
            KnockClientRpc(target.NetworkObjectId);
    }

    private void BeginForceOpen()
    {
        SetPose(MonsterAI.ScriptedPose.Attack, 0f);
        CueClientRpc(Cue.ForceOpen);
        EnterStage(LockerHuntStage.ForceOpen);
    }

    private void TickForceOpen(NetworkObject occupant)
    {
        monster.FaceScripted(this, target.DoorCenter);
        float elapsed = Time.time - stageStarted;
        if (elapsed >= forceOpenWindup && target.IsConcealing)
            target.TryForceOpenServer();
        if (elapsed >= forceOpenWindup + forceOpenHold)
        {
            lockerReadyAt[target] = Time.time + lockerCooldown;
            StopHunt(occupant != null && IsAlive(occupant) ? occupant.transform : null);
        }
    }

    private void OnOccupantGasped(LockerHiding locker, NetworkObject occupant)
    {
        if (locker == null) return;
        float distance = Vector3.Distance(transform.position, locker.DoorCenter);
        if (Stage != LockerHuntStage.None && target == locker)
        {
            suspicion.Value = Mathf.Min(100f, suspicion.Value + gaspSuspicion);
            return;
        }
        if (Stage == LockerHuntStage.None && distance <= gaspHearRadius && !monster.IsStoryDebugFrozen &&
            monster.IsReadyForScriptedControl)
        {
            witnessedLockers.Add(locker);
            BeginHunt(locker, true, gaspStartSuspicion);
        }
    }

    private void OnUrgentNoise(Vector3 position, float radius)
    {
        if (!IsServer || Stage == LockerHuntStage.None || Stage == LockerHuntStage.ForceOpen) return;
        if (Vector3.Distance(transform.position, position) > Mathf.Min(radius, distractionNoiseRange)) return;
        if (target != null) lockerReadyAt[target] = Time.time + lockerCooldown;
        StopHunt(null);
        monster.ForceInvestigatePosition(position);
    }

    private bool TryFindDistraction(out Transform player)
    {
        player = null;
        if (distractionSightRange <= 0f) return false;
        NetworkObject occupant = target != null ? target.Occupant : null;
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            NetworkObject candidate = client.PlayerObject;
            if (candidate == null || candidate == occupant || !IsAlive(candidate) ||
                LockerHiding.IsPlayerConcealed(candidate))
                continue;
            if (MonsterPerceptionUtility.CanSeeTarget(transform, candidate.transform, distractionSightRange,
                    monster.fieldOfView, monster.obstacleMask, 1f, 1f))
            {
                player = candidate.transform;
                return true;
            }
        }
        return false;
    }

    private void StopHunt(Transform engage)
    {
        bool wasHunting = Stage != LockerHuntStage.None;
        target = null;
        targetLockerId.Value = 0;
        suspicion.Value = 0f;
        stage.Value = LockerHuntStage.None;
        if (wasHunting)
        {
            globalReadyAt = Time.time + globalCooldown;
            SetPose(engage != null ? MonsterAI.ScriptedPose.Run : MonsterAI.ScriptedPose.Walk, monster.walkSpeed);
            monster.EndScriptedControl(this, engage);
        }
    }

    private void EnterStage(LockerHuntStage next)
    {
        stage.Value = next;
        stageStarted = Time.time;
    }

    private void SetPose(MonsterAI.ScriptedPose next, float speed)
    {
        pose.Value = next;
        monster.SetScriptedPose(this, next, speed);
    }

    // A chase after someone who is still out in the open always wins over checking lockers.
    private bool IsChasingVisiblePlayer()
    {
        bool chasing = monster.CurrentState == MonsterAI.MonsterState.Chase ||
                       monster.CurrentState == MonsterAI.MonsterState.Attack;
        Transform chased = monster.CurrentTarget;
        NetworkObject chasedObject = chased != null ? chased.GetComponentInParent<NetworkObject>() : null;
        return chasing && chasedObject != null && !LockerHiding.IsPlayerConcealed(chasedObject);
    }

    private bool IsLockerReady(LockerHiding locker) =>
        !lockerReadyAt.TryGetValue(locker, out float readyAt) || Time.time >= readyAt;

    private static bool IsAlive(NetworkObject player) =>
        player != null && (!player.TryGetComponent(out PlayerStats stats) || !stats.IsDead);

    // ───────────────────────────── Clients ─────────────────────────────

    // The TV monster has no NetworkAnimator, so the scripted pose is mirrored here.
    private void OnPoseChanged(MonsterAI.ScriptedPose previous, MonsterAI.ScriptedPose next)
    {
        if (IsServer) return;
        clientAnimator ??= GetComponent<Animator>();
        if (clientAnimator == null) return;
        foreach (string parameter in new[] { "isIdle", "isWalking", "isRunning", "isAttack", "isMissing" })
            clientAnimator.SetBool(parameter, false);
        clientAnimator.SetBool(next switch
        {
            MonsterAI.ScriptedPose.Walk => "isWalking",
            MonsterAI.ScriptedPose.Run => "isRunning",
            MonsterAI.ScriptedPose.Attack => "isAttack",
            _ => "isIdle"
        }, true);
    }

    [ClientRpc]
    private void CueClientRpc(Cue cue)
    {
        switch (cue)
        {
            case Cue.Sniff: PlayVoice(Pick(sniffClips)); break;
            case Cue.Growl: PlayVoice(Pick(growlClips)); PlayStatic(0.6f); break;
            case Cue.Breath: PlayVoice(Pick(breathClips)); break;
            case Cue.WhisperThere: PlayVoice(whisperThereClip); PlayStatic(0.4f); break;
            case Cue.WhisperBreath: PlayVoice(whisperBreathClip); PlayStatic(0.4f); break;
            case Cue.WhisperComeOut: PlayVoice(whisperComeOutClip); PlayStatic(0.4f); break;
            case Cue.Leave: PlayVoice(Pick(leaveClips)); break;
            case Cue.ForceOpen:
                PlayVoice(shriekClip);
                PlayVoice(foundWhisperClip, 0.9f);
                PlayStatic(1f);
                break;
        }
    }

    [ClientRpc]
    private void KnockClientRpc(ulong lockerId)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(lockerId, out NetworkObject lockerObject) &&
            lockerObject.TryGetComponent(out LockerHiding locker))
            locker.PlayKnockLocal();
    }

    private void PlayVoice(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        EnsureVoice();
        voice.PlayOneShot(clip, voiceVolume * volumeScale);
    }

    private void PlayStatic(float volumeScale)
    {
        AudioClip clip = Pick(staticClips);
        if (clip == null) return;
        EnsureVoice();
        voice.PlayOneShot(clip, staticVolume * volumeScale);
    }

    private void EnsureVoice()
    {
        if (voice != null) return;
        var voiceObject = new GameObject("Locker Hunt Voice");
        voiceObject.transform.SetParent(transform, false);
        voiceObject.transform.localPosition = Vector3.up * 1.7f;
        voice = voiceObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 1f;
        voice.rolloffMode = AudioRolloffMode.Linear;
        voice.minDistance = 1.5f;
        voice.maxDistance = audioMaxDistance;
        voice.dopplerLevel = 0f;
    }

    private static AudioClip Pick(AudioClip[] clips) =>
        clips != null && clips.Length > 0 ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;
}
