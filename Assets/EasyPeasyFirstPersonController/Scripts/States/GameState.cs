using UnityEngine;

public static class GameState
{
    public static bool isCutscene = false;

    // Enter Play Mode Options에서 Domain Reload를 끈 경우에도 이전 플레이 세션의
    // 정적 값이 남지 않도록 매 플레이 시작 시 명시적으로 초기화한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        isCutscene = false;
    }
}
