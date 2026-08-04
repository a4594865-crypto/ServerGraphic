using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using System;
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
    public int UpdateTicks { get; set; } = 1; // 保持原樣，由服主自定義

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
    public override string ModuleVersion => "1.1.1"; 
    public override string ModuleAuthor => "unfortunate (Modified)";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    
    private int _tickInterval = 1; 

    private List<CCSPlayerController> _targetPlayers = new List<CCSPlayerController>();
    private bool _isRoundEnd = false; 
    private CCSPlayerController? _lastVictim = null; 

    // --- 快取 ConVar 以提升伺服器效能 (修復點 2) ---
    private ConVar? _cvMaxMoney;
    private ConVar? _cvGiveC4;
    private ConVar? _cvFreeArmor;

    // --- 【計時器管理員】 ---
    private CounterStrikeSharp.API.Modules.Timers.Timer? _knifeDisplayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _roundEndTimer;
    private Dictionary<ulong, CounterStrikeSharp.API.Modules.Timers.Timer> _deathTimers = new();

    public override void Load(bool hotReload)
    {
        // 插件加載時快取 Cvar
        _cvMaxMoney = ConVar.Find("mp_maxmoney");
        _cvGiveC4 = ConVar.Find("mp_give_player_c4");
        _cvFreeArmor = ConVar.Find("mp_free_armor");

        RegisterListener<Listeners.OnMapStart>(map => 
        {
            ResetAllStatesAndTimers();
        });

        // (修復點 3) 監聽玩家斷線，修復 Memory Leak 與無效指標報錯
        RegisterListener<Listeners.OnClientDisconnect>(playerSlot => 
        {
            var player = Utilities.GetPlayerFromSlot(playerSlot);
            if (player != null && player.IsValid)
            {
                RemovePlayerFromHUD(player);
            }
        });

        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic) return;
            if (Server.TickCount % _tickInterval != 0) return; 

            for (int i = _targetPlayers.Count - 1; i >= 0; i--)
            {
                var player = _targetPlayers[i];
                if (player != null && player.IsValid && !player.IsBot)
                {
                    player.PrintToCenterHtml(currentImageHtml);
                }
                else
                {
                    // 若發現無效實體，順手清理
                    _targetPlayers.RemoveAt(i);
                }
            }
            
            if (_targetPlayers.Count == 0) bShowingServerGraphic = false;
        });
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        // 恢復原本邏輯：不大於 0 就強制為 1，否則遵從設定檔
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

        foreach (var timer in _deathTimers.Values)
        {
            timer.Kill();
        }
        _deathTimers.Clear();
    }

    // 輔助方法：乾淨地將玩家移出所有追蹤清單
    private void RemovePlayerFromHUD(CCSPlayerController player)
    {
        if (_targetPlayers.Contains(player))
        {
            _targetPlayers.Remove(player);
        }

        ulong steamId = player.SteamID;
        if (_deathTimers.TryGetValue(steamId, out var timer))
        {
            timer.Kill();
            _deathTimers.Remove(steamId);
        }

        if (_targetPlayers.Count == 0)
        {
            bShowingServerGraphic = false;
        }
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ResetAllStatesAndTimers(); 

        if (IsKnifeRound())
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
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
        var victim = @event.Userid;

        if (victim == null || !victim.IsValid || victim.IsBot || victim.IsHLTV) 
            return HookResult.Continue;

        if (!IsLive()) return HookResult.Continue;

        if (!_targetPlayers.Contains(victim))
        {
            _targetPlayers.Add(victim);
        }
        
        bShowingServerGraphic = true;
        _lastVictim = victim; 
        ulong steamId = victim.SteamID;

        if (_deathTimers.TryGetValue(steamId, out var existingTimer))
        {
            existingTimer.Kill();
        }

        if (_isRoundEnd)
        {
            _roundEndTimer?.Kill();
            _roundEndTimer = AddTimer(Config.RoundEndDisplayDuration, () =>
            {
                RemovePlayerFromHUD(victim);
            });
        }
        else
        {
            _deathTimers[steamId] = AddTimer(Config.DeathDisplayDuration, () =>
            {
                if (_isRoundEnd && _lastVictim == victim) 
                    return; 

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
        
        bool wasLastVictimStillViewing = (_lastVictim != null && _targetPlayers.Contains(_lastVictim));
        
        _targetPlayers.Clear();
        
        foreach (var timer in _deathTimers.Values) timer.Kill();
        _deathTimers.Clear();
        _roundEndTimer?.Kill();
        
        if (wasLastVictimStillViewing && _lastVictim != null && _lastVictim.IsValid)
        {
            _targetPlayers.Add(_lastVictim);
            bShowingServerGraphic = true;

            _roundEndTimer = AddTimer(Config.RoundEndDisplayDuration, () =>
            {
                RemovePlayerFromHUD(_lastVictim);
            });
        }
        else
        {
            bShowingServerGraphic = false;
        }

        return HookResult.Continue;
    }

    #region Helpers (修復點 2 & 4：效能最佳化與防誤傷版)
    private bool IsLive()
    {
        // 取得當前的遊戲規則狀態
        CCSGameRules? rules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity != null)
            {
                rules = entity.GameRules;
                break;
            }
        }

        if (rules != null && rules.WarmupPeriod) return false;

        // 使用快取的 ConVar 避免瞬間負載飆高
        try 
        { 
            if (_cvMaxMoney != null && _cvMaxMoney.GetPrimitiveValue<int>() == 0) return false; 
            if (_cvGiveC4 != null && _cvGiveC4.GetPrimitiveValue<int>() == 0) return false;
            if (_cvFreeArmor != null && _cvFreeArmor.GetPrimitiveValue<int>() == 1) return false;
        } 
        catch { /* 防止轉型失敗報錯 */ }

        // 移除了對 mp_ct_default_secondary 的檢查，避免特殊玩法伺服器失效
        return true;
    }

    private bool IsKnifeRound()
    {
        CCSGameRules? rules = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity != null)
            {
                rules = entity.GameRules;
                break;
            }
        }
        
        if (rules != null && rules.WarmupPeriod) return false;
        
        return !IsLive();
    }
    #endregion
}
