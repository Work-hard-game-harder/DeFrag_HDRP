using System.Collections.Generic;
using EasyPeasyFirstPersonController;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Audio;

namespace DeFrag.Player
{
    /// <summary>
    /// 하나의 소유자 마이크 스트림을 일반 근거리 음성과 무전기 음성으로 구분해 중계합니다.
    /// 일반 음성은 음성 감지 중에만 3D로, 무전기는 Push-To-Talk 중에만 2D로 재생합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkWalkieTalkieVoice : NetworkBehaviour
    {
        private enum VoiceMode : byte
        {
            None = 0,
            Proximity = 1,
            WalkieTalkie = 2
        }

        [Header("References")]
        [SerializeField] private StableMicrophoneInput microphoneInput;
        [SerializeField] private WalkieTalkieController walkieTalkieController;
        [SerializeField] private SoundEmitter soundEmitter;

        [Header("Proximity Voice")]
        [SerializeField] private NetworkPcmPlayback proximityPlayback;
        [SerializeField] private AudioMixerGroup proximityOutputMixerGroup;
        [SerializeField, Range(0f, 1f)] private float proximityPlaybackVolume = 1f;
        [SerializeField, Min(0.01f)] private float proximityMinDistance = 1f;
        [SerializeField, Min(0.1f)] private float proximityMaxDistance = 15f;
        [SerializeField, Range(0f, 1f)] private float voiceStartThreshold = 0.035f;
        [SerializeField, Range(0f, 1f)] private float voiceStopThreshold = 0.018f;
        [SerializeField, Min(0f)] private float voiceReleaseDelay = 0.2f;

        [Header("Walkie-Talkie Voice")]
        [SerializeField] private NetworkPcmPlayback walkiePlayback;
        [SerializeField] private AudioMixerGroup walkieOutputMixerGroup;
        [SerializeField, Range(0f, 1f)] private float walkiePlaybackVolume = 1f;

        [Header("Network Audio")]
        [SerializeField, Range(8000, 24000)] private int networkSampleRate = 16000;
        [SerializeField, Range(10, 60)] private int packetDurationMilliseconds = 20;
        [SerializeField, Range(20, 250)] private int jitterBufferMilliseconds = 80;
        [SerializeField, Range(200, 2000)] private int maxBufferedMilliseconds = 500;
        [SerializeField, Range(1, 6)] private int maxPacketsPerFrame = 3;

        [Header("Diagnostics")]
        [SerializeField] private bool logDiagnostics;
        [SerializeField] private int sentPacketCount;
        [SerializeField] private int receivedPacketCount;

        private readonly List<ulong> relayTargets = new(2);
        private int captureReadPosition;
        private int sourceSamplesPerPacket;
        private int outputSamplesPerPacket;
        private int sourceSampleRate;
        private byte[] packetBuffer;
        private bool captureReady;
        private bool proximityVoiceActive;
        private float belowVoiceThresholdSince = -1f;
        private VoiceMode localMode;
        private uint transmissionId;
        private uint packetSequence;
        private VoiceMode activeRemoteMode;
        private uint activeRemoteTransmissionId;
        private uint endedRemoteTransmissionId;
        private uint lastRemoteSequence;
        private bool hasRemoteSequence;
        private bool playbackInitialized;

        private void Awake()
        {
            ResolveReferences();
        }

        public override void OnNetworkSpawn()
        {
            ResolveReferences();
            EnsureRemotePlayback();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
                return;

            ResolveReferences();
            VoiceMode desiredMode = DetermineLocalVoiceMode();
            if (desiredMode != localMode)
            {
                if (localMode != VoiceMode.None)
                    EndLocalTransmission();

                localMode = desiredMode;
                if (localMode != VoiceMode.None)
                    BeginLocalTransmission();
            }

            if (localMode != VoiceMode.None)
                SendAvailablePackets();
        }

        private VoiceMode DetermineLocalVoiceMode()
        {
            if (walkieTalkieController != null && walkieTalkieController.IsTransmitting)
            {
                proximityVoiceActive = false;
                belowVoiceThresholdSince = -1f;
                return VoiceMode.WalkieTalkie;
            }

            return EvaluateProximityVoiceGate() ? VoiceMode.Proximity : VoiceMode.None;
        }

        private bool EvaluateProximityVoiceGate()
        {
            if (soundEmitter == null || !soundEmitter.IsMicActive)
            {
                proximityVoiceActive = false;
                belowVoiceThresholdSince = -1f;
                return false;
            }

            float volume = soundEmitter.CurrentVolume;
            if (!proximityVoiceActive)
            {
                if (volume < voiceStartThreshold)
                    return false;

                proximityVoiceActive = true;
                belowVoiceThresholdSince = -1f;
                return true;
            }

            if (volume >= voiceStopThreshold)
            {
                belowVoiceThresholdSince = -1f;
                return true;
            }

            if (belowVoiceThresholdSince < 0f)
                belowVoiceThresholdSince = Time.unscaledTime;

            if (Time.unscaledTime - belowVoiceThresholdSince < voiceReleaseDelay)
                return true;

            proximityVoiceActive = false;
            belowVoiceThresholdSince = -1f;
            return false;
        }

        private void BeginLocalTransmission()
        {
            transmissionId++;
            if (transmissionId == 0)
                transmissionId = 1;

            packetSequence = 0;
            captureReady = false;
            PrepareCapture();

            if (logDiagnostics)
                Debug.Log($"[NetworkVoice] {localMode} 송신 시작 (Owner {OwnerClientId}).", this);
        }

        private void EndLocalTransmission()
        {
            VoiceMode endedMode = localMode;
            captureReady = false;

            if (IsSpawned)
            {
                if (IsServer)
                    RelayTransmissionEnded(transmissionId, endedMode);
                else
                    EndTransmissionServerRpc(transmissionId, (byte)endedMode);
            }

            if (logDiagnostics)
                Debug.Log($"[NetworkVoice] {endedMode} 송신 종료 (Owner {OwnerClientId}).", this);
        }

        private void PrepareCapture()
        {
            ResolveMicrophoneInput();
            if (microphoneInput == null || microphoneInput.CircularBuffer == null ||
                microphoneInput.BufferLength == 0 || !microphoneInput.IsRecording)
                return;

            sourceSampleRate = Mathf.Max(8000, AudioSettings.outputSampleRate);
            outputSamplesPerPacket = Mathf.Max(
                1,
                networkSampleRate * packetDurationMilliseconds / 1000);
            sourceSamplesPerPacket = Mathf.Max(
                1,
                Mathf.CeilToInt(outputSamplesPerPacket * (float)sourceSampleRate / networkSampleRate));
            packetBuffer = new byte[outputSamplesPerPacket * 2];
            captureReadPosition = microphoneInput.WritePos;
            captureReady = true;
        }

        private void SendAvailablePackets()
        {
            if (!captureReady)
            {
                PrepareCapture();
                if (!captureReady)
                    return;
            }

            float[] sourceBuffer = microphoneInput.CircularBuffer;
            if (sourceBuffer == null || sourceBuffer.Length == 0 || !microphoneInput.IsRecording)
            {
                captureReady = false;
                return;
            }

            int packetsSent = 0;
            while (AvailableSamples(captureReadPosition, microphoneInput.WritePos, sourceBuffer.Length) >=
                   sourceSamplesPerPacket && packetsSent < maxPacketsPerFrame)
            {
                EncodePacket(sourceBuffer);
                SendPacket(packetBuffer, transmissionId, packetSequence++, localMode);
                captureReadPosition = (captureReadPosition + sourceSamplesPerPacket) % sourceBuffer.Length;
                sentPacketCount++;
                packetsSent++;
            }
        }

        private void EncodePacket(float[] sourceBuffer)
        {
            float gain = SettingManager.Instance != null ? SettingManager.Instance.MicGain : 1f;
            float sourceStep = sourceSampleRate / (float)networkSampleRate;

            for (int outputIndex = 0; outputIndex < outputSamplesPerPacket; outputIndex++)
            {
                int sourceOffset = Mathf.Min(
                    Mathf.FloorToInt(outputIndex * sourceStep),
                    sourceSamplesPerPacket - 1);
                int sourceIndex = (captureReadPosition + sourceOffset) % sourceBuffer.Length;
                float limitedSample = (float)System.Math.Tanh(sourceBuffer[sourceIndex] * gain);
                short pcm = (short)Mathf.RoundToInt(Mathf.Clamp(limitedSample, -1f, 1f) * 32767f);

                int byteIndex = outputIndex * 2;
                packetBuffer[byteIndex] = (byte)(pcm & 0xff);
                packetBuffer[byteIndex + 1] = (byte)((pcm >> 8) & 0xff);
            }
        }

        private void SendPacket(byte[] packet, uint currentTransmissionId, uint sequence, VoiceMode mode)
        {
            if (IsServer)
                RelayVoicePacket(packet, currentTransmissionId, sequence, mode);
            else
                SubmitVoicePacketServerRpc(packet, currentTransmissionId, sequence, (byte)mode);
        }

        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void SubmitVoicePacketServerRpc(
            byte[] packet,
            uint currentTransmissionId,
            uint sequence,
            byte modeValue,
            ServerRpcParams rpcParams = default)
        {
            VoiceMode mode = (VoiceMode)modeValue;
            if (rpcParams.Receive.SenderClientId != OwnerClientId ||
                !IsValidPacket(packet) || !IsPlayableMode(mode))
                return;

            RelayVoicePacket(packet, currentTransmissionId, sequence, mode);
        }

        private void RelayVoicePacket(
            byte[] packet,
            uint currentTransmissionId,
            uint sequence,
            VoiceMode mode)
        {
            if (!IsServer || !IsValidPacket(packet) || !IsPlayableMode(mode))
                return;

            ClientRpcParams targets = BuildRelayTargets();
            if (relayTargets.Count == 0)
                return;

            ReceiveVoicePacketClientRpc(
                packet,
                networkSampleRate,
                currentTransmissionId,
                sequence,
                (byte)mode,
                targets);
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void ReceiveVoicePacketClientRpc(
            byte[] packet,
            int sampleRate,
            uint currentTransmissionId,
            uint sequence,
            byte modeValue,
            ClientRpcParams rpcParams = default)
        {
            VoiceMode mode = (VoiceMode)modeValue;
            if (IsOwner || packet == null || packet.Length == 0 ||
                currentTransmissionId <= endedRemoteTransmissionId || !IsPlayableMode(mode))
                return;

            EnsureRemotePlayback();
            NetworkPcmPlayback targetPlayback = GetPlayback(mode);
            if (targetPlayback == null)
                return;

            if (activeRemoteTransmissionId != currentTransmissionId || activeRemoteMode != mode)
            {
                ClearRemotePlayback();
                activeRemoteTransmissionId = currentTransmissionId;
                activeRemoteMode = mode;
                hasRemoteSequence = false;
            }

            if (hasRemoteSequence && sequence <= lastRemoteSequence)
                return;

            hasRemoteSequence = true;
            lastRemoteSequence = sequence;
            receivedPacketCount++;
            targetPlayback.EnqueuePcm16(packet, sampleRate);

            if (logDiagnostics && sequence == 0)
                Debug.Log($"[NetworkVoice] {mode} 첫 패킷 수신 (Owner {OwnerClientId}).", this);
        }

        [ServerRpc]
        private void EndTransmissionServerRpc(
            uint currentTransmissionId,
            byte modeValue,
            ServerRpcParams rpcParams = default)
        {
            VoiceMode mode = (VoiceMode)modeValue;
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !IsPlayableMode(mode))
                return;

            RelayTransmissionEnded(currentTransmissionId, mode);
        }

        private void RelayTransmissionEnded(uint currentTransmissionId, VoiceMode mode)
        {
            if (!IsServer)
                return;

            ClientRpcParams targets = BuildRelayTargets();
            if (relayTargets.Count > 0)
                EndTransmissionClientRpc(currentTransmissionId, (byte)mode, targets);
        }

        [ClientRpc]
        private void EndTransmissionClientRpc(
            uint currentTransmissionId,
            byte modeValue,
            ClientRpcParams rpcParams = default)
        {
            if (IsOwner)
                return;

            if (currentTransmissionId > endedRemoteTransmissionId)
                endedRemoteTransmissionId = currentTransmissionId;

            if (activeRemoteTransmissionId == currentTransmissionId)
            {
                GetPlayback((VoiceMode)modeValue)?.StopAndClear();
                hasRemoteSequence = false;
                activeRemoteMode = VoiceMode.None;
            }
        }

        private ClientRpcParams BuildRelayTargets()
        {
            relayTargets.Clear();
            if (NetworkManager != null)
                foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                    if (clientId != OwnerClientId)
                        relayTargets.Add(clientId);

            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = relayTargets }
            };
        }

        private bool IsValidPacket(byte[] packet)
        {
            int expectedLength = Mathf.Max(
                1,
                networkSampleRate * packetDurationMilliseconds / 1000) * 2;
            return packet != null && packet.Length == expectedLength;
        }

        private static bool IsPlayableMode(VoiceMode mode)
        {
            return mode == VoiceMode.Proximity || mode == VoiceMode.WalkieTalkie;
        }

        private void ResolveReferences()
        {
            if (walkieTalkieController == null)
                walkieTalkieController = GetComponentInChildren<WalkieTalkieController>(true);
            if (soundEmitter == null)
                soundEmitter = GetComponentInChildren<SoundEmitter>(true);
            ResolveMicrophoneInput();
        }

        private void ResolveMicrophoneInput()
        {
            if (microphoneInput != null)
                return;

            SettingManager manager = SettingManager.Instance;
            if (manager != null)
                microphoneInput = manager.MicrophoneInput;
        }

        private void EnsureRemotePlayback()
        {
            if (playbackInitialized)
                return;

            proximityPlayback = ResolveOrCreatePlayback(
                proximityPlayback,
                "Network Proximity Voice Playback");
            walkiePlayback = ResolveOrCreatePlayback(
                walkiePlayback,
                "Network Walkie Voice Playback");

            proximityPlayback.Initialize(
                proximityPlaybackVolume,
                jitterBufferMilliseconds,
                maxBufferedMilliseconds,
                proximityOutputMixerGroup,
                1f,
                proximityMinDistance,
                proximityMaxDistance);
            walkiePlayback.Initialize(
                walkiePlaybackVolume,
                jitterBufferMilliseconds,
                maxBufferedMilliseconds,
                walkieOutputMixerGroup,
                0f,
                1f,
                500f);
            playbackInitialized = true;
        }

        private NetworkPcmPlayback ResolveOrCreatePlayback(
            NetworkPcmPlayback current,
            string objectName)
        {
            if (current != null)
                return current;

            Transform existing = transform.Find(objectName);
            if (existing != null)
            {
                NetworkPcmPlayback found = existing.GetComponent<NetworkPcmPlayback>();
                if (found != null)
                    return found;
            }

            GameObject playbackObject = new(objectName);
            playbackObject.transform.SetParent(transform, false);
            playbackObject.AddComponent<AudioSource>();
            return playbackObject.AddComponent<NetworkPcmPlayback>();
        }

        private NetworkPcmPlayback GetPlayback(VoiceMode mode)
        {
            return mode == VoiceMode.Proximity
                ? proximityPlayback
                : mode == VoiceMode.WalkieTalkie
                    ? walkiePlayback
                    : null;
        }

        private void ClearRemotePlayback()
        {
            proximityPlayback?.StopAndClear();
            walkiePlayback?.StopAndClear();
        }

        private static int AvailableSamples(int from, int to, int length)
        {
            int distance = to - from;
            return distance >= 0 ? distance : distance + length;
        }

        public override void OnNetworkDespawn()
        {
            ClearRemotePlayback();
            localMode = VoiceMode.None;
            proximityVoiceActive = false;
            captureReady = false;
            base.OnNetworkDespawn();
        }

        private void OnValidate()
        {
            networkSampleRate = Mathf.Clamp(networkSampleRate, 8000, 24000);
            packetDurationMilliseconds = Mathf.Clamp(packetDurationMilliseconds, 10, 60);
            jitterBufferMilliseconds = Mathf.Clamp(jitterBufferMilliseconds, 20, 250);
            maxBufferedMilliseconds = Mathf.Max(maxBufferedMilliseconds, jitterBufferMilliseconds * 2);
            proximityPlaybackVolume = Mathf.Clamp01(proximityPlaybackVolume);
            proximityMinDistance = Mathf.Max(0.01f, proximityMinDistance);
            proximityMaxDistance = Mathf.Max(proximityMinDistance, proximityMaxDistance);
            voiceStartThreshold = Mathf.Clamp01(voiceStartThreshold);
            voiceStopThreshold = Mathf.Clamp(voiceStopThreshold, 0f, voiceStartThreshold);
            voiceReleaseDelay = Mathf.Max(0f, voiceReleaseDelay);
            walkiePlaybackVolume = Mathf.Clamp01(walkiePlaybackVolume);
            maxPacketsPerFrame = Mathf.Clamp(maxPacketsPerFrame, 1, 6);
        }
    }
}
