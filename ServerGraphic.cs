using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;
using CounterStrikeSharp.API.Modules.Timers; 

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "LINKTOIMAGE";

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 250;

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 35;

    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 1; // 設定檔保留，但內部已改用更智慧的計時器

    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 7.0f;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.15"; // 升級為 1.0.15 (廢除 OnTick 暴力刷新，改用節能計時器)
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    
    private CounterStrikeSharp.API.Modules.Timers.Timer? _delayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _displayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudTimer; // 新增專屬的 UI 刷新計時器

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => 
        {
            bShowingServerGraphic = false;
            ClearAllTimers();
        });
        
        // 【重要修改】：完全刪除了 OnTick，解放伺服器 CPU 效能！
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        currentImageHtml = $"<div style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'><img src='{Config.Image}' style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'></div>";
    }

    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ClearAllTimers();

        _delayTimer = AddTimer(0.5f, () =>
        {
            if (!IsLive()) return;

            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
            {
                if (!gameRulesProxy.GameRules.FreezePeriod) return;
            }

            bShowingServerGraphic = true;

            // 【效能革命】：改用 0.2 秒刷新一次 (每秒 5 次)，取代原本的每秒 64 次。畫面一樣穩，伺服器不再報錯卡頓！
            _hudTimer = AddTimer(0.2f, () => 
            {
                if (!bShowingServerGraphic) return;
                
                // 改用 GetPlayers() 更快，因為它只會抓有玩家的空位，不會像以前一樣跑無效迴圈
                foreach (var player in Utilities.GetPlayers())
                {
                    if (IsPlayerValid(player))
                    {
                        player.PrintToCenterHtml(currentImageHtml);
                    }
                }
            }, TimerFlags.REPEAT);

            _displayTimer = AddTimer(Config.DisplayDuration, () =>
            {
                CloseHUD();
            });
        });

        return HookResult.Continue;
    }

    private void CloseHUD()
    {
        bShowingServerGraphic = false; 
        _hudTimer?.Kill();
        _hudTimer = null;
    }

    private void ClearAllTimers()
    {
        _delayTimer?.Kill();
        _delayTimer = null;

        _displayTimer?.Kill();
        _displayTimer = null;

        _hudTimer?.Kill();
        _hudTimer = null;
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

        var ctSecondary = ConVar.Find("mp_ct_default_secondary");
        if (ctSecondary != null)
        {
            try { if (string.IsNullOrEmpty(ctSecondary.GetPrimitiveValue<string>())) return false; } catch { }
        }

        var tSecondary = ConVar.Find("mp_t_default_secondary");
        if (tSecondary != null)
        {
            try { if (string.IsNullOrEmpty(tSecondary.GetPrimitiveValue<string>())) return false; } catch { }
        }

        return true;
    }
    #endregion
}
