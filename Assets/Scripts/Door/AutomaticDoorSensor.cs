using UnityEngine;

namespace DeFrag.Doors
{
    public sealed class AutomaticDoorSensor : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField, Min(0.1f)] private float detectionRadius = 4f;
        [SerializeField] private LayerMask actorLayers = 1 << 7;
        [Tooltip("Collider가 없는 TV몬스터처럼 AutomaticDoorActor로 등록된 액터도 감지합니다.")]
        [SerializeField] private bool detectRegisteredActors = true;

        [Header("Door")]
        [SerializeField] private VerticalDoorMotor door;

        private readonly Collider[] detectedActors = new Collider[16];
        private void FixedUpdate()
        {
            bool detected = Physics.OverlapSphereNonAlloc(
                transform.position,
                detectionRadius,
                detectedActors,
                actorLayers,
                QueryTriggerInteraction.Ignore) > 0;

            if (!detected && detectRegisteredActors)
                detected = AutomaticDoorActor.IsAnyActorWithin(transform.position, detectionRadius);

            if (detected)
                door.Open();
            else
                door.Close();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.3f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);
        }
    }
}
