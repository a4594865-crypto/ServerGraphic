using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "LINKTOIMAGE";

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 600;

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 120;

    // 已經不需要 DisplayDuration 秒數了，完全交由伺服器凍結時間決定
    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 8;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.7"; // 原生事件連動版：完美同步凍結時間
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading +++ ");
        // 確保換圖時關閉
        RegisterListener<Listeners.OnMapStart>(map => bShowingServerGraphic = false);
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        currentImageHtml = $"<img src='{Config.Image}' width='{Config.ImageWidth}' height='{Config.ImageHeight}'>";

        // 負責在凍結時間內維持圖片顯示
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic) return;

            int tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;
            if (Server.TickCount % tickInterval != 0) return;

            foreach (var player in Utilities.GetPlayers())
            {
                if (IsPlayerValid(player))
                {
                    player.PrintToCenterHtml(currentImageHtml);
                }
            }
        });
    }

    // 🟢 事件 1：回合開始（凍結時間起點）
    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // 判斷是否為 Live 局
        if (!IsLive())
        {
            Logger.LogInformation("[ServerGraphic] 偵測為暖身或刀局，不顯示 HUD。");
            return HookResult.Continue;
        }

        Logger.LogInformation("[ServerGraphic] Live 局開始，進入凍結時間，啟動 HUD。");
        bShowingServerGraphic = true; // 開啟 OnTick 投影

        return HookResult.Continue;
    }

    // 🔴 事件 2：凍結時間結束（玩家可以移動的瞬間）
    [GameEventHandler]
    public HookResult OnEventRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (bShowingServerGraphic)
        {
            Logger.LogInformation("[ServerGraphic] 凍結時間結束，關閉 HUD 並清除黑框。");
            bShowingServerGraphic = false; // 關閉 OnTick 投影

            // 發送空白字串，瞬間清除引擎殘留的黑框
            foreach (var player in Utilities.GetPlayers())
            {
                if (IsPlayerValid(player))
                {
                    player.PrintToCenter(" "); 
                }
            }
        }
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
        // 檢查內建暖身
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
        {
            if (gameRulesProxy.GameRules.WarmupPeriod) return false;
        }

        // 對準你的 CFG：mp_maxmoney 0
        var maxMoney = ConVar.Find("mp_maxmoney");
        if (maxMoney != null)
        {
            try { if (maxMoney.GetPrimitiveValue<int>() == 0) return false; } catch { }
        }

        // 對準你的 CFG：mp_give_player_c4 0
        var giveC4 = ConVar.Find("mp_give_player_c4");
        if (giveC4 != null)
        {
            try { if (giveC4.GetPrimitiveValue<int>() == 0) return false; } catch { }
            try { if (giveC4.GetPrimitiveValue<bool>() == false) return false; } catch { }
        }

        // 對準你的 CFG：mp_free_armor 1
        var freeArmor = ConVar.Find("mp_free_armor");
        if (freeArmor != null)
        {
            try { if (freeArmor.GetPrimitiveValue<int>() == 1) return false; } catch { }
            try { if (freeArmor.GetPrimitiveValue<bool>() == true) return false; } catch { }
        }

        return true; // 以上皆非，才是真正的 Live 局
    }
    #endregion
}
