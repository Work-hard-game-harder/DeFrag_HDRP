using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>Server chooses the destination. Each peer owns its cinematic presentation.</summary>
[RequireComponent(typeof(NetworkObject), typeof(BoxCollider))]
public sealed class CinematicSceneTrigger : NetworkBehaviour
{
    [SerializeField] private string destinationScene = "B2F";
    [SerializeField] private VideoClip cinematic;
    private bool started;

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (started || !IsServer || other.GetComponentInParent<StarterAssets.PersonController>() == null)
            return;
        if (cinematic == null || !Application.CanStreamedLevelBeLoaded(destinationScene))
        {
            Debug.LogError("[CinematicSceneTrigger] Assign a cinematic and enable the destination in Build Settings.", this);
            return;
        }
        started = true;
        ShowCinematicClientRpc();
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        // Allow the reliable cinematic message to be queued before the scene event.
        yield return null;
        SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(destinationScene, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            CancelCinematicClientRpc();
            started = false;
            Debug.LogError($"[CinematicSceneTrigger] Scene load rejected: {status}", this);
        }
    }

    [ClientRpc]
    private void ShowCinematicClientRpc() => CinematicLoadingOverlay.Begin(cinematic, destinationScene);

    [ClientRpc]
    private void CancelCinematicClientRpc() => CinematicLoadingOverlay.Cancel();
}
