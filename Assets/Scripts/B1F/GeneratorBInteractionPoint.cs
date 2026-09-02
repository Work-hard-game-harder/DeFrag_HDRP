using UnityEngine;

namespace DeFrag.B1F
{
    public enum GeneratorBInteractionType : byte
    {
        ControlPanel,
        FuelInlet
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class GeneratorBInteractionPoint : MonoBehaviour, IInteractable
    {
        [SerializeField] private GeneratorBController controller;
        [SerializeField] private GeneratorBInteractionType interactionType;

        public GeneratorBInteractionType InteractionType => interactionType;

        private void Reset()
        {
            if (controller == null)
                controller = GetComponentInParent<GeneratorBController>();
        }

        private void OnValidate()
        {
            if (controller == null)
                controller = GetComponentInParent<GeneratorBController>();
        }

        public string GetInteractionText() => controller != null
            ? controller.GetInteractionText(interactionType)
            : "발전기 B 연결 누락";

        public bool IsHoldInteraction() => false;

        public void Interact(PlayerInteraction player)
        {
            controller?.InteractAt(interactionType, player);
        }
    }
}
