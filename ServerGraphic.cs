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

    // [保留] .NET 現代語法：集合表達式 []，不影響效能
    private List<CCSPlayerController> _targetPlayers = [];
    private bool _isRoundEnd = false; 
    private CCSPlayerController? _lastVictim = null; 

    private ConVar? _cvMaxMoney;
    private ConVar? _cvGiveC4;
    private ConVar? _cvFreeArmor;

    private CounterStrikeSharp.API.Modules.Timers.Timer? _knifeDisplayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _roundEndTimer;
    
    private Dictionary<ulong, CounterStrikeSharp.API.Modules.Timers.Timer> _deathTimers = [];

    public override void Load(bool hotReload)
    {
        _cvMaxMoney = ConVar.Find("mp_maxmoney");
        _cvGiveC4 = ConVar.Find("mp_give_player_c4");
        _cvFreeArmor = ConVar.Find("mp_free_armor");

        RegisterListener<Listeners.OnMapStart>(map => ResetAllStatesAndTimers());

        RegisterListener<Listeners.OnClientDisconnect>(playerSlot => 
        {
            if (Utilities.GetPlayerFromSlot(playerSlot) is { IsValid: true } player)
            {
                RemovePlayerFromHUD(player);
            }
        });

        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic || Server.TickCount % _tickInterval != 0) return; 

            for (int i = _targetPlayers.Count - 1; i >= 0; i--)
            {
                // [保留] 屬性模式匹配：底層效能與普通 if 判斷一樣快，但代碼更乾淨
                if (_targetPlayers[i] is { IsValid: true, IsBot: false } player)
                {
                    player.PrintToCenterHtml(currentImageHtml);
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
            // [退回] 拔除 LINQ，換回最高效能的傳統 foreach 迴圈，拒絕產生多餘的 GC
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
            _roundEndTimer?.Kill();
            _roundEndTimer = AddTimer(Config.RoundEndDisplayDuration, () => RemovePlayerFromHUD(victim));
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
            _targetPlayers.Add(_lastVictim);
            bShowingServerGraphic = true;

            _roundEndTimer = AddTimer(Config.RoundEndDisplayDuration, () => RemovePlayerFromHUD(_lastVictim));
        }
        else
        {
            bShowingServerGraphic = false;
        }

        return HookResult.Continue;
    }

    #region Helpers (效能與可讀性兼具版)
    // 獨立出一個高效率找 GameRules 的方法，避免程式碼重複
    private CCSGameRules? GetGameRules()
    {
        // [退回] 拔除 LINQ，使用原生的 foreach，這是獲取實體效能最好的方式
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            // 【.NET 10 升級】：更深層的屬性模式匹配
            if (entity is { GameRules: not null } proxy) return proxy.GameRules;
        }
        return null;
    }

    private bool IsLive()
    {
        if (GetGameRules() is { WarmupPeriod: true }) return false;

        try 
        { 
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
