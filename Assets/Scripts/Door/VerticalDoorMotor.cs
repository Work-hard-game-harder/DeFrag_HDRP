using System.Collections;
using UnityEngine;

namespace DeFrag.Doors
{
    public sealed class VerticalDoorMotor : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float openHeight = 6f;
        [SerializeField, Min(0.01f)] private float moveSpeed = 3f;

        private Vector3 closedPosition;
        private Vector3 openPosition;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            closedPosition = transform.position;
            openPosition = closedPosition + Vector3.up * openHeight;
        }

        public void Open()
        {
            IsOpen = true;
            MoveTo(openPosition);
        }

        public void Close()
        {
            IsOpen = false;
            MoveTo(closedPosition);
        }

        private void MoveTo(Vector3 target)
        {
            StopAllCoroutines();
            StartCoroutine(MoveRoutine(target));
        }

        private IEnumerator MoveRoutine(Vector3 target)
        {
            while (transform.position != target)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    target,
                    moveSpeed * Time.deltaTime);
                yield return null;
            }
        }
    }
}
