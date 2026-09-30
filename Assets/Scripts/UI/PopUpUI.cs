using UnityEngine;

public class PopupUI : MonoBehaviour
{
    private bool ownsInputGate;

    private void OnEnable()
    {
        ownsInputGate = GameplayInputGate.TryAcquire(this);
        ApplyOpenCursorState();
    }

    private void LateUpdate()
    {
        // 다른 로컬 플레이어/메뉴 스크립트가 커서를 다시 잠그더라도
        // 팝업이 열린 동안에는 버튼을 누를 수 있는 상태를 유지합니다.
        ApplyOpenCursorState();
    }

    public void ClosePopup()
    {
        AudioManager.Instance?.PlaySFX("UIClick");
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (ownsInputGate)
        {
            GameplayInputGate.Release(this);
            ownsInputGate = false;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDestroy()
    {
        if (ownsInputGate)
            GameplayInputGate.Release(this);
    }

    private static void ApplyOpenCursorState()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
