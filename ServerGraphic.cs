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
    public string Image { get; set; } = "LINKTOIMAGE";[cite: 1]

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 600;[cite: 1]

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 120;[cite: 1]

    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 8;[cite: 1]

    // 新增：自定義顯示秒數 (例如設定為 5 秒)
    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";[cite: 1]
    public override string ModuleVersion => "1.0.9"; // 升級版本號以供辨識
    public override string ModuleAuthor => "unfortunate";[cite: 1]

    public ServerGraphicConfig Config { get; set; } = new();[cite: 1]
    public bool bShowingServerGraphic = false;[cite: 1]
    private string currentImageHtml = "";[cite: 1]

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading +++ ");[cite: 1]
        RegisterListener<Listeners.OnMapStart>(map => bShowingServerGraphic = false);[cite: 1]
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");[cite: 1]
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;[cite: 1]
        currentImageHtml = $"<img src='{Config.Image}' width='{Config.ImageWidth}' height='{Config.ImageHeight}'>";[cite: 1]

        RegisterListener<Listeners.OnTick>(() =>[cite: 1]
        {
            if (!bShowingServerGraphic) return;[cite: 1]

            int tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;[cite: 1]
            if (Server.TickCount % tickInterval != 0) return;[cite: 1]

            foreach (var player in Utilities.GetPlayers())[cite: 1]
            {
                if (IsPlayerValid(player))[cite: 1]
                {
                    player.PrintToCenterHtml(currentImageHtml);[cite: 1]
                }
            }
        });[cite: 1]
    }

    // 🟢 事件 1：回合開始（保留過濾假畫面的機制）
    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (!IsLive())[cite: 1]
        {
            Logger.LogInformation("[ServerGraphic] 偵測為暖身或刀局，不顯示 HUD。");[cite: 1]
            return HookResult.Continue;[cite: 1]
        }

        Logger.LogInformation("[ServerGraphic] 準備進入 Live 局，等待 1.2 秒過濾 restart 過渡期...");[cite: 1]

        AddTimer(1.2f, () =>[cite: 1]
        {
            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();[cite: 1]
            if (gameRulesProxy != null && gameRulesProxy.GameRules != null)[cite: 1]
            {
                if (!gameRulesProxy.GameRules.FreezePeriod)[cite: 1]
                {
                    Logger.LogInformation("[ServerGraphic] 偵測到 mp_restartgame 過渡期，略過 HUD 顯示。");[cite: 1]
                    return;[cite: 1]
                }
            }

            Logger.LogInformation($"[ServerGraphic] 真實 Live 局凍結時間確認，啟動 HUD。預計顯示 {Config.DisplayDuration} 秒。");[cite: 1]
            bShowingServerGraphic = true;[cite: 1]

            // 【新增核心邏輯】：依照設定的秒數，時間到自動關閉 HUD
            AddTimer(Config.DisplayDuration, () =>
            {
                if (bShowingServerGraphic)
                {
                    Logger.LogInformation("[ServerGraphic] 設定的顯示時間結束，關閉 HUD 並清除黑框。");
                    CloseHUD();
                }
            });
        });[cite: 1]

        return HookResult.Continue;[cite: 1]
    }

    // 將清除 HUD 的邏輯獨立為一個方法，方便呼叫
    private void CloseHUD()
    {
        bShowingServerGraphic = false; 
        foreach (var player in Utilities.GetPlayers()) 
        {
            if (IsPlayerValid(player)) 
            {
                player.PrintToCenter(" "); 
            }
        }
    }

    // ⚠️ 注意：已經將 OnEventRoundFreezeEnd 刪除！
    // 這樣凍結時間結束時就不會強制關掉圖片了。

    #region Helpers
    public static bool IsPlayerValid(CCSPlayerController? player)[cite: 1]
    {
        return player != null[cite: 1]
            && player.IsValid[cite: 1]
            && !player.IsBot[cite: 1]
            && !player.IsHLTV[cite: 1]
            && player.PlayerPawn != null[cite: 1]
            && player.PlayerPawn.IsValid[cite: 1]
            && player.PlayerPawn.Value != null[cite: 1]
            && player.PlayerPawn.Value.IsValid;[cite: 1]
    }

    private bool IsLive()[cite: 1]
    {
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();[cite: 1]
        if (gameRulesProxy != null && gameRulesProxy.GameRules != null)[cite: 1]
        {
            if (gameRulesProxy.GameRules.WarmupPeriod) return false;[cite: 1]
        }

        var maxMoney = ConVar.Find("mp_maxmoney");[cite: 1]
        if (maxMoney != null)[cite: 1]
        {
            try { if (maxMoney.GetPrimitiveValue<int>() == 0) return false; } catch { }[cite: 1]
        }

        var giveC4 = ConVar.Find("mp_give_player_c4");[cite: 1]
        if (giveC4 != null)[cite: 1]
        {
            try { if (giveC4.GetPrimitiveValue<int>() == 0) return false; } catch { }[cite: 1]
            try { if (giveC4.GetPrimitiveValue<bool>() == false) return false; } catch { }[cite: 1]
        }

        var freeArmor = ConVar.Find("mp_free_armor");[cite: 1]
        if (freeArmor != null)[cite: 1]
        {
            try { if (freeArmor.GetPrimitiveValue<int>() == 1) return false; } catch { }[cite: 1]
            try { if (freeArmor.GetPrimitiveValue<bool>() == true) return false; } catch { }[cite: 1]
        }

        return true;[cite: 1]
    }
    #endregion
}
