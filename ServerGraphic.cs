using System;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Cvars; // 導入 ConVar 模組來讀取伺服器參數
using System.Text.Json.Serialization;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("HtmlContent")]
    public string HtmlContent { get; set; } = "<img src='https://cdn.jsdelivr.net/gh/a4594865-crypto/ServerGraphic@main/images/logo2.png' width='600' height='120'>";

    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f; // 顯示 5 秒自動消失

    [JsonPropertyName("RefreshInterval")]
    public float RefreshInterval { get; set; } = 1.2f; // 呼吸感刷新頻率
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic_Optimized";
    public override string ModuleVersion => "1.3.3"; // 排除刀局完美版

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
            // 如果是 暖身、暫停、或是刀局，全部跳過不顯示！
            if (IsWarmup() || IsPaused() || IsKnifeRound())
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

    private bool IsWarmup()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        return gameRules != null && gameRules.WarmupPeriod;
    }

    private bool IsPaused()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (gameRules != null)
        {
            return gameRules.MatchWaitingForResume || 
                   gameRules.TerroristTimeOutActive || 
                   gameRules.CTTimeOutActive;
        }
        return false;
    }

    /// <summary>
    /// 偵測是否為 MatchZy 刀局
    /// </summary>
    private bool IsKnifeRound()
    {
        try
        {
            // 抓取伺服器的 C4 發放參數
            var giveC4ConVar = ConVar.Find("mp_give_player_c4");
            if (giveC4ConVar != null)
            {
                // CS2 參數可能是 bool 也可能是 int，用 try-catch 確保萬無一失
                try { if (giveC4ConVar.GetPrimitiveValue<bool>() == false) return true; } catch { }
                try { if (giveC4ConVar.GetPrimitiveValue<int>() == 0) return true; } catch { }
            }

            // 抓取伺服器的購買時間參數
            var buyTimeConVar = ConVar.Find("mp_buytime");
            if (buyTimeConVar != null)
            {
                try { if (buyTimeConVar.GetPrimitiveValue<float>() == 0f) return true; } catch { }
                try { if (buyTimeConVar.GetPrimitiveValue<int>() == 0) return true; } catch { }
            }
        }
        catch
        {
            // 讀取參數失敗時不干擾正常運作
        }

        return false; // 如果參數正常發放 C4 和購買時間，就是真正的 LIVE 回合
    }

    private void StartHudTimer()
    {
        StopHudTimer();
        _elapsedTime = 0f;

        SendHudToAll();

        _hudTimer = AddTimer(Config.RefreshInterval, () =>
        {
            _elapsedTime += Config.RefreshInterval;

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
