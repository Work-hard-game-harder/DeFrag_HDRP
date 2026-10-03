/// <summary>
/// One question for gameplay presentation that should wait for cinematics (scene-entry subtitles,
/// subtitle triggers): is a full-screen local cinematic covering the game right now?
/// </summary>
public static class CinematicPlayback
{
    public static bool IsCoveringGameplay =>
        LobbyIntroCinematic.IsPlaying || CinematicLoadingOverlay.IsPlaying;
}
