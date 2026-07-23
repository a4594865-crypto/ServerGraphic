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
    public float DisplayDuration { get; set; } = 20.0f; // 保險用的最長顯示秒數 (凍結時間結束會提前強制關閉)

    [JsonPropertyName("RefreshInterval")]
    public float RefreshInterval { get; set; } = 0.15f; // 無縫刷新頻率
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.2.0";

    public ServerGraphicConfig Config { get; set; }
    
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudTimer;
    private float _elapsedTime = 0f;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        // 手動測試指令：不受暖身/凍結限制，方便管理員隨時測試
        AddCommand("css_testhud", "Test HUD", (player, info) =>
        {
            Console.WriteLine("[ServerGraphic] 管理員手動觸發了 HUD 測試！");
            if (player != null) player.PrintToChat(" \x04[ServerGraphic]\x01 正在測試發送 HUD...");
            StartHudTimer();
        });

        // 1. 回合開始（凍結時間開始）
        RegisterEventHandler<EventRoundStart>((@event, info) =>
        {
            // 檢查是否為暖身階段，如果是暖身/刀搶/練習階段就跳過
            if (IsWarmup())
            {
                Console.WriteLine("[ServerGraphic] 目前為暖身階段，跳過發送 HUD。");
                return HookResult.Continue;
            }

            Console.WriteLine("[ServerGraphic] LIVE 回合凍結時間開始，準備發送 HUD！");
            StartHudTimer();
            return HookResult.Continue;
        });

        // 2. 凍結時間結束（倒數歸零，玩家可以開始移動） -> 立刻強制關閉 HUD
        RegisterEventHandler<EventRoundFreezeEnd>((@event, info) =>
        {
            Console.WriteLine("[ServerGraphic] 凍結時間結束，立刻清除 HUD 畫面！");
            StopHudTimer();
            return HookResult.Continue;
        });

        // 3. 回合結束時也確保清除計時器
        RegisterEventHandler<EventRoundEnd>((@event, info) =>
        {
            StopHudTimer();
            return HookResult.Continue;
        });
    }

    /// <summary>
    /// 檢查目前是否處於暖身/非正式階段
    /// </summary>
    private bool IsWarmup()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        return gameRules != null && gameRules.WarmupPeriod;
    }

    private void StartHudTimer()
    {
        StopHudTimer();
        _elapsedTime = 0f;

        SendHudToAll();

        _hudTimer = AddTimer(Config.RefreshInterval, () =>
        {
            _elapsedTime += Config.RefreshInterval;

            if (_elapsedTime >= Config.DisplayDuration)
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
        int count = 0;
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) continue;

            player.PrintToCenterHtml(Config.HtmlContent);
            count++;
        }
    }
}
