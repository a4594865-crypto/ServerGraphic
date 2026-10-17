using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using System.Collections.Generic;
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
    public int UpdateTicks { get; set; } = 1; 

    [JsonPropertyName("DeathDisplayDuration")]
    public float DeathDisplayDuration { get; set; } = 2.5f; 

    [JsonPropertyName("RoundEndDisplayDuration")]
    public float RoundEndDisplayDuration { get; set; } = 5.0f; 

    [JsonPropertyName("KnifeRoundDisplayDuration")]
    public float KnifeRoundDisplayDuration { get; set; } = 5.0f; 
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.1.3-Strict-Perf"; 
    public override string ModuleAuthor => "unfortunate (Strict Perf Optimized)";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    
    private int _tickInterval = 1; 

    // 【0-GC 優化】：預先配置容量 (16)，避免正賽擊殺瞬間發生 List / Dictionary 底層陣列擴容
    private readonly List<CCSPlayerController> _targetPlayers = new(16);
    private bool _isRoundEnd = false; 
    private CCSPlayerController? _lastVictim = null; 

    private ConVar? _cvMaxMoney;
    private ConVar? _cvGiveC4;
    private ConVar? _cvFreeArmor;

    private CounterStrikeSharp.API.Modules.Timers.Timer? _knifeDisplayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _roundEndTimer;
    
    private readonly Dictionary<ulong, CounterStrikeSharp.API.Modules.Timers.Timer> _deathTimers = new(16);

    // 【0-GC 安全快取】：快取 CCSGameRulesProxy 實體，兼具 IsValid 記憶體安全驗證與 0 全圖掃描
    private CCSGameRulesProxy? _cachedGameRulesProxy = null;

    public override void Load(bool hotReload)
    {
        _cvMaxMoney = ConVar.Find("mp_maxmoney");
        _cvGiveC4 = ConVar.Find("mp_give_player_c4");
        _cvFreeArmor = ConVar.Find("mp_free_armor");

        RegisterListener<Listeners.OnMapStart>(map => 
        {
            ResetAllStatesAndTimers();
            _cachedGameRulesProxy = null; // 換地圖時清空快取
        });

        RegisterListener<Listeners.OnClientDisconnect>(playerSlot => 
        {
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            if (player is not null)
            {
                RemovePlayerFromHUD(player);
            }
        });

        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic) return;

            // 【防呆修復】：如果伺服器目前處於暫停狀態（連動 MatchZy 的 isPaused 或官方暫停），強制關閉圖片渲染，防止解除暫停後 OnTick 暴走卡死 FPS
            if (MatchZy.isPaused || (GetGameRules() is { IsPaused: true }))
            {
                bShowingServerGraphic = false;
                return;
            }

            if (_tickInterval > 1 && Server.TickCount % _tickInterval != 0) return; 

            int count = _targetPlayers.Count;
            if (count == 0)
            {
                bShowingServerGraphic = false;
                return;
            }

            string html = currentImageHtml;
            for (int i = count - 1; i >= 0; i--)
            {
                // 屬性模式匹配：底層效能與普通 if 判斷一樣快，但代碼更乾淨
                if (_targetPlayers[i] is { IsValid: true, IsBot: false } player)
                {
                    player.PrintToCenterHtml(html);
                }
                else
                {
                    _targetPlayers.RemoveAt(i);
                }
            }
            
            if (_targetPlayers.Count is 0) bShowingServerGraphic = false;
        });
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        _tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks; 
        
        currentImageHtml = $"<div style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'><img src='{Config.Image}' style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'></div>";
    }

    private void ResetAllStatesAndTimers()
    {
        _isRoundEnd = false;
        _lastVictim = null;
        bShowingServerGraphic = false;
        _targetPlayers.Clear();

        _knifeDisplayTimer?.Kill(); _knifeDisplayTimer = null;
        _roundEndTimer?.Kill(); _roundEndTimer = null;

        foreach (var timer in _deathTimers.Values) timer.Kill();
        _deathTimers.Clear();
    }

    private void RemovePlayerFromHUD(CCSPlayerController player)
    {
        _targetPlayers.Remove(player);

        if (_deathTimers.Remove(player.SteamID, out var timer))
        {
            timer.Kill();
        }

        if (_targetPlayers.Count is 0) bShowingServerGraphic = false;
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ResetAllStatesAndTimers(); 

        if (IsKnifeRound())
        {
            // 拔除 LINQ，使用最高效能的傳統 foreach 迴圈，拒絕產生多餘的 GC
            foreach (var player in Utilities.GetPlayers())
            {
                if (player is { IsValid: true, IsBot: false, IsHLTV: false })
                {
                    _targetPlayers.Add(player);
                }
            }

            if (_targetPlayers.Count > 0)
            {
                bShowingServerGraphic = true;
                _knifeDisplayTimer = AddTimer(Config.KnifeRoundDisplayDuration, () => 
                {
                    _targetPlayers.Clear();
                    bShowingServerGraphic = false;
                });
            }
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (@event.Userid is not { IsValid: true, IsBot: false, IsHLTV: false } victim) 
            return HookResult.Continue;

        if (!IsLive()) return HookResult.Continue;

        if (!_targetPlayers.Contains(victim))
        {
            _targetPlayers.Add(victim);
        }
        
        bShowingServerGraphic = true;
        _lastVictim = victim; 
        ulong steamId = victim.SteamID;

        if (_deathTimers.Remove(steamId, out var existingTimer))
        {
            existingTimer.Kill();
        }

        if (_isRoundEnd)
        {
            // 【修復】：使用各自的 _deathTimers[steamId]，防止回合結束後多人同時死亡(如C4爆炸)時互相覆蓋計時器導致圖片卡死
            _deathTimers[steamId] = AddTimer(Config.RoundEndDisplayDuration, () => RemovePlayerFromHUD(victim));
        }
        else
        {
            _deathTimers[steamId] = AddTimer(Config.DeathDisplayDuration, () =>
            {
                if (_isRoundEnd && _lastVictim == victim) return; 
                RemovePlayerFromHUD(victim);
            });
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!IsLive()) return HookResult.Continue;

        _isRoundEnd = true; 
        bool wasLastVictimStillViewing = _lastVictim is not null && _targetPlayers.Contains(_lastVictim);
        
        _targetPlayers.Clear();
        foreach (var timer in _deathTimers.Values) timer.Kill();
        _deathTimers.Clear();
        _roundEndTimer?.Kill();
        
        if (wasLastVictimStillViewing && _lastVictim is { IsValid: true })
        {
            CCSPlayerController targetVictim = _lastVictim;
            _targetPlayers.Add(targetVictim);
            bShowingServerGraphic = true;

            _roundEndTimer = AddTimer(Config.RoundEndDisplayDuration, () => RemovePlayerFromHUD(targetVictim));
        }
        else
        {
            bShowingServerGraphic = false;
        }

        return HookResult.Continue;
    }

    #region Helpers (0-GC 效能與安全快取版)
    private CCSGameRules? GetGameRules()
    {
        if (_cachedGameRulesProxy is { IsValid: true, GameRules: not null })
        {
            return _cachedGameRulesProxy.GameRules;
        }

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity is { IsValid: true, GameRules: not null } proxy)
            {
                _cachedGameRulesProxy = proxy;
                return proxy.GameRules;
            }
        }
        return null;
    }

    private bool IsLive()
    {
        if (GetGameRules() is { WarmupPeriod: true }) return false;

        try 
        { 
            _cvMaxMoney ??= ConVar.Find("mp_maxmoney");
            _cvGiveC4 ??= ConVar.Find("mp_give_player_c4");
            _cvFreeArmor ??= ConVar.Find("mp_free_armor");

            if (_cvMaxMoney?.GetPrimitiveValue<int>() is 0) return false; 
            if (_cvGiveC4?.GetPrimitiveValue<int>() is 0) return false;
            if (_cvFreeArmor?.GetPrimitiveValue<int>() is 1) return false;
        } 
        catch { /* 防止轉型失敗 */ }

        return true;
    }

    private bool IsKnifeRound()
    {
        if (GetGameRules() is { WarmupPeriod: true }) return false;
        
        return !IsLive();
    }
    #endregion
}
