using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using System;
using System.Collections.Generic;
using CounterStrikeSharp.API.Modules.Timers; 

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    // === 原本的死亡/回合結束 HUD 設定 ===
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

    // === 新增：獨立的刀局凍結時間 HUD 設定 ===
    [JsonPropertyName("KnifeImage")]
    public string KnifeImage { get; set; } = "LINKTO_KNIFE_IMAGE";

    [JsonPropertyName("KnifeImageWidth")]
    public int KnifeImageWidth { get; set; } = 250;

    [JsonPropertyName("KnifeImageHeight")]
    public int KnifeImageHeight { get; set; } = 35;
    
    [JsonPropertyName("KnifeDisplayDuration")]
    public float KnifeDisplayDuration { get; set; } = 5.0f; // 預設顯示 5 秒
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.30"; // 簡單穩定版
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    
    // 預先準備好兩種 HTML
    private string currentImageHtml = "";
    private string knifeImageHtml = ""; 
    
    // 【關鍵】：這用來控制現在 OnTick 到底要刷哪一張圖
    private string _activeHtml = ""; 
    
    private int _tickInterval = 1; 

    private List<CCSPlayerController> _targetPlayers = new List<CCSPlayerController>();
    private bool _isRoundEnd = false; 
    private CCSPlayerController? _lastVictim = null; 

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => 
        {
            bShowingServerGraphic = false;
            _isRoundEnd = false;
            _lastVictim = null;
            _targetPlayers.Clear(); 
        });

        // 原本最穩定的 OnTick，現在改為印出 _activeHtml
        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic) return;
            if (Server.TickCount % _tickInterval != 0) return;

            for (int i = _targetPlayers.Count - 1; i >= 0; i--)
            {
                var player = _targetPlayers[i];
                if (player != null && player.IsValid)
                {
                    player.PrintToCenterHtml(_activeHtml);
                }
            }
        });
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        _tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;
        
        currentImageHtml = $"<div style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'><img src='{Config.Image}' style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'></div>";
        knifeImageHtml = $"<div style='width: {Config.KnifeImageWidth}px; height: {Config.KnifeImageHeight}px;'><img src='{Config.KnifeImage}' style='width: {Config.KnifeImageWidth}px; height: {Config.KnifeImageHeight}px;'></div>";
    }

   [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isRoundEnd = false;
        _lastVictim = null;
        bShowingServerGraphic = false;
        _targetPlayers.Clear();

        // 延遲 1.5 秒避開 CS2 原生 UI 洗畫面
        AddTimer(1.5f, () =>
        {
            if (IsKnifeRound())
            {
                _activeHtml = knifeImageHtml; // 切換成刀局圖片
                
                foreach (var player in Utilities.GetPlayers())
                {
                    if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
                    {
                        _targetPlayers.Add(player); // 把所有玩家加入名單
                    }
                }
                
                bShowingServerGraphic = true; // 開啟顯示

                // 照你的要求，預設顯示 5 秒後關閉
                AddTimer(Config.KnifeDisplayDuration, () =>
                {
                    bShowingServerGraphic = false;
                    _targetPlayers.Clear();
                });
            }
        });

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
        
        _activeHtml = currentImageHtml; // 切換回死亡圖片
        bShowingServerGraphic = true;
        _lastVictim = victim; 

        if (_isRoundEnd)
        {
            AddTimer(Config.RoundEndDisplayDuration, () =>
            {
                if (_targetPlayers.Contains(victim))
                {
                    _targetPlayers.Remove(victim);
                }
                if (_targetPlayers.Count == 0) bShowingServerGraphic = false;
            });
        }
        else
        {
            AddTimer(Config.DeathDisplayDuration, () =>
            {
                if (_isRoundEnd && _lastVictim == victim) 
                    return; 

                if (_targetPlayers.Contains(victim))
                {
                    _targetPlayers.Remove(victim);
                }

                if (_targetPlayers.Count == 0)
                {
                    bShowingServerGraphic = false;
                }
            });
        }

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!IsLive()) return HookResult.Continue;

        _isRoundEnd = true; 
        
        bool wasLastVictimStillViewing = false;
        if (_lastVictim != null && _targetPlayers.Contains(_lastVictim))
        {
            wasLastVictimStillViewing = true;
        }
        
        _targetPlayers.Clear();
        
        if (wasLastVictimStillViewing && _lastVictim != null && _lastVictim.IsValid)
        {
            _targetPlayers.Add(_lastVictim);
            _activeHtml = currentImageHtml; // 切換回回合結束圖片
            bShowingServerGraphic = true;

            AddTimer(Config.RoundEndDisplayDuration, () =>
            {
                if (_targetPlayers.Contains(_lastVictim))
                {
                    _targetPlayers.Remove(_lastVictim);
                }
                if (_targetPlayers.Count == 0)
                {
                    bShowingServerGraphic = false;
                }
            });
        }
        else
        {
            bShowingServerGraphic = false;
        }

        return HookResult.Continue;
    }

    #region Helpers
    private bool IsKnifeRound()
    {
        bool isWarmup = false;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            if (entity.GameRules != null)
            {
                isWarmup = entity.GameRules.WarmupPeriod;
            }
            break; 
        }

        if (isWarmup) return false;
        
        return !IsLive();
    }

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
    #endregion
}
