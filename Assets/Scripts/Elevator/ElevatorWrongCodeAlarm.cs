using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class ElevatorWrongCodeAlarm : MonoBehaviour
{
    [Header("Alarm Sound")]
    [SerializeField] private AudioSource alarmSource;
    [SerializeField] private AudioClip alarmClip;
    [Range(0f, 1f)] [SerializeField] private float alarmVolume = 1f;

    [Header("Robot Response")]
    [Tooltip("로봇이 실제로 달려올 NavMesh 위의 위치입니다.")]
    [SerializeField] private Transform responseWaypoint;
    [Min(0.1f)] [SerializeField] private float alertRadius = 45f;
    [Min(0f)] [SerializeField] private float alarmCooldown = 3f;

    [Header("Events")]
    [SerializeField] private UnityEvent onAlarmTriggered;

    private float nextAllowedAlarmTime;

    private void Awake()
    {
        if (alarmSource == null)
            alarmSource = GetComponent<AudioSource>();

        alarmSource.playOnAwake = false;
    }

    public void Trigger(PlayerInteraction sourcePlayer)
    {
        if (Time.unscaledTime < nextAllowedAlarmTime)
            return;

        nextAllowedAlarmTime = Time.unscaledTime + alarmCooldown;
        PlayAlarmSound();
        onAlarmTriggered?.Invoke();

        Vector3 responsePosition = GetResponsePosition();

        CooperativeTerminalHintRelay relay = sourcePlayer != null
            ? sourcePlayer.GetComponentInParent<CooperativeTerminalHintRelay>(true)
            : null;

        // 기존 플레이어 Relay가 있으면 오프라인/호스트/클라이언트 권한 처리를
        // 그대로 위임한다. 클라이언트가 로봇 AI를 직접 조종하지 않게 한다.
        if (relay != null)
        {
            relay.ReportEmergencyAlarm(responsePosition, alertRadius);
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && !manager.IsServer)
        {
            Debug.LogWarning(
                "[ElevatorAlarm] 클라이언트 플레이어에서 CooperativeTerminalHintRelay를 찾지 못해 " +
                "로봇 조사 요청을 전송하지 못했습니다.",
                this);
            return;
        }

        DispatchLocally(responsePosition);
    }

    private Vector3 GetResponsePosition()
    {
        if (responseWaypoint != null)
            return responseWaypoint.position;

        Debug.LogWarning(
            "[ElevatorAlarm] Response Waypoint is not assigned. " +
            "Using the keypad position as a fallback.",
            this);
        return transform.position;
    }

    private void PlayAlarmSound()
    {
        if (alarmSource == null)
        {
            Debug.LogWarning("[ElevatorAlarm] Alarm Source가 연결되지 않았습니다.", this);
            return;
        }

        if (alarmClip != null)
            alarmSource.PlayOneShot(alarmClip, alarmVolume);
        else
            alarmSource.Play();
    }

    private void DispatchLocally(Vector3 alarmPosition)
    {
        PatrolRobotAI[] robots = FindObjectsByType<PatrolRobotAI>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        PatrolRobotAI closestRobot = null;
        float shortestPath = float.PositiveInfinity;
        foreach (PatrolRobotAI robot in robots)
        {
            if (robot == null || !robot.enabled || !robot.gameObject.activeInHierarchy)
                continue;
            if (!robot.TryGetEmergencyPath(alarmPosition, out _, out float pathDistance))
                continue;
            if (pathDistance >= shortestPath)
                continue;

            shortestPath = pathDistance;
            closestRobot = robot;
        }

        closestRobot?.ReceiveEmergencyAlarm(alarmPosition);
        int responders = closestRobot != null ? 1 : 0;

        WorldNoiseSystem.Emit(alarmPosition, alertRadius);
        Debug.Log($"[ElevatorAlarm] 순찰 로봇 {responders}대가 엘리베이터를 조사합니다.", this);
    }

    private void OnDrawGizmosSelected()
    {
        if (responseWaypoint == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(responseWaypoint.position, 0.5f);
        Gizmos.DrawLine(transform.position, responseWaypoint.position);
    }
}
