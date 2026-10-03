using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Emergency-exit zone. The server counts the party members standing inside; once every connected
/// player is in (and the escape sequence allows it), it reports the exit quest signal and loads the
/// destination through NetworkSceneManager. The loading cinematic is chosen per destination by
/// SceneTransitionCinematicLibrary and played locally on each peer while the scene loads.
/// </summary>
[RequireComponent(typeof(NetworkObject), typeof(BoxCollider))]
public sealed class CinematicSceneTrigger : NetworkBehaviour
{
    [SerializeField] private string destinationScene = "B2F";
    [SerializeField] private DeFrag.B1F.B1FEscapeSequence escapeSequence;
    [Tooltip("Quest signal reported (server) when the whole party has reached the exit.")]
    [SerializeField] private string exitReachedSignal = QuestSignals.B1FExitReached;
    [Tooltip("Seconds every player must stay inside before the transition starts (avoids brushing the edge).")]
    [Min(0f)] [SerializeField] private float holdSeconds = 0.6f;

    private readonly NetworkVariable<byte> playersInside = new();
    private readonly NetworkVariable<byte> playersRequired = new();
    private BoxCollider zone;
    private float allInsideSince = -1f;
    private bool started;

    /// <summary>Replicated: party members inside the zone / connected party size.</summary>
    public int PlayersInside => playersInside.Value;
    public int PlayersRequired => playersRequired.Value;
    public bool IsOpen => escapeSequence == null || escapeSequence.EscapeReady;
    public bool Started => started;

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    private void Awake() => zone = GetComponent<BoxCollider>();

    private void Update()
    {
        if (!IsSpawned || !IsServer || started) return;
        // Positions, not trigger callbacks: remote players and players hiding in lockers (controller
        // disabled) do not reliably raise physics trigger events on the server.
        int required = 0, inside = 0;
        foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
        {
            required++;
            if (client.PlayerObject != null && Contains(client.PlayerObject.transform.position)) inside++;
        }
        if (playersInside.Value != inside) playersInside.Value = (byte)inside;
        if (playersRequired.Value != required) playersRequired.Value = (byte)required;

        bool everyone = IsOpen && required > 0 && inside >= required;
        if (!everyone)
        {
            allInsideSince = -1f;
            return;
        }
        if (allInsideSince < 0f) allInsideSince = Time.unscaledTime;
        if (Time.unscaledTime - allInsideSince >= holdSeconds) StartTransition();
    }

    private void StartTransition()
    {
        if (!Application.CanStreamedLevelBeLoaded(destinationScene))
        {
            Debug.LogError($"[CinematicSceneTrigger] Enable '{destinationScene}' in Build Settings.", this);
            enabled = false;
            return;
        }
        started = true;
        if (!string.IsNullOrWhiteSpace(exitReachedSignal))
            QuestManager.Instance?.ReportProgress(exitReachedSignal, "B1F_EMERGENCY_EXIT");
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        // Let the quest snapshot go out before the scene event.
        yield return null;
        SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(destinationScene, LoadSceneMode.Single);
        if (status == SceneEventProgressStatus.Started) yield break;
        started = false;
        allInsideSince = -1f;
        Debug.LogError($"[CinematicSceneTrigger] Scene load rejected: {status}", this);
    }

    /// <summary>Is a player standing at <paramref name="feet"/> inside the (possibly rotated) box?</summary>
    public bool Contains(Vector3 feet)
    {
        Vector3 local = zone.transform.InverseTransformPoint(feet + Vector3.up * 0.9f) - zone.center;
        Vector3 half = zone.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>Local query for presentation: is this peer's own player inside the zone?</summary>
    public bool ContainsLocalPlayer()
    {
        NetworkObject local = NetworkManager != null ? NetworkManager.LocalClient?.PlayerObject : null;
        return local != null && Contains(local.transform.position);
    }
}
