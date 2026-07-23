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
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.4.1"; // 防閃現延遲判定版
    public override string ModuleAuthor => "unfortunate / Optimized";

    public ServerGraphicConfig Config { get; set; } = new();

    public int iMpFreezeTimemp;
    public bool bShowingServerGraphic = false;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hideTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _checkDelayTimer; // 新增延遲判定計時器

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
            _hideTimer = AddTimer(5.0f, StopShowingGraphic); 
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

        // 【關鍵修改】延遲 0.2 秒再判定，等待 MatchZy 的 mp_restartgame 1 跑完，避免參數切換時產生「閃現」
        _checkDelayTimer = AddTimer(0.2f, () =>
        {
            // 如果 0.2 秒後確認是 暖身、暫停 或 刀局，直接中斷不顯示
            if (IsWarmup() || IsPaused() || IsKnifeRound())
            {
                StopShowingGraphic();
                return;
            }

            iMpFreezeTimemp = ConVar.Find("mp_freezetime")!.GetPrimitiveValue<int>();
            
            bShowingServerGraphic = true;
            _hideTimer?.Kill();
            _hideTimer = AddTimer(iMpFreezeTimemp, StopShowingGraphic);
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
            // 1. 檢查 C4 (你設定檔裡的 mp_give_player_c4 0)
            var giveC4ConVar = ConVar.Find("mp_give_player_c4");
            if (giveC4ConVar != null)
            {
                try { if (giveC4ConVar.GetPrimitiveValue<bool>() == false) return true; } catch { }
                try { if (giveC4ConVar.GetPrimitiveValue<int>() == 0) return true; } catch { }
            }

            // 2. 【新增】檢查最大金錢 (你設定檔裡的 mp_maxmoney 0)
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
