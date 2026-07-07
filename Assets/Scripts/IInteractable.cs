using UnityEngine;

public interface IInteractable
{
    // HUD에 띄워줄 아이템 이름이나 툴팁 텍스트 (예: "단서 읽기", "엘레베이터 작동")
    string GetInteractionText();

    // 꾹 누르기가 필요한 오브젝트인지 여부 반환 (단서는 true, 엘레베이터는 false)
    bool IsHoldInteraction();

    // 상호작용이 실행될 때 호출될 핵심 함수
    void Interact(PlayerInteraction player);
}