using UnityEngine;
using UnityEngine.Rendering;

public sealed class NightVisionController : MonoBehaviour
{
    [SerializeField] private Volume nightVisionVolume;

    public void SetNightVisionActive(bool active)
    {
        if (nightVisionVolume != null)
            nightVisionVolume.weight = active ? 1f : 0f;
    }
}
