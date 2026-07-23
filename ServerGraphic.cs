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
    public override string ModuleVersion => "1.5.7"; // 真理版：與 FreezePeriod 完美同步
    public override string ModuleAuthor => "unfortunate / Optimized";

    public ServerGraphicConfig Config { get; set; } = new();

    private CCSGameRules? _gameRules;
    private bool _gameRulesInitialized;
    private float _lastRuleCheckTime = 0f;

    // 狀態驅動變數 (取代舊版的 Timer)
    private bool _wasFreezeTime = false;
    private bool _wasShowingGraphic = false;
    private float _centerMessageExpiration = 0f;
    private bool _isManualTest = false;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!_gameRulesInitialized) InitializeGameRules();

            bool isFreezeTime = _gameRules != null && _gameRules.FreezePeriod;
            bool isValidState = !IsWarmup() && !IsPaused() && !IsKnifeRound();

            // 🎯 核心邏輯 1：偵測「凍結時間剛開始」的瞬間，設定圖片壽命
            if (isFreezeTime && !_wasFreezeTime)
            {
                _centerMessageExpiration = Server.CurrentTime + Config.DisplayDuration;
            }
            _wasFreezeTime = isFreezeTime;

            // 🎯 核心邏輯 2：判斷當下是否應該顯示 HUD
            bool isUiActive = false;
            if (Server.CurrentTime <= _centerMessageExpiration)
            {
                if (_isManualTest) 
                {
                    isUiActive = true; // 管理員打指令強制測試
                }
                else if (isFreezeTime && isValidState)
                {
                    isUiActive = true; // 在凍結時間內，且狀態合法
                }
            }
            else
            {
                _isManualTest = false; // 時間到，自動解除測試狀態
            }

            // 🚨 壓制黑框：只要圖片在顯示，且確實處於凍結時間，就壓制引擎黑框
            if (_gameRules != null && isUiActive && isFreezeTime)
            {
                _gameRules.GameRestart = false;
            }

            // ==========================================
            // 效能跳幀處理 (節省伺服器 CPU)
            int tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;
            if (Server.TickCount % tickInterval != 0) return;
            // ==========================================

            // 🎯 核心邏輯 3：發送或清理畫面
            if (isUiActive)
            {
                foreach (var p in Utilities.GetPlayers())
                {
                    if (IsPlayerValid(p)) p.PrintToCenterHtml(Config.HtmlContent);
                }
                _wasShowingGraphic = true;
            }
            else if (_wasShowingGraphic)
            {
                // 當 isUiActive 變成 false (凍結時間結束，或是 5 秒到了)，立刻清空殘影
                foreach (var p in Utilities.GetPlayers())
                {
                    if (IsPlayerValid(p)) p.PrintToCenterHtml("");
                }
                _wasShowingGraphic = false;
            }
        });
    }

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading +++ (v1.5.7)");
        
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);

        AddCommand("css_testhud", "Test HUD", (player, info) =>
        {
            _isManualTest = true;
            _centerMessageExpiration = Server.CurrentTime + Config.DisplayDuration;
        });

        if (hotReload)
        {
            Server.NextFrame(InitializeGameRules);
        }
        
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }

    private void OnMapStartHandler(string mapName)
    {
        _gameRules = null;
        _gameRulesInitialized = false;
        _lastRuleCheckTime = 0f;
        
        _wasFreezeTime = false;
        _wasShowingGraphic = false;
        _centerMessageExpiration = 0f;
        _isManualTest = false;
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
