using DeFrag.Player;
using EasyPeasyFirstPersonController;
using Unity.Netcode;
using UnityEngine;

public sealed class LobbyAvatar : MonoBehaviour
{
    private void Awake()
    {
        foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (ShouldRemainEnabled(behaviour))
                continue;

            behaviour.enabled = false;
        }

        foreach (Camera avatarCamera in GetComponentsInChildren<Camera>(true))
        {
            avatarCamera.enabled = false;
        }

        foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = false;
        }

        foreach (CharacterController controller in GetComponentsInChildren<CharacterController>(true))
        {
            controller.enabled = false;
        }

        foreach (AudioSource audioSource in GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.enabled = false;
        }
    }

    private bool ShouldRemainEnabled(MonoBehaviour behaviour)
    {
        return behaviour == this ||
               behaviour is NetworkObject ||
               behaviour is SoundEmitter ||
               behaviour is NetworkVoiceActivityIndicator;
    }
}
