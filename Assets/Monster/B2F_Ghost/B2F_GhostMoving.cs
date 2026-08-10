using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkTransform))]
public class B2F_GhostMoving : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float stoppingDistance = 0.5f;

    [Header("Target Re-check (optional)")]
    [SerializeField] private bool reevaluateTargetPeriodically = false;
    [SerializeField] private float targetCheckInterval = 3f;

    private Transform targetPlayer;
    private float targetCheckTimer;

    public override void OnNetworkSpawn()
    {
        // 이동 로직은 서버에서만 계산 (클라이언트는 NetworkTransform으로 위치만 받음)
        if (!IsServer)
        {
            enabled = false;
            return;
        }

        FindNearestPlayerAtSpawn();
    }

    private void FindNearestPlayerAtSpawn()
    {
        float minDist = Mathf.Infinity;
        Transform nearest = null;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            NetworkObject playerObj = client.PlayerObject;
            if (playerObj == null) continue;

            float dist = Vector3.Distance(transform.position, playerObj.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = playerObj.transform;
            }
        }

        targetPlayer = nearest;

        if (targetPlayer == null)
        {
            Debug.LogWarning("[GhostSmokeMover] 스폰 시점에 유효한 플레이어를 찾지 못했습니다.");
        }
    }

    private void Update()
    {
        if (!IsServer || targetPlayer == null) return;

        // 선택: 주기적으로 타겟 재탐색이 필요하면 사용 (기본은 스폰 시 고정 타겟 유지)
        if (reevaluateTargetPeriodically)
        {
            targetCheckTimer += Time.deltaTime;
            if (targetCheckTimer >= targetCheckInterval)
            {
                targetCheckTimer = 0f;
                FindNearestPlayerAtSpawn();
            }
        }

        MoveTowardsTarget();
    }

    private void MoveTowardsTarget()
    {
        Vector3 toTarget = targetPlayer.position - transform.position;
        toTarget.y = 0f; // 수평 이동만 (연기라 부유 높이는 별도 처리 가능)

        float distance = toTarget.magnitude;
        if (distance <= stoppingDistance) return;

        Vector3 dir = toTarget.normalized;
        transform.position += dir * moveSpeed * Time.deltaTime;

        if (dir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                Time.deltaTime * 3f
            );
        }
    }
}