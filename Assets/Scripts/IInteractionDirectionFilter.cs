using UnityEngine;

/// <summary>
/// Optional companion to <see cref="IInteractable"/>: lets an interactable reject the local
/// player's aim based on where they stand and which way they look (e.g. front-only props).
/// Pure presentation-side filtering; server RPCs must still validate on their own.
/// </summary>
public interface IInteractionDirectionFilter
{
    bool CanInteractFrom(Vector3 viewerPosition, Vector3 viewDirection);
}
