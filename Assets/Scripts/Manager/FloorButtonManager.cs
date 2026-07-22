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
            //원래 코드
            // bool result = SettingManager.Instance.IsUnlocked(fb.floorName);

            // 테스트용(항상 true)
            bool result = true;

            Debug.Log($"[FloorButtonManager] floorName: '{fb.floorName}' → isUnlocked: {result} / PlayerPrefs key: 'Unlocked_{fb.floorName}' = {PlayerPrefs.GetInt("Unlocked_" + fb.floorName, 0)}");
        }

        RefreshAllButtons();
    }

    public void RefreshAllButtons()
    {
        foreach (var fb in floorButtons)
        {
            //원래 코드
            //bool isUnlocked = SettingManager.Instance.IsUnlocked(fb.floorName);

            // 테스트용(강제해금)
            bool isUnlocked = true;

            var image = fb.button.GetComponent<Image>();
            if (image != null)
                image.sprite = isUnlocked ? fb.unlockedSprite : fb.lockedSprite;

            fb.button.interactable = isUnlocked;
        }
    }
}