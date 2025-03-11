namespace WSServerInfo;

public class PlayerModel
{
    public int UID {get; set;}
    public int Slot {get; set;}
    public int Team {get; set;}
    public string Name {get; set;}
    public ulong? SteamID {get; set;}
    public string? IP {get; set;}
    public bool IsBot {get; set;}
    public string AvatarUrl {get; set;} = "";
    public long ConnectedAt {get; set;}
}