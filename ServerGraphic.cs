using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using HttpUtils;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "LINKTOIMAGE";

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 600;

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 120;

    // 恢復秒數控制
    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f;

    // 恢復跳幀頻率，節省 CPU
    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 8;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.6"; // 終極合體版：OnTick 顯示 + 空白字串殺黑框
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading +++ ");
        RegisterListener<Listeners.OnMapStart>(map => bShowingServerGraphic = false);
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        currentImageHtml = $"<img src='{Config.Image}' width='{Config.ImageWidth}' height='{Config.ImageHeight}'>";

        // 🚨 投影機機制：HTML 必須靠 OnTick 才能維持在畫面上不被引擎吃掉
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

    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // 1. 攔截暖身與刀局
        if (!IsLive())
        {
            Logger.LogInformation("[ServerGraphic] 偵測為非 Live 局 (暖身/刀局)，略過 HUD。");
            return HookResult.Continue;
        }

        Logger.LogInformation($"[ServerGraphic] Live 局開始！啟動 HUD 投影 {Config.DisplayDuration} 秒。");
        
        // 2. 開啟投影機 (啟動 OnTick)
        bShowingServerGraphic = true;

        // 3. 5 秒後強制關閉投影，並使出「空白文字殺黑框」大絕招
        AddTimer(Config.DisplayDuration, () =>
        {
            bShowingServerGraphic = false; // 停止 OnTick
            
            foreach (var player in Utilities.GetPlayers())
            {
                if (IsPlayerValid(player))
                {
                    // 🚨 關鍵：發送普通文字的「空白」，強迫引擎把 HTML 的黑框收回！
                    player.PrintToCenter(" "); 
                }
            }
            Logger.LogInformation("[ServerGraphic] 顯示結束，已發送空白文字徹底清除黑框。");
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
        // 檢查 1：是否為內建暖身時間
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
        {
            if (gameRulesProxy.GameRules.WarmupPeriod) return false;
        }

        // 檢查 2：防呆讀取你的 mp_maxmoney 0
        var maxMoney = ConVar.Find("mp_maxmoney");
        if (maxMoney != null)
        {
            try { if (maxMoney.GetPrimitiveValue<int>() == 0) return false; } catch { }
        }

        // 檢查 3：防呆讀取你的 mp_give_player_c4 0
        var giveC4 = ConVar.Find("mp_give_player_c4");
        if (giveC4 != null)
        {
            try { if (giveC4.GetPrimitiveValue<int>() == 0) return false; } catch { }
            try { if (giveC4.GetPrimitiveValue<bool>() == false) return false; } catch { }
        }

        // 檢查 4：防呆讀取你的 mp_free_armor 1 (最準確的刀局特徵)
        var freeArmor = ConVar.Find("mp_free_armor");
        if (freeArmor != null)
        {
            try { if (freeArmor.GetPrimitiveValue<int>() == 1) return false; } catch { }
            try { if (freeArmor.GetPrimitiveValue<bool>() == true) return false; } catch { }
        }

        return true;
    }
    #endregion
}
