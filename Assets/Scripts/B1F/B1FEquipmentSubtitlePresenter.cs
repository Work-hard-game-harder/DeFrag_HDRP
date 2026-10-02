using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    /// <summary>
    /// 공용 장비 수집 퀘스트가 완료된 뒤, 각 클라이언트의 소유 플레이어가
    /// 실제로 보유한 역할 아이템에 맞는 자막만 로컬로 재생합니다.
    /// 퀘스트 진행/아이템 소유 권한은 기존 서버 권한 구조를 그대로 사용합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class B1FEquipmentSubtitlePresenter : MonoBehaviour
    {
        [Serializable]
        private sealed class EquipmentSubtitleBinding
        {
            [Tooltip("이 역할을 판별할 인벤토리 아이템 데이터입니다.")]
            public ItemData requiredItem;

            [Tooltip("해당 아이템을 가진 로컬 플레이어에게 재생할 자막 Trigger입니다.")]
            public SubtitleTrigger subtitleTrigger;
        }

        [Header("Quest Completion")]
        [SerializeField] private QuestManager questManager;
        [SerializeField] private string equipmentQuestId = "b1f_equipment";

        [Header("Local Role Subtitles")]
        [SerializeField] private List<EquipmentSubtitleBinding> roleSubtitles = new();

        private NetworkPlayerInventory localInventory;
        private Coroutine bindRoutine;
        private bool questSubscribed;
        private bool inventorySubscribed;
        private bool hasPresented;

        private void OnEnable()
        {
            bindRoutine = StartCoroutine(BindDependencies());
        }

        private void OnDisable()
        {
            if (bindRoutine != null)
            {
                StopCoroutine(bindRoutine);
                bindRoutine = null;
            }

            Unsubscribe();
        }

        private IEnumerator BindDependencies()
        {
            while (isActiveAndEnabled)
            {
                if (questManager == null)
                    questManager = QuestManager.Instance;

                if (!questSubscribed && questManager != null)
                {
                    questManager.onQuestStepChanged += HandleQuestStepChanged;
                    questSubscribed = true;
                }

                if (localInventory == null)
                    localInventory = ResolveLocalInventory();

                if (!inventorySubscribed && localInventory != null)
                {
                    localInventory.HeldItemsChanged += HandleHeldItemsChanged;
                    inventorySubscribed = true;
                }

                if (questManager != null && questManager.IsInitialized && localInventory != null)
                {
                    TryPresentRoleSubtitle();
                    bindRoutine = null;
                    yield break;
                }

                yield return null;
            }

            bindRoutine = null;
        }

        private NetworkPlayerInventory ResolveLocalInventory()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                NetworkObject localPlayer = manager.LocalClient?.PlayerObject;
                return localPlayer != null
                    ? localPlayer.GetComponent<NetworkPlayerInventory>()
                    : null;
            }

            // B1F 씬 단독 실행을 위한 비네트워크 테스트 경로입니다.
            NetworkPlayerInventory[] inventories =
                FindObjectsByType<NetworkPlayerInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (NetworkPlayerInventory inventory in inventories)
            {
                if (inventory != null && (!inventory.IsSpawned || inventory.IsOwner))
                    return inventory;
            }

            return null;
        }

        private void HandleQuestStepChanged()
        {
            TryPresentRoleSubtitle();
        }

        private void HandleHeldItemsChanged()
        {
            // 퀘스트를 먼저 완료하고 역할 아이템 동기화가 조금 늦게 도착하거나,
            // 다른 역할 아이템을 나중에 획득하는 경우도 놓치지 않습니다.
            TryPresentRoleSubtitle();
        }

        private void TryPresentRoleSubtitle()
        {
            if (hasPresented || localInventory == null || !IsEquipmentQuestCompleted())
                return;

            foreach (EquipmentSubtitleBinding binding in roleSubtitles)
            {
                if (binding == null || binding.requiredItem == null || binding.subtitleTrigger == null)
                    continue;

                if (!localInventory.ContainsHeldItem(binding.requiredItem))
                    continue;

                // 한 플레이어가 두 역할 아이템을 모두 가진 솔로 테스트에서도
                // Inspector 목록의 첫 번째 일치 역할만 재생합니다.
                hasPresented = true;
                binding.subtitleTrigger.PlaySubtitleFromInteract();
                return;
            }
        }

        private bool IsEquipmentQuestCompleted()
        {
            if (questManager == null || !questManager.IsInitialized ||
                string.IsNullOrWhiteSpace(equipmentQuestId))
                return false;

            int questIndex = questManager.questList.FindIndex(step =>
                step != null && string.Equals(
                    step.questId,
                    equipmentQuestId.Trim(),
                    StringComparison.OrdinalIgnoreCase));
            if (questIndex < 0)
                return false;

            int currentIndex = questManager.GetCurrentStepIndex();
            if (currentIndex > questIndex)
                return true;
            if (currentIndex < questIndex)
                return false;

            QuestStep equipmentStep = questManager.questList[questIndex];
            return equipmentStep != null && equipmentStep.IsCompleted();
        }

        private void Unsubscribe()
        {
            if (questSubscribed && questManager != null)
                questManager.onQuestStepChanged -= HandleQuestStepChanged;
            questSubscribed = false;

            if (inventorySubscribed && localInventory != null)
                localInventory.HeldItemsChanged -= HandleHeldItemsChanged;
            inventorySubscribed = false;
        }
    }
}
