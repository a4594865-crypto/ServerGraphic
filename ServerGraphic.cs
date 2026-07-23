using System;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Text.Json.Serialization;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("HtmlContent")]
    public string HtmlContent { get; set; } = "<img src='https://cdn.jsdelivr.net/gh/a4594865-crypto/ServerGraphic@main/images/logo2.png' width='600' height='120'>";

    // 【加回來了】讓你可以自由在 .json 裡面設定想要的秒數！
    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f; 
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.4.2"; // 恢復自訂秒數版
    public override string ModuleAuthor => "unfortunate / Optimized";

    public ServerGraphicConfig Config { get; set; } = new();

    public bool bShowingServerGraphic = false;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hideTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _checkDelayTimer;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (bShowingServerGraphic) 
            {
                if (IsPaused())
                {
                    StopShowingGraphic();
                    return;
                }

                foreach (var player in Utilities.GetPlayers())
                {
                    if (!IsPlayerValid(player))
                        continue;

                    player.PrintToCenterHtml(Config.HtmlContent);
                }
            }
        });
    }

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading +++ ");
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);

        AddCommand("css_testhud", "Test HUD", (player, info) =>
        {
            bShowingServerGraphic = true;
            _hideTimer?.Kill();
            _hideTimer = AddTimer(Config.DisplayDuration, StopShowingGraphic); // 測試指令也吃設定檔秒數
        });
        
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }

    private void OnMapStartHandler(string mapName)
    {
        StopShowingGraphic();
    }

    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _checkDelayTimer?.Kill();

        _checkDelayTimer = AddTimer(0.2f, () =>
        {
            if (IsWarmup() || IsPaused() || IsKnifeRound())
            {
                StopShowingGraphic();
                return;
            }

            bShowingServerGraphic = true;
            _hideTimer?.Kill();

            // 【關鍵修改】不再強制抓 mp_freezetime，而是使用你設定檔裡的 DisplayDuration！
            _hideTimer = AddTimer(Config.DisplayDuration, StopShowingGraphic);
        });

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnEventRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        StopShowingGraphic();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnEventRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        StopShowingGraphic();
        return HookResult.Continue;
    }

    private void StopShowingGraphic()
    {
        bShowingServerGraphic = false;
        
        _checkDelayTimer?.Kill();
        _checkDelayTimer = null;

        _hideTimer?.Kill();
        _hideTimer = null;
    }

    private bool IsWarmup()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        return gameRules != null && gameRules.WarmupPeriod;
    }

    private bool IsPaused()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gameRules != null)
        {
            return gameRules.MatchWaitingForResume || 
                   gameRules.TerroristTimeOutActive || 
                   gameRules.CTTimeOutActive;
        }
        return false;
    }

    private bool IsKnifeRound()
    {
        try
        {
            var giveC4ConVar = ConVar.Find("mp_give_player_c4");
            if (giveC4ConVar != null)
            {
                try { if (giveC4ConVar.GetPrimitiveValue<bool>() == false) return true; } catch { }
                try { if (giveC4ConVar.GetPrimitiveValue<int>() == 0) return true; } catch { }
            }

            var maxMoneyConVar = ConVar.Find("mp_maxmoney");
            if (maxMoneyConVar != null)
            {
                try { if (maxMoneyConVar.GetPrimitiveValue<int>() == 0) return true; } catch { }
            }
        }
        catch {}
        
        return false;
    }
    #endregion
}
