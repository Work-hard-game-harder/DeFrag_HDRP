using UnityEngine;

public sealed class NightVisionIlluminator : MonoBehaviour
{
    [Header("Infrared Light")]
    [SerializeField] private Light infraredLight;

    public void SetActive(bool active)
    {
        if (infraredLight != null)
            infraredLight.enabled = active;
    }

    private void Awake()
    {
        // 프리팹 생성 직후에는 반드시 꺼진 상태로 시작
        SetActive(false);
    }

    private void OnDisable()
    {
        SetActive(false);
    }
}