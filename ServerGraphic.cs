using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using HttpUtils;

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "LINKTOIMAGE";
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.2";
    public override string ModuleAuthor => "unfortunate";
    public int iMpFreezeTimemp;
    public bool bShowingServerGraphic = false;

    public ServerGraphicConfig Config { get; set; } = new();

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphice] Loading +++ ");
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);
        if (hotReload)
        {
            Console.WriteLine("[INFO] [CS2ServerGraphic] hotReload +++ ");
            Console.WriteLine("[INFO] [CS2ServerGraphic] hotReload --- ");
        }

        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }
    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (bShowingServerGraphic) {
                foreach (var player in Utilities.GetPlayers())
                {
                    if (!IsPlayerValid(player))
                        continue;


                    player.PrintToCenterHtml($"<img src='{Config.Image}'>");
                }
            }
        });
    }

    private void OnMapStartHandler(string mapName)
    {
        bShowingServerGraphic = false;
    }

    public void GetServerGraphicUrl()
    {
        Task.Run(async () =>
        {
            try
            {
                string? response = await Utils.HttpGetAsync("modalFeedbackEvent");
                if (response != null)
                {
                    // 处理响应（注意线程安全）
                    Logger.LogInformation(response);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"后台请求失败: {ex.Message}");
            }
        });
    }

    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        iMpFreezeTimemp = ConVar.Find("mp_freezetime")!.GetPrimitiveValue<int>();
        Logger.LogInformation("[OnEventRoundStart] Round has started with iMpFreezeTimemp: {iMpFreezeTimemp}", iMpFreezeTimemp);
        bShowingServerGraphic = true;
        AddTimer(iMpFreezeTimemp, () =>
        {
            bShowingServerGraphic = false;
            Logger.LogInformation("[OnEventRoundStart] mp_freezetime ended");
        });
        return HookResult.Continue;
    }

    #region Helpers
    public static bool IsPlayerValid(CCSPlayerController? player)
    {
        return player != null
            && player.IsValid
            && !player.IsBot
            && player.Pawn != null
            && player.Pawn.IsValid
            && player.Connected == PlayerConnectedState.PlayerConnected
            && !player.IsHLTV;
    }
    #endregion
}
