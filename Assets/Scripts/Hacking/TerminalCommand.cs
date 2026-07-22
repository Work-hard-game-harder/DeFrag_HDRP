using System;

[Flags]
public enum TerminalCommands
{
    None = 0,
    UnlockDoor = 1 << 0,
    DownloadData = 1 << 1,
    ConnectServer = 1 << 2
}

public static class TerminalCommandLabel
{
    public static string Get(TerminalCommands command)
    {
        return command switch
        {
            TerminalCommands.UnlockDoor => "UNLOCK DOOR",
            TerminalCommands.DownloadData => "DOWNLOAD DATA",
            TerminalCommands.ConnectServer => "CONNECT SERVER",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
        };
    }
}
