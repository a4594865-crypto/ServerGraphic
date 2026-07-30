using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using System.Linq;
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
    public int ImageHeight { get; set; } = 20;

    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 1; 

    // 【修改】將原本的 DisplayDuration 拆分為兩種不同情境的秒數
    [JsonPropertyName("DeathDisplayDuration")]
    public float DeathDisplayDuration { get; set; } = 3.0f; // 對應 spec_freeze_time

    [JsonPropertyName("RoundEndDisplayDuration")]
    public float RoundEndDisplayDuration { get; set; } = 5.0f; // 對應 mp_win_panel_display_time
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.22"; // 升級為 1.0.22 (雙重秒數獨立設定版)
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    
    private int _tickInterval = 1; 

    private List<CCSPlayerController> _targetPlayers = new List<CCSPlayerController>();
    private bool _isRoundEnd = false; // 用來防止死亡計時器跟回合結束計時器打架

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => 
        {
            bShowingServerGraphic = false;
            _isRoundEnd = false;
            _targetPlayers.Clear(); 
        });

        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic) return;
            if (Server.TickCount % _tickInterval != 0) return;

            // 【效能極限優化】：保留反向迴圈，不產生記憶體垃圾
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

    // 回合開始時重置狀態
    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isRoundEnd = false;
        bShowingServerGraphic = false;
        _targetPlayers.Clear();
        return HookResult.Continue;
    }

    // 【新增】回合結束事件
    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!IsLive()) return HookResult.Continue;

        _isRoundEnd = true; // 標記回合已結束，覆蓋掉任何進行中的死亡計時器

        // 回合結束時，將所有有效玩家加入名單
        foreach (var player in Utilities.GetPlayers())
        {
            if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
            {
                if (!_targetPlayers.Contains(player))
                {
                    _targetPlayers.Add(player);
                }
            }
        }
        
        bShowingServerGraphic = true;

        // 使用回合結束的獨立秒數 (預設 5 秒)
        AddTimer(Config.RoundEndDisplayDuration, () =>
        {
            _targetPlayers.Clear();
            bShowingServerGraphic = false;
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

        // 0.2 秒延遲顯示
        AddTimer(0.2f, () =>
        {
            // 如果這 0.2 秒內回合剛好結束了，就直接交給 OnRoundEnd 去處理，這裡不動作
            if (_isRoundEnd) return; 
            
            // 再次確認玩家狀態
            if (victim == null || !victim.IsValid) return;

            if (!_targetPlayers.Contains(victim))
            {
                _targetPlayers.Add(victim);
            }
            bShowingServerGraphic = true;

            // 使用玩家死亡的獨立秒數 (預設 3 秒)
            AddTimer(Config.DeathDisplayDuration, () =>
            {
                // 如果在計時期間回合結束了，就不提早移除他，讓回合結束的 5 秒計時器來接手
                if (_isRoundEnd) return; 

                if (_targetPlayers.Contains(victim))
                {
                    _targetPlayers.Remove(victim);
                }

                if (_targetPlayers.Count == 0)
                {
                    bShowingServerGraphic = false;
                }
            });
        });

        return HookResult.Continue;
    }

    #region Helpers
    // 嚴格檢查是否為正規競技 (阻擋暖身與刀局)
    private bool IsLive()
    {
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
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
