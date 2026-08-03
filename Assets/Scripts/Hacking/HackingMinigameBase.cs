using System;
using UnityEngine;

/// <summary>
/// Base contract for purchasable or custom hacking minigames.
/// Put the minigame UI below this component and call the protected result
/// methods when that minigame reaches an outcome.
/// </summary>
public abstract class HackingMinigameBase : MonoBehaviour
{
    [Header("Menu")]
    [SerializeField] private string displayName = "PASSWORD CRACKING";

    public event Action Succeeded;
    public event Action Failed;
    public event Action Cancelled;

    public string DisplayName => displayName;
    public virtual bool ConsumesTextInput => false;
    public virtual bool CloseTerminalOnSuccess => false;
    public virtual string ControlHint => "[W/S] SELECT    [E] EXECUTE    [BACKSPACE] RETURN";

    public abstract void Begin(ConnectionDevice device, TerminalCommands command);

    public virtual void End() { }

    protected void ReportSuccess() => Succeeded?.Invoke();
    protected void ReportFailure() => Failed?.Invoke();
    protected void RequestCancel() => Cancelled?.Invoke();
}
