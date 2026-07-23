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

    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f; 
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.4.7"; // 賽事級極致優化版
    public override string ModuleAuthor => "unfortunate / Optimized";

    public ServerGraphicConfig Config { get; set; } = new();

    public bool bShowingServerGraphic = false;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hideTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _checkDelayTimer;
    
    private CCSGameRules? _gameRules;
    private bool _gameRulesInitialized;
    
    // 新增：用於防止 OnTick 瘋狂掃描的冷卻時間戳記
    private float _lastRuleCheckTime = 0f;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!_gameRulesInitialized) InitializeGameRules();

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
            
            if (_hideTimer != null) _hideTimer.Kill();
            
            // 安全的計時器寫法：自然結束時先清空指標，避免 Stop 誤殺自己
            _hideTimer = AddTimer(Config.DisplayDuration, () => {
                _hideTimer = null;
                StopShowingGraphic();
            }); 
        });

        if (hotReload)
        {
            Server.NextFrame(InitializeGameRules);
        }
        
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }

    private void OnMapStartHandler(string mapName)
    {
        StopShowingGraphic();
        _gameRules = null;
        _gameRulesInitialized = false;
        _lastRuleCheckTime = 0f;
    }

    private void InitializeGameRules()
    {
        if (_gameRulesInitialized) return;

        // 【致命效能修復】限制每 1 秒才去全服搜索一次，絕對不允許 1 秒 64 次的瘋狂掃描
        if (Server.CurrentTime - _lastRuleCheckTime < 1.0f) return;
        _lastRuleCheckTime = Server.CurrentTime;

        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        _gameRules = gameRulesProxy?.GameRules;
        _gameRulesInitialized = _gameRules != null;
    }

    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_checkDelayTimer != null)
        {
            _checkDelayTimer.Kill();
            _checkDelayTimer = null;
        }

        _checkDelayTimer = AddTimer(0.2f, () =>
        {
            _checkDelayTimer = null;

            if (IsWarmup() || IsPaused() || IsKnifeRound())
            {
                StopShowingGraphic();
                return;
            }

            bShowingServerGraphic = true;
            
            if (_hideTimer != null) _hideTimer.Kill();
            
            // 安全的計時器寫法
            _hideTimer = AddTimer(Config.DisplayDuration, () => {
                _hideTimer = null;
                StopShowingGraphic();
            });
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
        if (bShowingServerGraphic)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (IsPlayerValid(player))
                {
                    player.PrintToCenterHtml(""); 
                }
            }
        }

        bShowingServerGraphic = false;
        
        // 【框架報錯修復】僅在外部中斷時才執行 Kill，避免計時器自我毀滅
        if (_checkDelayTimer != null)
        {
            _checkDelayTimer.Kill();
            _checkDelayTimer = null;
        }

        if (_hideTimer != null)
        {
            _hideTimer.Kill();
            _hideTimer = null;
        }
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

    private bool IsWarmup()
    {
        return _gameRules != null && _gameRules.WarmupPeriod;
    }

    private bool IsPaused()
    {
        if (_gameRules != null)
        {
            return _gameRules.MatchWaitingForResume || 
                   _gameRules.TerroristTimeOutActive || 
                   _gameRules.CTTimeOutActive;
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
