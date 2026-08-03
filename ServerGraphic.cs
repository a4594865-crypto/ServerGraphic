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

    // ================= 新增：刀局專用參數 =================
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
    public override string ModuleVersion => "1.0.36"; // 雙系統獨立版 + 延遲判定修復搶拍 + Zero-Allocation
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    
    // === 原本的死亡/回合結束 HUD 系統 ===
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    private List<CCSPlayerController> _targetPlayers = new List<CCSPlayerController>();
    private bool _isRoundEnd = false; 
    private CCSPlayerController? _lastVictim = null; 

    // === 新增：完全獨立的刀局 HUD 系統 ===
    public bool bShowingKnifeGraphic = false;
    private string knifeImageHtml = "";

    private int _tickInterval = 1; 

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => 
        {
            bShowingServerGraphic = false;
            bShowingKnifeGraphic = false; // 重置刀局開關
            _isRoundEnd = false;
            _lastVictim = null;
            _targetPlayers.Clear(); 
        });

        RegisterListener<Listeners.OnTick>(() =>
        {
            // 兩個都沒開，直接無負擔 return
            if (!bShowingServerGraphic && !bShowingKnifeGraphic) return;
            if (Server.TickCount % _tickInterval != 0) return;

            // 【效能優化】：迴圈找 Slot 達成 Zero-Allocation，不產生 GC 垃圾
            for (int i = 0; i < Server.MaxPlayers; i++)
            {
                var player = Utilities.GetPlayerFromSlot(i);
                if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) continue;

                // 系統 1：死亡 HUD (優先顯示)
                if (bShowingServerGraphic && _targetPlayers.Contains(player))
                {
                    player.PrintToCenterHtml(currentImageHtml);
                }
                // 系統 2：刀局 HUD (完全不干擾死亡 HUD)
                else if (bShowingKnifeGraphic)
                {
                    player.PrintToCenterHtml(knifeImageHtml);
                }
            }
        });
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        _tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;
        
        currentImageHtml = $"<div style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'><img src='{Config.Image}' style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'></div>";
        
        // 預先準備好刀局的 HTML
        knifeImageHtml = $"<div style='width: {Config.KnifeImageWidth}px; height: {Config.KnifeImageHeight}px;'><img src='{Config.KnifeImage}' style='width: {Config.KnifeImageWidth}px; height: {Config.KnifeImageHeight}px;'></div>";
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isRoundEnd = false;
        _lastVictim = null;
        bShowingServerGraphic = false;
        bShowingKnifeGraphic = false; // 刀局重置
        _targetPlayers.Clear();

        // 【關鍵修正】：把 IsLive() 的判斷，移進去 1.0 秒的計時器裡面！
        // 延遲 1 秒後，讓 MatchZy 有足夠時間把刀局的規則 (沒錢、沒C4) 設定完畢
        AddTimer(1.0f, () => 
        {
            if (!IsLive())
            {
                bShowingKnifeGraphic = true; // 獨立開關啟動！

                AddTimer(Config.KnifeDisplayDuration, () => 
                {
                    bShowingKnifeGraphic = false; // 設定的秒數後關閉 (預設 5 秒)
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
    private bool IsLive()
    {
        // 【效能優化】：拔除 LINQ 的 FirstOrDefault()，改用純粹的 foreach 迴圈，達成 Zero-Allocation
        CCSGameRulesProxy? gameRulesProxy = null;
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            gameRulesProxy = entity;
            break; // 找到第一個就中斷迴圈，效能與 FirstOrDefault 完全一致，但不產生 GC 垃圾
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
