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
    public int ImageHeight { get; set; } = 35;

    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 1; 

    [JsonPropertyName("DeathDisplayDuration")]
    public float DeathDisplayDuration { get; set; } = 3.0f; // 一般中途死亡的顯示秒數

    [JsonPropertyName("RoundEndDisplayDuration")]
    public float RoundEndDisplayDuration { get; set; } = 5.0f; // 最後死亡(觸發回合結束)的顯示秒數
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.24"; // 升級為 1.0.24 (僅限最後死亡玩家版)
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    
    private int _tickInterval = 1; 

    private List<CCSPlayerController> _targetPlayers = new List<CCSPlayerController>();
    private bool _isRoundEnd = false; 

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

            // 效能極限優化：使用反向迴圈
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

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _isRoundEnd = false;
        bShowingServerGraphic = false;
        _targetPlayers.Clear();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!IsLive()) return HookResult.Continue;

        _isRoundEnd = true; 
        
        // 回合結束時，把還在看 3 秒圖片的「舊死者」清掉
        // 這樣畫面就能完全讓給接下來 0.2 秒後要顯示的「最後死者」
        _targetPlayers.Clear();
        bShowingServerGraphic = false;

        // 注意：這裡完全不需要掃描玩家了！極度省效能！

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;

        if (victim == null || !victim.IsValid || victim.IsBot || victim.IsHLTV) 
            return HookResult.Continue;

        if (!IsLive()) return HookResult.Continue;

        // 等待 0.2 秒
        AddTimer(0.2f, () =>
        {
            if (victim == null || !victim.IsValid) return;

            if (!_targetPlayers.Contains(victim))
            {
                _targetPlayers.Add(victim);
            }
            bShowingServerGraphic = true;

            // 【核心邏輯】：判斷這 0.2 秒內回合是否結束了？
            // 如果已經結束 (_isRoundEnd == true)，代表他是最後一個死的，給 5 秒
            // 如果還沒結束，代表是一般擊殺，給 3 秒
            float displayDuration = _isRoundEnd ? Config.RoundEndDisplayDuration : Config.DeathDisplayDuration;

            AddTimer(displayDuration, () =>
            {
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
