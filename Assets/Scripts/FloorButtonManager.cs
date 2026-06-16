using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FloorButtonManager : MonoBehaviour
{
    [System.Serializable]
    public class FloorButtonInfo
    {
        public string floorName;
        public Button button;
        public Sprite lockedSprite;
        public Sprite unlockedSprite;
    }

    [SerializeField] private FloorButtonInfo[] floorButtons;

    // Start() 대신 OnEnable() 사용
    // 캔버스가 SetActive(true) 될 때마다 자동으로 버튼 갱신
    void OnEnable()
    {
        foreach (var fb in floorButtons)
        {
            bool result = SettingManager.Instance.IsUnlocked(fb.floorName);
            Debug.Log($"[FloorButtonManager] floorName: '{fb.floorName}' → isUnlocked: {result} / PlayerPrefs key: 'Unlocked_{fb.floorName}' = {PlayerPrefs.GetInt("Unlocked_" + fb.floorName, 0)}");
        }

        RefreshAllButtons();
    }

    public void RefreshAllButtons()
    {
        foreach (var fb in floorButtons)
        {
            bool isUnlocked = SettingManager.Instance.IsUnlocked(fb.floorName);

            var image = fb.button.GetComponent<Image>();
            if (image != null)
                image.sprite = isUnlocked ? fb.unlockedSprite : fb.lockedSprite;

            fb.button.interactable = isUnlocked;
        }
    }
}