using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BtnTextDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI targetTMP; // UI에 표시할 TMP 텍스트

    // 버튼에 연결할 메서드
    public void ShowButtonText(string textToShow)
    {
        if (targetTMP != null)
        {
            targetTMP.text = textToShow;
        }
    }
}
