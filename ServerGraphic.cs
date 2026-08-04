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
    public override string ModuleVersion => "1.0.31"; // 升級 1.0.31：加入過渡鎖，徹底解決打完 .stay 進入正式局時的誤判
    public override string ModuleAuthor => "unfortunate (Modified)";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    
    private int _tickInterval = 1; 

    private List<CCSPlayerController> _targetPlayers = new List<CCSPlayerController>();
    private bool _isRoundEnd = false; 
    private CCSPlayerController? _lastVictim = null; 

    // 計時器管理員
    private CounterStrikeSharp.API.Modules.Timers.Timer? _knifeDisplayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _roundEndTimer;
    private Dictionary<ulong, CounterStrikeSharp.API.Modules.Timers.Timer> _deathTimers = new();

    // 狀態追蹤
    private bool _wasInKnifeRound = false;
    
    // --- 新增：用來鎖住「刀局剛結束、準備透過 .stay/.swap 進入正式局」的過渡鎖 ---
    private bool _isPendingLiveAfterKnife = false;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => 
        {
            ResetAllStatesAndTimers();
            _wasInKnifeRound = false;
            _isPendingLiveAfterKnife = false;
        });

        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic) return;
            if (Server.TickCount % _tickInterval != 0) return;

            for (int i = _targetPlayers.Count - 1; i >= 0; i--)
            {
                var player = _targetPlayers[i];
                if (player != null && player.IsValid)
                {
                    player.PrintToCenterHtml(currentImageHtml);
                }
            }
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

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ResetAllStatesAndTimers(); 

        // 【關鍵防線】：如果我們剛打完刀局，現在正準備因 .stay / .swap 重啟進入正式局
        if (_isPendingLiveAfterKnife)
        {
            _isPendingLiveAfterKnife = false; // 解鎖
            return HookResult.Continue; // 直接跳過，絕對不顯示 HUD！
        }

        bool currentIsKnife = IsKnifeRound();

        if (currentIsKnife && !_wasInKnifeRound)
        {
            _wasInKnifeRound = true; 

            _targetPlayers.Clear();
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
                    bShowingServerGraphic = false;
                    _targetPlayers.Clear();
                });
            }
        }
        else if (!currentIsKnife)
        {
            _wasInKnifeRound = false;
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
                if (_targetPlayers.Contains(victim)) _targetPlayers.Remove(victim);
                if (_targetPlayers.Count == 0) bShowingServerGraphic = false;
            });
        }
        else
        {
            _deathTimers[steamId] = AddTimer(Config.DeathDisplayDuration, () =>
            {
                if (_isRoundEnd && _lastVictim == victim) 
                    return; 

                if (_targetPlayers.Contains(victim)) _targetPlayers.Remove(victim);
                if (_targetPlayers.Count == 0) bShowingServerGraphic = false;

                _deathTimers.Remove(steamId);
            });
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        // 如果當前正在打刀局，當刀局結束的那一刻，啟動「準備進入正式局」的過渡鎖！
        if (_wasInKnifeRound)
        {
            _isPendingLiveAfterKnife = true;
            _wasInKnifeRound = false;
        }

        if (!IsLive()) return HookResult.Continue;

        _isRoundEnd = true; 
        
        bool wasLastVictimStillViewing = false;
        if (_lastVictim != null && _targetPlayers.Contains(_lastVictim))
        {
            wasLastVictimStillViewing = true;
        }
        
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
                if (_targetPlayers.Contains(_lastVictim)) _targetPlayers.Remove(_lastVictim);
                if (_targetPlayers.Count == 0) bShowingServerGraphic = false;
            });
        }
        else
        {
            bShowingServerGraphic = false;
        }

        return HookResult.Continue;
    }

    #region Helpers
    private bool IsLive()
    {
        CCSGameRulesProxy? gameRulesProxy = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            gameRulesProxy = entity;
            break; 
        }

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

    private bool IsKnifeRound()
    {
        bool isWarmup = false;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity != null && entity.GameRules != null)
            {
                isWarmup = entity.GameRules.WarmupPeriod;
                break;
            }
        }
        
        if (isWarmup) return false;
        return !IsLive();
    }
    #endregion
}
