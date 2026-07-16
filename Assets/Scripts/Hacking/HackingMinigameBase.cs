using System;
using UnityEngine;

/// <summary>
/// Base contract for purchasable or custom hacking minigames.
/// Put the minigame UI below this component and call the protected result
/// methods when that minigame reaches an outcome.
/// </summary>
public abstract class HackingMinigameBase : MonoBehaviour
{
    public event Action Succeeded;
    public event Action Failed;
    public event Action Cancelled;

    public abstract void Begin(ConnectionDevice device);

    public virtual void End() { }

    protected void ReportSuccess() => Succeeded?.Invoke();
    protected void ReportFailure() => Failed?.Invoke();
    protected void RequestCancel() => Cancelled?.Invoke();
}
