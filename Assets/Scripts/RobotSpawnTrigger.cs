using UnityEngine;

public class RobotSpawnTrigger : MonoBehaviour
{
    [Header("퀘스트 연동 (필요 없다면 -1 지정)")]


    [Header("스폰할 프리팹")]
    public GameObject robotPrefab; // 인스펙터에서 Lobby patrol robot 프리팹을 드래그앤드롭

    [Header("스폰 위치 배열 (3곳)")]
    public Transform[] spawnPoints = new Transform[3]; // 로봇이 생성될 위치 3곳의 Transform

    [Header("충돌 체크 태그")]
    public string playerTag = "Player";

    private bool isTriggered = false; // 중복 스폰 방지용 플래그

    private void OnTriggerEnter(Collider other)
    {
        // 1. 플레이어가 부딪혔는지 확인
        if (!other.CompareTag(playerTag)) return;

        // 2. 중복 실행 방지
        if (isTriggered) return;

        // 모든 조건 충족 시 작동
        isTriggered = true;
        SpawnRobots();

        // 트리거 오브젝트 비활성화 (혹은 Destroy(gameObject)로 아예 삭제 가능)
        gameObject.SetActive(false);
    }

    private void SpawnRobots()
    {
        if (robotPrefab == null)
        {
            Debug.LogError("Robot Spawn Trigger: 로봇 프리팹이 지정되지 않았습니다!");
            return;
        }

        // 지정된 스폰 포인트 배열을 돌며 로봇 생성
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] != null)
            {
                // 프리팹 생성 (위치와 회전값 적용)
                GameObject spawnedRobot = Instantiate(robotPrefab, spawnPoints[i].position, spawnPoints[i].rotation);
                spawnedRobot.name = $"Lobby_Patrol_Robot_{i + 1}";
                
                Debug.Log($"{spawnPoints[i].name} 위치에 로봇 세 마리 중 {i + 1}번째 스폰 완료");
            }
            else
            {
                // 만약 스폰 포인트를 덜 지정했다면 트리거 위치 주변에 대충 스폰시킴
                Vector3 randomOffset = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                Instantiate(robotPrefab, transform.position + randomOffset, Quaternion.identity);
                Debug.LogWarning($"스폰 포인트 {i}번이 비어있어 트리거 근처에 무작위 스폰했습니다.");
            }
        }
    }
}