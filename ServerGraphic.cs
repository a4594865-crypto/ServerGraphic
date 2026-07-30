using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;
using System.Collections.Generic; // 【新增】為了使用名單 (List) 功能
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
    public int UpdateTicks { get; set; } = 1; // 維持 1 保持完美的呼吸感

    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 7.0f;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.18"; // 升級為 1.0.18 (名單快取極限優化版)
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";
    
    private int _tickInterval = 1; 

    private CounterStrikeSharp.API.Modules.Timers.Timer? _delayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _displayTimer;
    
    // 【極限優化核心】：宣告一個專屬發送名單
    private List<CCSPlayerController> _targetPlayers = new List<CCSPlayerController>();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => 
        {
            bShowingServerGraphic = false;
            _targetPlayers.Clear(); // 換圖時清空名單
            ClearAllTimers();
        });

        RegisterListener<Listeners.OnTick>(() =>
        {
            if (!bShowingServerGraphic) return;
            if (Server.TickCount % _tickInterval != 0) return;

            // 【效能解放】：不再呼叫 Utilities.GetPlayers()，不產生記憶體垃圾
            // 也不再做繁瑣的 BOT、HLTV、隊伍判斷，直接對著點好名的名單發送！
            foreach (var player in _targetPlayers)
            {
                // 僅保留最後一道安全防線：防範這 7 秒內剛好有人斷線離開伺服器
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

// 替換原本的 OnEventRoundStart 變成 OnPlayerDeath
    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;

        // 安全性檢查：確認死者存在且不是 BOT
        if (victim == null || !victim.IsValid || victim.IsBot || victim.IsHLTV) 
            return HookResult.Continue;

        // 判斷是否為正式回合 (暖身不顯示)
        if (!IsLive()) return HookResult.Continue;

        // 把死掉的玩家加入發送名單，你原本的 OnTick 就會開始對他發送圖片
        if (!_targetPlayers.Contains(victim))
        {
            _targetPlayers.Add(victim);
        }
        bShowingServerGraphic = true;

        // 依照設定檔的秒數，時間到就把「他」從名單踢除
        AddTimer(Config.DisplayDuration, () =>
        {
            if (_targetPlayers.Contains(victim))
            {
                _targetPlayers.Remove(victim);
            }

            // 如果大家都復活或沒人死了 (名單清空)，關閉開關
            if (_targetPlayers.Count == 0)
            {
                bShowingServerGraphic = false;
            }
        });

        return HookResult.Continue;
    }

    #region Helpers
    private bool IsLive()
    {
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
        {
            // 只要是暖身階段，就回傳 false (不顯示圖片)
            if (gameRulesProxy.GameRules.WarmupPeriod) return false;
        }

        // 把原本檢查 mp_maxmoney、C4 等繁雜的 Cvar 邏輯全刪了
        // 這樣刀場 (通常會沒收 C4 和金錢) 圖片也能正常彈出來！
        return true;
    }
    #endregion
}
