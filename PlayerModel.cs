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
    public int Kills {get; set;} = 0;
    public int Deaths {get; set;} = 0;
    public int Damage {get; set;} = 0;
    public int Assists {get; set;} = 0;
    public int HeadShotKills {get; set;} = 0;
    public int EntryCount {get; set;} = 0;
    public long ConnectedAt {get; set;}
}