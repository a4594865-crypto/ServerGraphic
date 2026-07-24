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

    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 8;

    // 新增：自定義顯示秒數 (例如設定為 5 秒)
    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.9"; // 升級版本號以供辨識
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => bShowingServerGraphic = false);
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        // 【修正 1】：改為使用 CSS style 屬性，確保 Panorama UI 能正確鎖定圖片比例
        currentImageHtml = $"<img src='{Config.Image}' style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'>";

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

    // 🟢 事件 1：回合開始（保留過濾假畫面的機制）
    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        AddTimer(0.5f, () =>
        {
            // 【修正 2】：將 IsLive() 移進 Timer 內。
            // 讓伺服器與比賽插件有 0.5 秒的時間處理刀局 C4 與護甲設定，徹底解決第一局誤判！
            if (!IsLive())
            {
                return;
            }

            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
            {
                if (!gameRulesProxy.GameRules.FreezePeriod)
                {
                    return;
                }
            }

            bShowingServerGraphic = true;

            // 【新增核心邏輯】：依照設定的秒數，時間到自動關閉 HUD
            AddTimer(Config.DisplayDuration, () =>
            {
                if (bShowingServerGraphic)
                {
                    CloseHUD();
                }
            });
        });

        return HookResult.Continue;
    }

    // 🟢 事件 2：玩家重生（解決離線重進或中途加入的殘留問題）
    [GameEventHandler]
    public HookResult OnEventPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;

        // 【修正 3】：如果玩家有效，且當前「不是」顯示 HUD 的時間
        // 就發送空字串，徹底清空該玩家剛連線時，伺服器塞給他的殘留記憶
        if (IsPlayerValid(player) && !bShowingServerGraphic)
        {
            player.PrintToCenterHtml("");
        }

        return HookResult.Continue;
    }

    // 將清除 HUD 的邏輯獨立為一個方法，方便呼叫
    private void CloseHUD()
    {
        // 僅關閉布林值，讓 OnTick 停止每秒重新投影圖片
        bShowingServerGraphic = false; 

        // 交給 CS2 引擎自己把圖片跟黑框一起平滑淡出
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
            if (gameRulesProxy.GameRules.WarmupPeriod) return false;
        }

        var maxMoney = ConVar.Find("mp_maxmoney");
        if (maxMoney != null)
        {
            try { if (maxMoney.GetPrimitiveValue<int>() == 0) return false; } catch { }
        }

        var giveC4 = ConVar.Find("mp_give_player_c4");
        if (giveC4 != null)
        {
            try { if (giveC4.GetPrimitiveValue<int>() == 0) return false; } catch { }
            try { if (giveC4.GetPrimitiveValue<bool>() == false) return false; } catch { }
        }

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
