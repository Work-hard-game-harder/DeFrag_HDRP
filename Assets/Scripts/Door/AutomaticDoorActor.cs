using System.Collections.Generic;
using UnityEngine;

namespace DeFrag.Doors
{
    /// <summary>
    /// Collider가 없는 NavMesh 액터도 자동문 센서가 감지할 수 있게 등록합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AutomaticDoorActor : MonoBehaviour
    {
        private static readonly HashSet<AutomaticDoorActor> ActiveActors = new();

        private void OnEnable()
        {
            ActiveActors.Add(this);
        }

        private void OnDisable()
        {
            ActiveActors.Remove(this);
        }

        public static bool IsAnyActorWithin(Vector3 position, float radius)
        {
            float radiusSquared = radius * radius;

            foreach (AutomaticDoorActor actor in ActiveActors)
            {
                if (actor == null || !actor.isActiveAndEnabled)
                    continue;

                if ((actor.transform.position - position).sqrMagnitude <= radiusSquared)
                    return true;
            }

            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry()
        {
            ActiveActors.Clear();
        }
    }
}
