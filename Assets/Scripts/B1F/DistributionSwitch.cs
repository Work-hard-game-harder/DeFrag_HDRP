using System.Collections;
using UnityEngine;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    public sealed class DistributionSwitch : MonoBehaviour
    {
        [Header("Pivot-relative positions")]
        [SerializeField] private float onLocalX = -0.1038886f;
        [SerializeField] private float offLocalX = 0.03645783f;
        [SerializeField, Min(0.01f)] private float moveDuration = 0.16f;
        [SerializeField] private AnimationCurve moveCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private Coroutine moveRoutine;
        public int Index { get; private set; } = -1;
        public bool IsOn { get; private set; }

        internal void Configure(int index) => Index = index;

        internal void ApplyState(bool isOn, bool immediate)
        {
            IsOn = isOn;
            float targetX = isOn ? onLocalX : offLocalX;
            if (moveRoutine != null) StopCoroutine(moveRoutine);

            if (immediate || !gameObject.activeInHierarchy)
            {
                SetLocalX(targetX);
                moveRoutine = null;
                return;
            }

            moveRoutine = StartCoroutine(MoveRoutine(targetX));
        }

        private IEnumerator MoveRoutine(float targetX)
        {
            float startX = transform.localPosition.x;
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = moveCurve.Evaluate(Mathf.Clamp01(elapsed / moveDuration));
                SetLocalX(Mathf.LerpUnclamped(startX, targetX, t));
                yield return null;
            }

            SetLocalX(targetX);
            moveRoutine = null;
        }

        private void SetLocalX(float x)
        {
            Vector3 position = transform.localPosition;
            position.x = x;
            transform.localPosition = position;
        }
    }
}
