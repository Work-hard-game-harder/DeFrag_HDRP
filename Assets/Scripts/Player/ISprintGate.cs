namespace DeFrag.Player
{
    /// <summary>
    /// Decouples the movement controller from the system that grants sprint permission.
    /// </summary>
    public interface ISprintGate
    {
        bool CanSprint { get; }
        void SetSprinting(bool isSprinting);
    }
}

