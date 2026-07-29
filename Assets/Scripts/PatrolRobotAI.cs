using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public class PatrolRobotAI : MonoBehaviour
{
    public enum State { Patrol, Inspect, Alert, Chase }
    public State currentState = State.Patrol;
    public event System.Action<State> StateChanged;

    [Header("Vision Settings")]
    public Transform player;
    public float viewDistance = 15f; 
    public float viewAngle = 120f;   
    public Transform eyeLocation;    
    public LayerMask obstacleMask;   

    [Header("Chase Settings")]
    [SerializeField] private float patrolSpeed = 3.5f;
    [SerializeField] private float chaseSpeed = 5.5f;
    [SerializeField] private float lostSightGraceTime = 3f;

    [Header("Random Patrol Settings")]
    public float patrolRadius = 20f;       // 濡쒕큸????踰덉뿉 ?먯깋??理쒕? 諛섍꼍 (?덈Т ?щ㈃ 硫由?媛?
    public float minPatrolDistance = 7f;   // 理쒖냼 ?대룞 嫄곕━ (?쒖옄由ш구??諛⑹??? ?곷떦??癒?怨?

    [Header("Vision Settings")]
    public Light visionLight; // ??蹂?섎? 異붽?! (?몄뒪?숉꽣?먯꽌 ?꾧퉴 留뚮뱺 Spot Light瑜??뚯뼱???ｌ쑝?몄슂)
    
    private NavMeshAgent agent;
    private bool isInspecting = false;
    private Vector3 lastKnownPlayerPos;
    private float lastSeenPlayerTime = float.NegativeInfinity;
    private float nextPlayerSearchTime;
    private GameObject[] playerCandidates = System.Array.Empty<GameObject>();
    private const float PlayerSearchInterval = 0.5f;

    void Start()
    {
        SyncVisionLight();
        agent = GetComponent<NavMeshAgent>();
        agent.speed = patrolSpeed;
        RefreshPlayerCandidates();
        SetRandomPatrolDestination(); // ?쒖옉?섏옄留덉옄 ?쒕뜡??怨녹쑝濡?異쒕컻
    }

    private void OnValidate()
    {
        if (visionLight == null) return;
        SyncVisionLight();
    }

    private void SyncVisionLight()
    {
        visionLight.range = viewDistance;
        visionLight.spotAngle = viewAngle;
    }

    private void SetState(State nextState)
    {
        if (currentState == nextState) return;
        currentState = nextState;
        StateChanged?.Invoke(currentState);
    }

    void Update()
    {
        if (Time.time >= nextPlayerSearchTime)
            RefreshPlayerCandidates();

        if (!agent.isOnNavMesh || !agent.enabled) 
            return;
        // 1?④퀎: ?뚮젅?댁뼱 媛먯? (??긽 理쒖떊 寃곌낵 ?좎?)
        bool seesPlayer = TryFindVisiblePlayer(out Transform visiblePlayer);
        if (seesPlayer)
        {
            player = visiblePlayer;
            lastSeenPlayerTime = Time.time;
            lastKnownPlayerPos = player.position;
        }

        // 2?④퀎: 媛뺣젰???곹깭 ?꾪솚 ?쇰━ (detection 寃곌낵???곕씪 '利됱떆' ?꾪솚)
        // [洹쒖튃 1] ?뚮젅?댁뼱瑜?諛쒓껄?섎㈃, '?대뼡 ?곹깭?? 利됱떆 異붽꺽(鍮④컙遺??쇰줈 ?꾪솚?⑸땲??
        if (seesPlayer && currentState != State.Chase)
        {
            StartChase();
            return; // ?꾪솚 ?꾨즺, ?됰룞? ?ㅼ쓬 ?꾨젅?꾨????ㅽ뻾
        }
        // [洹쒖튃 2] ?뚮젅?댁뼱瑜?異붽꺽 以묒씠?ㅺ? ?볦튂硫? 利됱떆 寃쎄퀎(二쇳솴遺?濡??꾪솚?⑸땲??
        else if (!seesPlayer && currentState == State.Chase
            && Time.time - lastSeenPlayerTime >= lostSightGraceTime)
        {
            StopChase();
            return; // ?꾪솚 ?꾨즺, ?됰룞? ?ㅼ쓬 ?꾨젅?꾨????ㅽ뻾
        }

        // 3?④퀎: ?곹깭蹂?怨좎쑀 ?됰룞 (?꾪솚 ?쇰━???ш린 ?ы븿 ????
        switch (currentState)
        {
            case State.Patrol:
                visionLight.color = Color.yellow; // ?몃?遺?
                agent.isStopped = false; // ?대룞 蹂댁옣
                
                // ?쒖같 吏???꾩갑 ???먮━踰덇굅由ш린
                if (!agent.pathPending && agent.remainingDistance < 0.5f && !isInspecting)
                {
                    StartCoroutine(InspectRoutine()); 
                }
                break;

            case State.Inspect:
                visionLight.color = new Color(1f, 0.5f, 0f); // 二쇳솴遺?
                // 肄붾（???됰룞? InspectRoutine() ?대??먯꽌 泥섎━ (?꾪솚? 2?④퀎?먯꽌 ?대? ?섑뻾??
                break;

            case State.Alert:
                visionLight.color = new Color(1f, 0.5f, 0f); // 二쇳솴遺?
                agent.isStopped = false; // ?대룞 蹂댁옣

                // ?섏깋 ?ㅽ뙣 ???ㅼ떆 ?쒖같 蹂듦? (?대룞 ?쇰━??StopChase()??援ы쁽)
                if (!agent.pathPending && agent.remainingDistance < 1f)
                {
                    Debug.Log("?뚮젅?댁뼱 ?섏깋 ?ㅽ뙣. ?ㅼ떆 ?쒖같 紐⑤뱶濡?蹂듦??⑸땲??");
                    SetState(State.Patrol);
                    agent.speed = patrolSpeed;
                    SetRandomPatrolDestination(); // ?덈줈???쒖같 吏?먯쑝濡??대룞
                }
                break;

            case State.Chase:
                visionLight.color = Color.red; // 鍮④컙遺?
                agent.isStopped = false; // ?대룞 蹂댁옣

                // ?쒖빞瑜??좉퉸 踰쀬뼱?섎룄 留덉?留?紐⑷꺽 ?꾩튂源뚯? ?뺣컯?⑸땲??
                agent.SetDestination(seesPlayer ? player.position : lastKnownPlayerPos);
                break;
        }
    }

    // NavMesh ?꾩뿉??臾댁옉??紐⑹쟻吏瑜?李얜뒗 ?듭떖 濡쒖쭅
    // NavMesh ?꾩뿉??臾댁옉??紐⑹쟻吏瑜?李얜뒗 ?듭떖 濡쒖쭅 (?섏젙蹂?
    void SetRandomPatrolDestination()
    {
        bool foundPoint = false;

        for (int i = 0; i < 30; i++)
        {
            // ?섏젙 1: 怨듭쨷???꾨땶 諛붾떏 ?됰㈃(XZ異? 湲곗??쇰줈留??쒕뜡 醫뚰몴瑜??앹꽦?⑸땲??
            Vector2 randomCircle = Random.insideUnitCircle * patrolRadius;
            Vector3 randomPos = transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

            // ?섏젙 2: 理쒖냼 ?대룞 嫄곕━蹂대떎 媛源뚯슦硫?臾댁떆?섍퀬 ?ㅼ떆 戮묒뒿?덈떎.
            if (Vector3.Distance(transform.position, randomPos) < minPatrolDistance) continue;

            // ?섏젙 3: 醫뚰몴 洹쇱쿂???뚮???諛붾떏(NavMesh)??李얜뒗 ?먯깋 踰붿쐞瑜?5.0f濡??됰꼮?섍쾶 ?섎┰?덈떎.
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPos, out hit, 5.0f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                foundPoint = true;
                break; // 紐⑹쟻吏瑜?李얠븯?쇰땲 諛섎났臾??덉텧!
            }
        }

        // 臾댄븳 ?먮━踰덇굅由?諛⑹????덉쟾?μ튂
        // 留뚯빟 援ъ꽍??媛뉙엳嫄곕굹 留듭씠 醫곸븘??30踰??쒕룄?덈뒗?곕룄 ?곸젅??怨녹쓣 紐?李얠븯?ㅻ㈃,
        // ?쇰떒 濡쒕큸??諛붾씪蹂대뒗 ?뺣㈃ ?욎そ 3m 吏?먯쑝濡?媛뺤젣濡?嫄몄뼱媛寃?留뚮벊?덈떎.
        if (!foundPoint)
        {
            agent.SetDestination(transform.position + transform.forward * 3f);
        }
    }

    // ?쒖빞 媛먯? 濡쒖쭅
    // ?섏젙???쒓컖 媛먯? 濡쒖쭅 (?붿슧 ?뺣??섍퀬 踰꾧렇 ?녿뒗 踰꾩쟾)
    // ?섏젙???쒓컖 媛먯? 濡쒖쭅 (?붾쾭洹?紐⑤뱶 + ?뺣? ?寃?
    private bool TryFindVisiblePlayer(out Transform visiblePlayer)
    {
        visiblePlayer = null;
        float closestSqrDistance = float.MaxValue;

        foreach (GameObject candidate in playerCandidates)
        {
            if (candidate == null || !candidate.activeInHierarchy) continue;
            if (candidate.transform.root == transform.root) continue;
            if (!CanSeePlayer(candidate.transform)) continue;

            float sqrDistance = (candidate.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance >= closestSqrDistance) continue;

            closestSqrDistance = sqrDistance;
            visiblePlayer = candidate.transform;
        }

        return visiblePlayer != null;
    }

    private bool CanSeePlayer(Transform targetPlayer)
    {
        CharacterController playerController = targetPlayer.GetComponent<CharacterController>();
        Bounds playerBounds = playerController.bounds;
        Transform coneTransform = visionLight.transform;
        Vector3 coneOrigin = coneTransform.position;
        Vector3 coneForward = coneTransform.forward;

        float centerProjection = Vector3.Dot(playerBounds.center - coneOrigin, coneForward);
        Vector3 closestAxisPoint = coneOrigin
            + coneForward * Mathf.Clamp(centerProjection, 0f, visionLight.range);
        Vector3 closestControllerPoint = playerController.ClosestPoint(closestAxisPoint);
        float lowerSampleHeight = Mathf.Min(playerController.radius * 0.5f, playerBounds.extents.y);
        Vector3 lowerControllerPoint = new Vector3(
            playerBounds.center.x,
            playerBounds.min.y + lowerSampleHeight,
            playerBounds.center.z);

        return CanSeePoint(closestControllerPoint, coneOrigin, coneForward)
            || CanSeePoint(lowerControllerPoint, coneOrigin, coneForward)
            || CanSeePoint(playerBounds.center, coneOrigin, coneForward);
    }

    private bool CanSeePoint(Vector3 targetPos, Vector3 coneOrigin, Vector3 coneForward)
    {
        Vector3 direction = targetPos - coneOrigin;
        float distance = direction.magnitude;

        if (distance > visionLight.range || distance <= Mathf.Epsilon) return false;

        direction /= distance;
        if (Vector3.Angle(coneForward, direction) >= visionLight.spotAngle * 0.5f) return false;

        RaycastHit[] hits = Physics.RaycastAll(
            coneOrigin,
            direction,
            visionLight.range,
            obstacleMask,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.root == transform.root) continue;
            return HasPlayerTag(hit.collider.transform);
        }

        return false;
    }

    private void RefreshPlayerCandidates()
    {
        nextPlayerSearchTime = Time.time + PlayerSearchInterval;
        playerCandidates = GameObject.FindGameObjectsWithTag("Player");
    }

    private static bool HasPlayerTag(Transform target)
    {
        while (target != null)
        {
            if (target.CompareTag("Player")) return true;
            target = target.parent;
        }
        return false;
    }

    // Kept temporarily for comparison while the new self-filtering vision is tested.
    bool LegacyCanSeePlayer()
    {
        Vector3 targetPos = player.position + Vector3.up * 1.0f; // 媛???믪씠 議곗?
        Vector3 dirToPlayer = (targetPos - eyeLocation.position).normalized;
        float angleToPlayer = Vector3.Angle(transform.forward, dirToPlayer);

        if (angleToPlayer < viewAngle / 2f) 
        {
            float distToPlayer = Vector3.Distance(eyeLocation.position, targetPos);
            if (distToPlayer <= viewDistance)
            {
                // [?묒뒪?덉씠] ??Scene) ?붾㈃?먯꽌 濡쒕큸???덉뿉???섍????덉씠?瑜?珥덈줉???좎쑝濡?蹂댁뿬以띾땲??
                Debug.DrawRay(eyeLocation.position, dirToPlayer * distToPlayer, Color.green);

                RaycastHit hit;
                if (Physics.Raycast(eyeLocation.position, dirToPlayer, out hit, distToPlayer))
                {
                    // [?닿껐 1] 留뚯빟 ?덉씠?媛 濡쒕큸 ?먭린 ?먯떊??紐명넻???뚮졇?ㅻ㈃? -> 臾댁떆!
                    if (hit.collider.transform.root == this.transform) return false;

                    // [異붿쟻湲? ?꾨?泥??덉씠?媛 '臾댁뾿'??遺?ろ삍?붿? 肄섏넄???대쫫???꾩썎?덈떎.
                    Debug.Log("濡쒕큸 ?쒖빞??嫄몃┛ 臾쇱껜: " + hit.collider.name + " (?쒓렇: " + hit.collider.tag + ")");

                    // [?닿껐 2] ?뚮젅?댁뼱 蹂몄껜肉먮쭔 ?꾨땲?? ?먯떇 肄쒕씪?대뜑(?? ?ㅻ━, 媛諛?????留욎븘???몄떇?섍쾶 留뚮벊?덈떎.
                    if (hit.collider.CompareTag("Player") || hit.transform.root.CompareTag("Player"))
                    {
                        return true; 
                    }
                }
            }
        }
        return false;
    }

    // ?먮━踰덇굅由щ뒗 肄붾（??
    IEnumerator InspectRoutine()
    {
        SetState(State.Inspect);
        isInspecting = true;
        agent.isStopped = true; 

        Quaternion startRot = transform.rotation;
        yield return new WaitForSeconds(1f); 

        yield return StartCoroutine(SmoothRotate(startRot * Quaternion.Euler(0, -130, 0), 1.5f));
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(SmoothRotate(startRot, 1f));
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(SmoothRotate(startRot * Quaternion.Euler(0, 130, 0), 1.5f));
        yield return new WaitForSeconds(0.5f);

        yield return StartCoroutine(SmoothRotate(startRot, 1f));
        
        agent.isStopped = false;
        isInspecting = false;
        SetState(State.Patrol);
        
        SetRandomPatrolDestination(); // ?먮━踰덇굅由ш린 ?앸궃 ???덈줈??臾댁옉??紐⑹쟻吏濡?
    }

    IEnumerator SmoothRotate(Quaternion targetRot, float duration)
    {
        float time = 0;
        Quaternion start = transform.rotation;
        while (time < duration)
        {
            transform.rotation = Quaternion.Slerp(start, targetRot, time / duration);
            time += Time.deltaTime;
            yield return null;
        }
        transform.rotation = targetRot;
    }

    void StartChase()
    {
        // 1. 肄붾（?댁씠 異⑸룎?섏? ?딅룄濡?以묒? (媛??以묒슂)
        if(isInspecting) { StopAllCoroutines(); isInspecting = false; }
        
        SetState(State.Chase); // 異붽꺽 紐⑤뱶(鍮④컙遺?濡??꾪솚
        agent.speed = chaseSpeed;
        Debug.Log("?먮퉭! ?뚮젅?댁뼱 諛쒓껄! 異붽꺽?⑸땲??");
        agent.isStopped = false; // ?대룞 ?ш컻
    }
    
    public void ReceiveCCTVReport(Vector3 targetLocation)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        StopAllCoroutines();
        isInspecting = false;
        agent.isStopped = false;
        
        SetState(State.Alert);
        agent.speed = chaseSpeed;
        lastKnownPlayerPos = targetLocation;
        agent.SetDestination(targetLocation);
        Debug.Log("CCTV 蹂닿퀬 ?묒닔! ?대떦 ?꾩튂濡??대룞?⑸땲??");
    }

    private void OnCollisionEnter(Collision collision)
    {
        PlayerStats playerStats = collision.collider.GetComponentInParent<PlayerStats>();
        if (playerStats == null)
            return;

        playerStats.TakeDamage(1);
    }
    // ?섏젙??StopChase ?⑥닔 (?뚮젅?댁뼱瑜??볦낀????
    void StopChase()
    {
        SetState(State.Alert); // 寃쎄퀎 紐⑤뱶(二쇳솴遺?濡??꾪솚
        agent.speed = patrolSpeed;
        Debug.Log("?뚮젅?댁뼱瑜??볦낀?듬땲?? 留덉?留됱쑝濡?蹂??꾩튂瑜??섏깋?⑸땲??");

        // ?덉쟾?μ튂: lastKnownPlayerPos媛 ?좏슚?쒖? ?뺤씤?섍퀬, ?좏슚?섏? ?딅떎硫??쇰떒 ?꾩옱 ?꾩튂瑜??섏깋?⑸땲??
        // (Vector3.zero???댁긽??怨녹쓽 ?꾪삎?곸씤 ?덉떆)
        if (lastKnownPlayerPos == Vector3.zero || Vector3.Distance(transform.position, lastKnownPlayerPos) < 1f)
        {
            // 洹쇱쿂 NavMesh ?꾩쓽 ?쒕뜡??吏?먯쑝濡?紐⑹쟻吏 ?ㅼ젙
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position + transform.forward * 3f, out hit, 5.0f, NavMesh.AllAreas))
            {
                lastKnownPlayerPos = hit.position;
            }
            else
            {
                lastKnownPlayerPos = transform.position; // 洹몃쭏??????섎㈃ ?쒖옄由??섏깋
            }
        }
        
        agent.SetDestination(lastKnownPlayerPos); // ?덉쟾?섍쾶 寃利앸맂 ?꾩튂濡??대룞
    }
}
