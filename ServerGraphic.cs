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
    public override string ModuleVersion => "1.4.5"; // 採用 LiteMatch 的 GameRules 快取優化版
    public override string ModuleAuthor => "unfortunate / Optimized";

    public ServerGraphicConfig Config { get; set; } = new();

    public bool bShowingServerGraphic = false;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hideTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _checkDelayTimer;
    
    // 借鑒 LiteMatchManager 的快取機制，極大化提升 OnTick 效能
    private CCSGameRules? _gameRules;
    private bool _gameRulesInitialized;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        RegisterListener<Listeners.OnTick>(() =>
        {
            // 每一幀檢查：如果還沒抓過遊戲規則，才去抓一次
            if (!_gameRulesInitialized) InitializeGameRules();

            if (bShowingServerGraphic) 
            {
                // 現在 IsPaused() 直接讀取記憶體快取，瞬間完成，0 效能負擔！
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
            _hideTimer = AddTimer(Config.DisplayDuration, StopShowingGraphic); 
        });

        // 如果是熱重載 (Hot Reload)，立刻初始化規則
        if (hotReload)
        {
            InitializeGameRules();
        }
        
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }

    private void OnMapStartHandler(string mapName)
    {
        StopShowingGraphic();
        
        // 借鑒 LiteMatchManager：換地圖時清空快取，讓 OnTick 重新抓取
        _gameRules = null;
        _gameRulesInitialized = false;
    }

    // 將全服搜索獨立出來，只執行一次
    private void InitializeGameRules()
    {
        if (_gameRulesInitialized) return;
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        _gameRules = gameRulesProxy?.GameRules;
        _gameRulesInitialized = _gameRules != null;
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

    #region Helpers
    public static bool IsPlayerValid(CCSPlayerController? player)
    {
        return player != null
            && player.IsValid
            && !player.IsBot
            && player.Pawn != null
            && player.Pawn.IsValid
            && !player.IsHLTV;
    }

    private bool IsWarmup()
    {
        // 直接使用快取的 _gameRules，不用再搜尋了！
        return _gameRules != null && _gameRules.WarmupPeriod;
    }

    private bool IsPaused()
    {
        // 直接使用快取的 _gameRules，不用再搜尋了！
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
