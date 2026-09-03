using Unity.Netcode;
using UnityEngine;

namespace DeFrag.Player
{
    /// <summary>
    /// 비소유자 화면에서만 캐릭터 손에 장착 아이템을 표시합니다.
    /// 로컬 1인칭 아이템은 기존 EquipmentController가 별도로 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHeldItemVisualPresenter : MonoBehaviour
    {
        private Animator animator;
        private Transform animatedHandBone;
        private Transform handAnchor;
        private GameObject worldVisual;
        private ItemData displayedItem;
        private GameObject displayedPrefab;
        private Vector3 gripLocalPosition;
        private Quaternion gripLocalRotation = Quaternion.identity;
        private Vector3 gripLocalScale = Vector3.one;

        public bool HasVisual => worldVisual != null;

        public void Show(ItemData itemData)
        {
            GameObject prefab = ResolveWorldPrefab(itemData);
            if (itemData == null || prefab == null)
            {
                Clear();
                return;
            }

            if (displayedItem == itemData && displayedPrefab == prefab && worldVisual != null)
                return;

            Show(
                prefab,
                itemData.itemName,
                itemData.attachmentBone,
                itemData.worldHandLocalPosition,
                itemData.worldHandLocalEulerAngles,
                itemData.worldHandLocalScale,
                itemData);
        }

        public void Show(
            GameObject prefab,
            string visualName,
            HumanBodyBones attachmentBone,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            Show(
                prefab,
                visualName,
                attachmentBone,
                localPosition,
                localEulerAngles,
                localScale,
                null);
        }

        private void Show(
            GameObject prefab,
            string visualName,
            HumanBodyBones attachmentBone,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale,
            ItemData itemData)
        {
            if (prefab == null)
            {
                Clear();
                return;
            }

            if (displayedItem == itemData && displayedPrefab == prefab && worldVisual != null)
                return;

            Clear();
            if (!TryResolveHandAnchor(attachmentBone))
            {
                Debug.LogWarning(
                    $"[Held Item Visual] '{name}'에서 {attachmentBone} 손뼈를 찾지 못했습니다.",
                    this);
                return;
            }

            worldVisual = Instantiate(prefab, handAnchor, false);
            worldVisual.name = $"WorldHeld_{visualName}";

            // 월드 아이템 자체가 아니라 손바닥의 Grip 지점에 보정값을 적용합니다.
            // Grip은 RightHand 본의 자식이므로 줍기/걷기/달리기/앉기 애니메이션의
            // 손 이동과 회전을 그대로 따라갑니다.
            gripLocalPosition = localPosition;
            gripLocalRotation = Quaternion.Euler(localEulerAngles);
            gripLocalScale = localScale;
            FollowAnimatedHand();

            // 1인칭 프리팹에 루트 Transform 애니메이션이 있어도 손 위치를
            // 덮어쓰지 못하도록 먼저 표현 전용 상태로 만든 뒤 원점에 고정합니다.
            PreparePresentationClone(worldVisual);
            worldVisual.transform.localPosition = Vector3.zero;
            worldVisual.transform.localRotation = Quaternion.identity;
            worldVisual.transform.localScale = Vector3.one;

            displayedItem = itemData;
            displayedPrefab = prefab;
        }

        private void LateUpdate()
        {
            // 네트워크는 장착 상태만 동기화합니다. 실제 위치는 이 클라이언트에서
            // Animator가 계산한 최신 손뼈를 따라야 지연이나 바인드 포즈 고정이 없습니다.
            if (worldVisual != null)
                FollowAnimatedHand();
        }

        public void Clear()
        {
            if (worldVisual != null)
                Destroy(worldVisual);

            worldVisual = null;
            displayedItem = null;
            displayedPrefab = null;
        }

        private bool TryResolveHandAnchor(HumanBodyBones attachmentBone)
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            if (animator == null || !animator.isHuman)
                return false;

            Transform humanoidBone = animator.GetBoneTransform(attachmentBone);
            if (humanoidBone == null)
                return false;

            animatedHandBone = ResolveRenderedBone(humanoidBone);
            if (animatedHandBone == null)
                return false;

            if (handAnchor == null || handAnchor.parent != animatedHandBone)
            {
                GameObject anchorObject = new("WorldItemPoint");
                handAnchor = anchorObject.transform;
                handAnchor.SetParent(animatedHandBone, false);
            }

            return true;
        }

        private Transform ResolveRenderedBone(Transform humanoidBone)
        {
            // Humanoid 매핑과 실제 SkinnedMesh가 같은 Transform을 쓰는지 확인합니다.
            // 리그가 교체되거나 중첩 Animator가 생겨도 화면에 그려지는 손뼈를 우선합니다.
            foreach (SkinnedMeshRenderer skinnedMesh in
                     GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform meshBone in skinnedMesh.bones)
                {
                    if (meshBone == humanoidBone)
                        return meshBone;
                }

                foreach (Transform meshBone in skinnedMesh.bones)
                {
                    if (meshBone != null && meshBone.name == humanoidBone.name)
                        return meshBone;
                }
            }

            return humanoidBone;
        }

        private void FollowAnimatedHand()
        {
            if (animatedHandBone == null || handAnchor == null)
                return;

            // Animator/NetworkAnimator가 본을 갱신한 뒤에도 Grip의 로컬 오프셋을
            // 명시적으로 다시 적용하여 항상 현재 애니메이션 손바닥을 따라갑니다.
            Vector3 gripWorldPosition = animatedHandBone.TransformPoint(gripLocalPosition);
            Quaternion gripWorldRotation = animatedHandBone.rotation * gripLocalRotation;
            handAnchor.SetPositionAndRotation(gripWorldPosition, gripWorldRotation);
            handAnchor.localScale = gripLocalScale;
        }

        private static GameObject ResolveWorldPrefab(ItemData itemData)
        {
            if (itemData == null)
                return null;

            if (itemData.worldHeldPrefab != null)
                return itemData.worldHeldPrefab;
            if (itemData.heldPrefab != null)
                return itemData.heldPrefab;
            return itemData.itemPrefab;
        }

        private static void PreparePresentationClone(GameObject visual)
        {
            foreach (Animator itemAnimator in visual.GetComponentsInChildren<Animator>(true))
            {
                itemAnimator.applyRootMotion = false;
                itemAnimator.enabled = false;
            }

            foreach (Animation legacyAnimation in
                     visual.GetComponentsInChildren<Animation>(true))
            {
                legacyAnimation.Stop();
                legacyAnimation.enabled = false;
            }

            foreach (Collider itemCollider in visual.GetComponentsInChildren<Collider>(true))
                itemCollider.enabled = false;

            foreach (Rigidbody rigidbody in visual.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
            }

            foreach (NetworkBehaviour behaviour in
                     visual.GetComponentsInChildren<NetworkBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (NetworkObject networkObject in
                     visual.GetComponentsInChildren<NetworkObject>(true))
            {
                networkObject.enabled = false;
            }

            foreach (GetItem pickup in visual.GetComponentsInChildren<GetItem>(true))
                pickup.enabled = false;

            foreach (Camera itemCamera in visual.GetComponentsInChildren<Camera>(true))
                itemCamera.enabled = false;

            foreach (AudioListener listener in visual.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
