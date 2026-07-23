using System;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using System.Text.Json.Serialization;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("HtmlContent")]
    public string HtmlContent { get; set; } = "<img src='https://cdn.jsdelivr.net/gh/a4594865-crypto/ServerGraphic@main/images/logo2.png' width='600' height='120'>";

    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 15.0f; // 配合正常 5v5 的 15 秒凍結時間

    // 【修改重點】將刷新頻率改為 1.2 秒，配合 CS2 內建淡入動畫，創造完美的「呼吸感」
    [JsonPropertyName("RefreshInterval")]
    public float RefreshInterval { get; set; } = 1.2f; 
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.3.1"; // 呼吸感微調版

    public ServerGraphicConfig Config { get; set; }
    
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudTimer;
    private float _elapsedTime = 0f;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        AddCommand("css_testhud", "Test HUD", (player, info) =>
        {
            Console.WriteLine("[ServerGraphic] 手動觸發測試！");
            StartHudTimer();
        });

        // 1. 回合開始（凍結時間開始）
        RegisterEventHandler<EventRoundStart>((@event, info) =>
        {
            // 如果是暖身，或者剛開局就已經處於暫停狀態，跳過發送
            if (IsWarmup() || IsPaused())
            {
                return HookResult.Continue;
            }

            StartHudTimer();
            return HookResult.Continue;
        });

        // 2. 凍結時間結束 -> 立刻強制關閉 HUD
        RegisterEventHandler<EventRoundFreezeEnd>((@event, info) =>
        {
            StopHudTimer();
            return HookResult.Continue;
        });

        // 3. 回合結束時保險清除
        RegisterEventHandler<EventRoundEnd>((@event, info) =>
        {
            StopHudTimer();
            return HookResult.Continue;
        });
    }

    /// <summary>
    /// 檢查是否為暖身階段
    /// </summary>
    private bool IsWarmup()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        return gameRules != null && gameRules.WarmupPeriod;
    }

    /// <summary>
    /// 檢查是否為暫停狀態 (支援 MatchZy 的 .pause (.tech) 與 CS2 原生 .P 暫停)
    /// </summary>
    private bool IsPaused()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gameRules != null)
        {
            // MatchWaitingForResume = mp_pause_match (技術暫停/管理員暫停)
            // TerroristTimeOutActive / CTTimeOutActive = T或CT的隊伍暫停 (.P 觸發)
            return gameRules.MatchWaitingForResume || 
                   gameRules.TerroristTimeOutActive || 
                   gameRules.CTTimeOutActive;
        }
        return false;
    }

    private void StartHudTimer()
    {
        StopHudTimer();
        _elapsedTime = 0f;

        SendHudToAll();

        _hudTimer = AddTimer(Config.RefreshInterval, () =>
        {
            _elapsedTime += Config.RefreshInterval;

            // 觸發兩個條件之一就會關閉圖片：
            // 1. 顯示時間超過設定的 DisplayDuration (預設 15 秒)
            // 2. 偵測到遊戲被暫停
            if (_elapsedTime >= Config.DisplayDuration || IsPaused())
            {
                StopHudTimer();
                return;
            }

            SendHudToAll();
            
        }, TimerFlags.REPEAT);
    }

    private void StopHudTimer()
    {
        _hudTimer?.Kill();
        _hudTimer = null;
    }

    private void SendHudToAll()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) continue;
            player.PrintToCenterHtml(Config.HtmlContent);
        }
    }
}
