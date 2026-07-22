using TMPro;
using UnityEngine;

public sealed class DisplayCode : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI lobbyCodeText;

    private void Start()
    {
        if (lobbyCodeText == null)
        {
            return;
        }

        lobbyCodeText.text = string.IsNullOrEmpty(LobbyManager.SavedJoinCode)
            ? "코드를 불러올 수 없습니다."
            : $"Code: {LobbyManager.SavedJoinCode}";
    }
}
