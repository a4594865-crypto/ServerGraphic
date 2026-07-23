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

    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 8; 
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.5.3"; // 修正跳幀邏輯：徹底壓制黑框版
    public override string ModuleAuthor => "unfortunate / Optimized";

    public ServerGraphicConfig Config { get; set; } = new();

    private string _activeCenterMessage = "";
    private float _centerMessageExpiration = 0f;

    private CounterStrikeSharp.API.Modules.Timers.Timer? _checkDelayTimer;
    
    private CCSGameRules? _gameRules;
    private bool _gameRulesInitialized;
    private float _lastRuleCheckTime = 0f;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!_gameRulesInitialized) InitializeGameRules();

            // 先判斷當前時間點，是否「應該要顯示 UI」
            bool isUiActive = !string.IsNullOrEmpty(_activeCenterMessage) && (Server.CurrentTime <= _centerMessageExpiration);
            bool isPaused = IsPaused();

            // 🚨 步驟一：【消除黑框的底層壓制】
            // 這個區塊必須「每個 Tick」都執行，絕對不能跳幀，否則引擎黑框會趁虛而入！
            if (_gameRules != null)
            {
                if (isUiActive && !isPaused)
                {
                    _gameRules.GameRestart = _gameRules.RestartRoundTime < Server.CurrentTime;
                }
                else
                {
                    _gameRules.GameRestart = false; // 強制將黑框關閉
                }
            }

            // ==========================================
            // 🚨 步驟二：【發送 HTML 封包給玩家】 (這非常耗 CPU，所以在這裡攔截跳幀)
            int tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;
            if (Server.TickCount % tickInterval != 0) return;
            // ==========================================

            if (isUiActive)
            {
                if (isPaused)
                {
                    StopShowingGraphic();
                    return;
                }

                foreach (var p in Utilities.GetPlayers())
                {
                    if (IsPlayerValid(p)) p.PrintToCenterHtml(_activeCenterMessage);
                }
            }
            else if (!string.IsNullOrEmpty(_activeCenterMessage))
            {
                // 時間到了，呼叫清理方法把殘影洗掉
                StopShowingGraphic();
            }
        });
    }

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading +++ (v1.5.3)");
        
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);

        AddCommand("css_testhud", "Test HUD", (player, info) =>
        {
            ShowHud(Config.HtmlContent, Config.DisplayDuration);
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

        if (Server.CurrentTime - _lastRuleCheckTime < 1.0f) return;
        _lastRuleCheckTime = Server.CurrentTime;

        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        _gameRules = gameRulesProxy?.GameRules;
        _gameRulesInitialized = _gameRules != null;
    }

    private void ShowHud(string html, float duration)
    {
        _activeCenterMessage = html;
        _centerMessageExpiration = Server.CurrentTime + duration;
    }

    private void StopShowingGraphic()
    {
        if (!string.IsNullOrEmpty(_activeCenterMessage))
        {
            _activeCenterMessage = "";
            foreach (var p in Utilities.GetPlayers())
            {
                if (IsPlayerValid(p)) p.PrintToCenterHtml(""); 
            }
        }
        
        // 保險機制：在主動關閉時，立刻強制消除黑框
        if (_gameRules != null)
        {
            _gameRules.GameRestart = false;
        }
        
        if (_checkDelayTimer != null)
        {
            _checkDelayTimer.Kill();
            _checkDelayTimer = null;
        }
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

            ShowHud(Config.HtmlContent, Config.DisplayDuration);
        });

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnEventRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        StopShowingGraphic();
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
