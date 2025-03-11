using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;

namespace WSServerInfo;

public class PluginConfig : BasePluginConfig {
    public string Address {get; set;} = "localhost:27015"; // ws://localhost:27015/wssinfo
    public string BotAvatar {get; set;} = "/wssinfo";    
    public string SteamWebApi {get; set;} = "";
}

public class Main : BasePlugin
{
    public override string ModuleName => "WSServerInfo";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "iks__";
    public override string ModuleDescription => "Web-socket server info";
    private static readonly HttpListener _httpListener = new HttpListener();
    // Храним тут коннекты
    private static readonly ConcurrentBag<WebSocket> _webSockets = new ConcurrentBag<WebSocket>();
    private long[] _connects = new long[72];
    

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
                Team = p.TeamNum
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
            _httpListener.Prefixes.Add("http://*:27215/wssinfo/");
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

    private static async void ProcessWebSocketRequest(HttpListenerContext context)
    {
        // Принимаем WebSocket-соединение от клиента
        WebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
        
        // Получаем WebSocket для обмена данными
        WebSocket webSocket = webSocketContext.WebSocket;

        Console.WriteLine("Новое подключение от клиента!");
        // Добавляем его в список
        _webSockets.Add(webSocket);

        await ReceiveMessages(webSocket);
    }

    private static async Task ReceiveMessages(WebSocket webSocket)
    {
        byte[] buffer = new byte[1024];

        while (webSocket.State == WebSocketState.Open)
        {
            try
            {
                // Ожидаем сообщения от клиента
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    // Если клиент закрыл соединение, удаляем его из списка
                    _webSockets.TryTake(out _);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Закрытие соединения", CancellationToken.None);
                    Console.WriteLine("Клиент отключился.");
                    break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка при приёме сообщения: " + ex.Message);
                break;
            }
        }
    }

    private static async Task SendMessagesToAllClients(List<PlayerModel> playerModels)
    {
        try
            {
                string jsonMessage = JsonSerializer.Serialize(playerModels);
                Console.WriteLine(jsonMessage);
                byte[] buffer = Encoding.UTF8.GetBytes(jsonMessage);

                foreach (var webSocket in _webSockets)
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }

                Console.WriteLine("Сообщение отправлено всем подключённым клиентам.");
            }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка при отправке сообщения: " + ex.Message);
        }
    }

    
}
