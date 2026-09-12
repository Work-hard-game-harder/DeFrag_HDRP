using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DeFrag.UI
{
    /// <summary>
    /// DeathScreen 프리팹의 표시만 담당합니다. 레이아웃과 색상은 프리팹에서 편집합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PartyFailureView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text deathMessage;
        [SerializeField, TextArea] private string teammateFailureMessage = "전원 생존 실패\n\nMISSION FAILED";
        [SerializeField] private GameObject hostButtons;
        [SerializeField] private Button returnToLobbyButton;
        [SerializeField] private Button stageSelectionButton;

        public void Initialize(
            bool showHostButtons,
            bool isDeceasedLocalPlayer,
            float fadeDuration,
            UnityAction returnToLobby,
            UnityAction openStageSelection)
        {
            // 실제 사망자는 프리팹에 작성된 기본 문구를 그대로 사용합니다.
            if (!isDeceasedLocalPlayer && deathMessage != null)
                deathMessage.text = teammateFailureMessage;

            if (hostButtons != null)
                hostButtons.SetActive(showHostButtons);

            if (returnToLobbyButton != null)
            {
                returnToLobbyButton.onClick.RemoveAllListeners();
                returnToLobbyButton.onClick.AddListener(returnToLobby);
            }

            if (stageSelectionButton != null)
            {
                stageSelectionButton.onClick.RemoveAllListeners();
                stageSelectionButton.onClick.AddListener(openStageSelection);
            }

            StartCoroutine(FadeIn(fadeDuration));
        }

        private IEnumerator FadeIn(float duration)
        {
            if (canvasGroup == null)
                yield break;

            canvasGroup.alpha = 0f;
            float safeDuration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < safeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsed / safeDuration);
                yield return null;
            }

            canvasGroup.alpha = 1f;
        }
    }
}
