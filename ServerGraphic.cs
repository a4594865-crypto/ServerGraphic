using System;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using System.Text.Json.Serialization;

// 1. 設定檔：一樣支援設定檔自動生成
public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("HtmlContent")]
    public string HtmlContent { get; set; } = "<img src='你的圖片網址' width='600' height='120'>";

    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 15.0f; // 總共顯示秒數

    [JsonPropertyName("RefreshInterval")]
    public float RefreshInterval { get; set; } = 1.0f; // 每隔幾秒發送一次封包給玩家 (設 1.0 秒即可，非常省效能)
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.1.0";

    public ServerGraphicConfig Config { get; set; }
    
    // 宣告一個計時器變數，用來控制與清除 Timer
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudTimer;
    private float _elapsedTime = 0f;

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        // 【新增】手動測試指令：在伺服器或遊戲控制台輸入 css_testhud 即可手動觸發
        AddCommand("css_testhud", "Test HUD", (player, info) =>
        {
            Console.WriteLine("[ServerGraphic] 管理員手動觸發了 HUD 測試！");
            if (player != null) player.PrintToChat(" \x04[ServerGraphic]\x01 正在測試發送 HUD...");
            StartHudTimer();
        });

        // 【修改】將 EventRoundPrestart 改為 EventRoundStart，因為 Prestart 有時熱重載不會觸發
        RegisterEventHandler<EventRoundStart>((@event, info) =>
        {
            Console.WriteLine("[ServerGraphic] 偵測到回合開始，準備發送 HUD！");
            StartHudTimer();
            return HookResult.Continue;
        });

        // 確保回合結束時清除畫面上殘留的計時器
        RegisterEventHandler<EventRoundEnd>((@event, info) =>
        {
            Console.WriteLine("[ServerGraphic] 回合結束，清除 HUD 計時器。");
            StopHudTimer();
            return HookResult.Continue;
        });
    }

    private void StartHudTimer()
    {
        // 如果有舊的計時器正在跑，先砍掉避免重複執行
        StopHudTimer();
        _elapsedTime = 0f;

        // 立即發送第一次 HUD
        SendHudToAll();

        // 建立重複執行的 Timer，每隔 RefreshInterval 秒執行一次
        _hudTimer = AddTimer(Config.RefreshInterval, () =>
        {
            _elapsedTime += Config.RefreshInterval;

            // 檢查是否超過總顯示秒數
            if (_elapsedTime >= Config.DisplayDuration)
            {
                StopHudTimer(); // 時間到，銷毀計時器
                return;
            }

            // 發送 HUD
            SendHudToAll();
            
        }, TimerFlags.REPEAT);
    }

    private void StopHudTimer()
    {
        // 安全地銷毀計時器
        _hudTimer?.Kill();
        _hudTimer = null;
    }

    private void SendHudToAll()
    {
        int count = 0; // 用來計算成功發送給幾個玩家
        foreach (var player in Utilities.GetPlayers())
        {
            // 防錯機制：確保玩家有效且非機器人
            if (player != null && player.IsValid && !player.IsBot)
            {
                player.PrintToCenterHtml(Config.HtmlContent);
                count++;
            }
        }
        
        // 【新增】在伺服器後台黑視窗印出紀錄，確認到底有沒有抓到玩家以及發送的內容
        Console.WriteLine($"[ServerGraphic] 已發送 HTML 給 {count} 名真實玩家。發送內容: {Config.HtmlContent}");
    }
}
