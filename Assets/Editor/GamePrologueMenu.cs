using UnityEditor;

/// <summary>Editor toggle for the game-start prologue (off by default so Play Mode starts straight in the lobby).</summary>
internal static class GamePrologueMenu
{
    private const string MenuPath = "DEFRAG/Prologue/Play In Editor";

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        bool on = !EditorPrefs.GetBool(GamePrologueCinematic.PlayInEditorPrefKey, false);
        EditorPrefs.SetBool(GamePrologueCinematic.PlayInEditorPrefKey, on);
        Menu.SetChecked(MenuPath, on);
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, EditorPrefs.GetBool(GamePrologueCinematic.PlayInEditorPrefKey, false));
        return true;
    }
}
