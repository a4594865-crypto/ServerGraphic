using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using HttpUtils;
using System.Linq;

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "LINKTOIMAGE";

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 600;

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 120;

    // 🚨 新增：顯示秒數設定 (預設 5.0 秒)
    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f;

    // 🚨 新增：OnTick 刷新頻率 (預設 8，代表每 8 個 Tick 刷新一次，節省 CPU)
    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 8;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.4"; // 加入顯示秒數與 OnTick 刷新設定
    public override string ModuleAuthor => "unfortunate";
    
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
            if (bShowingServerGraphic) 
            {
                // 🚨 新增：跳幀邏輯 (避免每幀發送導致效能浪費)
                int tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;
                if (Server.TickCount % tickInterval != 0) return;

                foreach (var player in Utilities.GetPlayers())
                {
                    if (!IsPlayerValid(player))
                        continue;

                    player.PrintToCenterHtml($"<img src='{Config.Image}' width='{Config.ImageWidth}' height='{Config.ImageHeight}'>");
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
        if (!IsLive())
        {
            return HookResult.Continue;
        }

        Logger.LogInformation($"[OnEventRoundStart] Round started. HUD will show for {Config.DisplayDuration} seconds.");
        bShowingServerGraphic = true;
        
        // 🚨 修改：使用設定檔中的秒數 (Config.DisplayDuration)
        AddTimer(Config.DisplayDuration, () =>
        {
            bShowingServerGraphic = false;
            
            // 🚨 新增：時間到的瞬間，主動清空所有玩家的畫面，避免圖片殘留
            foreach (var player in Utilities.GetPlayers())
            {
                if (IsPlayerValid(player))
                {
                    player.PrintToCenterHtml(""); 
                }
            }
            
            Logger.LogInformation("[OnEventRoundStart] Display duration ended, HUD cleared.");
        });
        
        return HookResult.Continue;
    }

    #region Helpers
    public static bool IsPlayerValid(CCSPlayerController? player)
    {
        return player != null
            && player.IsValid
            && !player.IsBot
            && !player.IsHLTV
            && player.PlayerPawn != null
            && player.PlayerPawn.IsValid
            && player.PlayerPawn.Value != null
            && player.PlayerPawn.Value.IsValid;
    }

    private bool IsLive()
    {
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
        {
            if (gameRulesProxy.GameRules.WarmupPeriod)
            {
                return false;
            }
        }

        var maxMoneyConVar = ConVar.Find("mp_maxmoney");
        if (maxMoneyConVar != null)
        {
            try 
            { 
                if (maxMoneyConVar.GetPrimitiveValue<int>() == 0) return false; 
            } 
            catch { }
        }

        return true;
    }
    #endregion
}
