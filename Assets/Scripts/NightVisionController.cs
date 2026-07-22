using Unity.AppUI.UI;
using UnityEngine;
using UnityEngine.Rendering;

public class NightVisionController : MonoBehaviour
{
    public Volume nightVisionVolume;
    private bool isNightVisionActive = false;

    void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            ToggleNightVision();
        }
    }

    private void ToggleNightVision()
    {
        if(nightVisionVolume == null) { return; }

        isNightVisionActive = !isNightVisionActive;
        nightVisionVolume.weight = isNightVisionActive ? 1 : 0;
    }
}