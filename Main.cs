using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;

namespace WSServerInfo;

public class PluginConfig : BasePluginConfig {
    public string ServerPort {get; set;} = "27015"; // ws://localhost:27015/wssinfo
    public string SteamWebApi {get; set;} = "";
}

public class Main : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "WSServerInfo";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "iks__";
    public override string ModuleDescription => "Web-socket server info";

    public PluginConfig Config {get; set;} = null!;

    public async Task<PlayerSummaries?> GetPlayerSummaries(ulong steamId)
    {
        if (Config.SteamWebApi == "") return null;
        var webInterfaceFactory = new SteamWebInterfaceFactory(Config.SteamWebApi);
        var steamInterface = webInterfaceFactory.CreateSteamWebInterface<SteamUser>(new HttpClient());
        var playerSummaryResponse = await steamInterface.GetPlayerSummaryAsync(steamId);
        var data = playerSummaryResponse.Data;
        var summaries = new PlayerSummaries(
            data.SteamId,
            data.Nickname,
            data.ProfileUrl,
            data.AvatarUrl,
            data.AvatarFullUrl,
            data.AvatarUrl
        );
        return summaries;
    }

    private static readonly HttpListener _httpListener = new HttpListener();
    // Храним тут коннекты
    private static readonly ConcurrentBag<WebSocket> _webSockets = new ConcurrentBag<WebSocket>();
    private long[] _connects = new long[72];
    private Dictionary<ulong, string> _avatars {get; set;} = new();
    
    public override void Unload(bool hotReload)
    {
        base.Unload(hotReload);
        if (_httpListener.IsListening)
        {
            _httpListener.Stop();
            _httpListener.Close();
        }
    }

    public void UpdateAndSendMessages() {
        var playerModels = new List<PlayerModel>();
        var players = Utilities.GetPlayers();
        foreach (var p in players.ToList())
        {
            var pm = new PlayerModel() {
                UID = (int)p.UserId!,
                Name = p.PlayerName,
                Slot = p.Slot,
                SteamID = p.AuthorizedSteamID?.SteamId64 ?? null,
                IP = p.IpAddress ?? null,
                IsBot = p.IsBot,
                ConnectedAt = _connects[p.Slot],
                Team = p.TeamNum,
                Kills = p.ActionTrackingServices!.MatchStats.Kills,
                Deaths = p.ActionTrackingServices!.MatchStats.Deaths,
                Damage = p.ActionTrackingServices!.MatchStats.Damage,
                Assists = p.ActionTrackingServices!.MatchStats.Assists,
                HeadShotKills = p.ActionTrackingServices!.MatchStats.HeadShotKills,
                EntryCount = p.ActionTrackingServices!.MatchStats.EntryCount,
                AvatarUrl = p.AuthorizedSteamID != null ? _avatars[p.AuthorizedSteamID.SteamId64] : ""
            };
            playerModels.Add(pm);
        }
        Task.Run(async () => {
            await SendMessagesToAllClients(playerModels);
        });
    }

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);
        Task.Run(async () => {
            await StartServer();
        });
    }

    [GameEventHandler]
    public HookResult EventPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        _connects[player.Slot] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (player.AuthorizedSteamID != null)
        {
            var steamid = player.AuthorizedSteamID.SteamId64;
            _avatars[steamid] = "";
            Task.Run(async () => {
                var summ = await GetPlayerSummaries(steamid);
                if (summ != null)
                {
                    _avatars[steamid] = summ.AvatarFull;
                }
            });
        }
        
        Server.NextFrame(() => {
            UpdateAndSendMessages();
        }); 
        return HookResult.Continue;
    }
    [GameEventHandler]
    public HookResult EventPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        Server.NextFrame(() => {
            UpdateAndSendMessages();
        }); 
        return HookResult.Continue;
    }
    [GameEventHandler]
    public HookResult OnKill(EventPlayerDeath @event, GameEventInfo info)
    {
        Server.NextFrame(() => {
            UpdateAndSendMessages();
        }); 
        return HookResult.Continue;
    }
    [GameEventHandler]
    public HookResult OnTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        Server.NextFrame(() => {
            UpdateAndSendMessages();
        }); 
        return HookResult.Continue;
    }
    

    private async Task StartServer()
    {
        try
        {
            Console.WriteLine("Starting server..");
            _httpListener.Prefixes.Add($"http://*:{Config.ServerPort}/wssinfo/");
            _httpListener.Start();
            Console.WriteLine("WEB SOCKET STARTED =)");

            while (true)
            {
                HttpListenerContext context = await _httpListener.GetContextAsync();
                if (context.Request.IsWebSocketRequest)
                {
                    ProcessWebSocketRequest(context);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }
        catch (System.Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }

    private async void ProcessWebSocketRequest(HttpListenerContext context)
    {
        WebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
        WebSocket webSocket = webSocketContext.WebSocket;

        _webSockets.Add(webSocket);

        Server.NextFrame(() => {
            UpdateAndSendMessages();
        });

        await ReceiveMessages(webSocket);
    }

    private static async Task ReceiveMessages(WebSocket webSocket)
    {
        byte[] buffer = new byte[1024];

        while (webSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _webSockets.TryTake(out _);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Закрытие соединения", CancellationToken.None);
                    break;
                }
            }
            catch (Exception ex)
            {
                break;
            }
        }
    }

    private static async Task SendMessagesToAllClients(List<PlayerModel> playerModels)
    {
        try
            {
                string jsonMessage = JsonSerializer.Serialize(playerModels);
                byte[] buffer = Encoding.UTF8.GetBytes(jsonMessage);

                foreach (var webSocket in _webSockets)
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
            }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка при отправке сообщения: " + ex.Message);
        }
    }

    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
    }
}
