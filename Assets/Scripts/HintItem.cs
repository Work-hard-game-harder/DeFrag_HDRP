using System;

// Kept temporarily so existing scene references continue to load.
// New hint objects should use InteractableItem directly.
[Obsolete("Use InteractableItem instead. This compatibility component may be removed after scene migration.")]
public class HintItem : InteractableItem
{
}
