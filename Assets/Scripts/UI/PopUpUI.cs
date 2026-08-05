using UnityEngine;

public class PopupUI : MonoBehaviour
{
    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ClosePopup()
    {
        AudioManager.Instance.PlaySFX("UIClick");
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}