using System;

// Kept temporarily so older scenes do not lose their component reference.
// New and migrated pickup objects should use GetItem with an ItemData asset.
[Obsolete("Use GetItem with ItemData instead.")]
public class InventoryItem : GetItem
{
}
