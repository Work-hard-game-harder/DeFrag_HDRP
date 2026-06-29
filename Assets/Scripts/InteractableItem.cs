using UnityEngine;

public class InteractableItem : MonoBehaviour
{
    public string itemName = "의문의 문서";
    public Sprite hintSprite; // 힌트 이미지
    public bool isInteracted = false; //누가 먹었는지 체크하는 용도
}
