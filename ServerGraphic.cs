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
        currentImageHtml = $"<img src='{Config.Image}' width='{Config.ImageWidth}' height='{Config.ImageHeight}'>";

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
        if (!IsLive())
        {
            return HookResult.Continue;
        }

        AddTimer(1.2f, () =>
        {
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

   // 將清除 HUD 的邏輯獨立為一個方法，方便呼叫
    private void CloseHUD()
    {
        // 僅關閉布林值，讓 OnTick 停止每秒重新投影圖片
        bShowingServerGraphic = false; 

        // 這裡不再使用 foreach 去發送任何字串
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
