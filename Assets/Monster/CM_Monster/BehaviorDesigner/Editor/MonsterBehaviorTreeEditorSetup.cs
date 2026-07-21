using BehaviorDesigner.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MonsterBehaviorTreeEditorSetup
{
    private const string MenuPath = "Tools/DeFrag/Monster/Create Behavior Tree For Selected Monster";

    [MenuItem(MenuPath)]
    private static void CreateForSelectedMonster()
    {
        MonsterAI monsterAI = GetSelectedMonster();
        if (monsterAI == null)
            return;

        Undo.RecordObject(monsterAI, "Enable Monster Behavior Designer");
        monsterAI.SetBehaviorDesignerEnabled(true);

        BehaviorTree behaviorTree = monsterAI.GetComponent<BehaviorTree>();
        if (behaviorTree == null)
            behaviorTree = Undo.AddComponent<BehaviorTree>(monsterAI.gameObject);

        MonsterBehaviorTreeInstaller installer = monsterAI.GetComponent<MonsterBehaviorTreeInstaller>();
        if (installer == null)
            installer = Undo.AddComponent<MonsterBehaviorTreeInstaller>(monsterAI.gameObject);

        behaviorTree = installer.EnsureBehaviorTree();
        BehaviorSource source = behaviorTree.GetBehaviorSource();
        BehaviorDesigner.Editor.BinarySerialization.Save(source);

        if (!HasValidSerializedLayout(source))
        {
            Debug.LogError("[Monster Behavior Tree] 직렬화된 Task ID/부모 관계가 올바르지 않습니다. 트리를 저장하지 않았습니다.", monsterAI);
            return;
        }

        EditorUtility.SetDirty(monsterAI);
        EditorUtility.SetDirty(installer);
        EditorUtility.SetDirty(behaviorTree);
        EditorSceneManager.MarkSceneDirty(monsterAI.gameObject.scene);

        GameObject selectedMonster = monsterAI.gameObject;
        Selection.activeObject = null;
        EditorApplication.delayCall += () =>
        {
            Selection.activeGameObject = selectedMonster;
            EditorGUIUtility.PingObject(selectedMonster);
        };

        Debug.Log("[Monster Behavior Tree] Entry + Selector + 상태 6개를 정상적으로 생성하고 연결했습니다.", selectedMonster);
    }

    [MenuItem(MenuPath, true)]
    private static bool CanCreateForSelectedMonster()
    {
        return !EditorApplication.isPlaying && GetSelectedMonster() != null;
    }

    private static MonsterAI GetSelectedMonster()
    {
        GameObject selectedObject = Selection.activeGameObject;
        return selectedObject == null ? null : selectedObject.GetComponentInParent<MonsterAI>();
    }

    private static bool HasValidSerializedLayout(BehaviorSource source)
    {
        TaskSerializationData data = source.TaskData;
        if (data == null || data.types == null || data.parentIndex == null ||
            data.types.Count != 8 || data.parentIndex.Count != 8)
            return false;

        if (data.parentIndex[0] != -1 || data.parentIndex[1] != 0)
            return false;

        for (int i = 2; i < data.parentIndex.Count; i++)
        {
            if (data.parentIndex[i] != 1)
                return false;
        }

        return true;
    }
}
