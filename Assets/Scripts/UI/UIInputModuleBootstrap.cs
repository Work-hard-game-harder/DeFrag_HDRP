using UnityEngine;
using UnityEngine.InputSystem.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(InputSystemUIInputModule))]
public sealed class UIInputModuleBootstrap : MonoBehaviour
{
    private InputSystemUIInputModule inputModule;

    private void Awake()
    {
        inputModule = GetComponent<InputSystemUIInputModule>();
        EnsureActionsAssigned();
    }

    private void OnEnable()
    {
        EnsureActionsAssigned();
    }

    private void EnsureActionsAssigned()
    {
        if (inputModule == null)
        {
            inputModule = GetComponent<InputSystemUIInputModule>();
        }

        if (inputModule.actionsAsset == null ||
            inputModule.point == null ||
            inputModule.leftClick == null ||
            inputModule.submit == null)
        {
            inputModule.AssignDefaultActions();
        }
    }
}
