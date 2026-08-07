using UnityEngine;
using UnityEngine.SceneManagement;

public static class GameplayInputGate
{
    private static Object owner;
    private static int escapeConsumedFrame = -1;

    public static bool IsBlocked => owner != null;
    public static bool SuppressPauseEscape => IsBlocked || escapeConsumedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneReset()
    {
        owner = null;
        escapeConsumedFrame = -1;
        SceneManager.sceneLoaded -= ResetAfterSceneLoad;
        SceneManager.sceneLoaded += ResetAfterSceneLoad;
    }

    private static void ResetAfterSceneLoad(Scene scene, LoadSceneMode mode)
    {
        owner = null;
        escapeConsumedFrame = -1;
    }

    public static bool TryAcquire(Object requester)
    {
        if (requester == null || (owner != null && owner != requester))
            return false;

        owner = requester;
        return true;
    }

    public static void ConsumeEscape(Object requester)
    {
        if (owner == requester)
            escapeConsumedFrame = Time.frameCount;
    }

    public static void Release(Object requester)
    {
        if (owner == requester)
            owner = null;
    }
}
