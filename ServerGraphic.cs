using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;
using CounterStrikeSharp.API.Modules.Timers; 

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "LINKTOIMAGE";

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 600;

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 120;

    [JsonPropertyName("UpdateTicks")]
    public int UpdateTicks { get; set; } = 8;

    [JsonPropertyName("DisplayDuration")]
    public float DisplayDuration { get; set; } = 5.0f;
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.14"; // 升級為 1.0.14：修復殘影現象
    public override string ModuleAuthor => "unfortunate & SLAYER"; 

    public ServerGraphicConfig Config { get; set; } = new();
    public bool bShowingServerGraphic = false;
    private string currentImageHtml = "";

    // 用來記錄並管理正在運行的計時器，避免跨回合干擾
    private CounterStrikeSharp.API.Modules.Timers.Timer? _delayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _displayTimer;

    // 從不閃爍版本移植過來的變數，用來緩存規則與控制執行頻率
    private CCSGameRulesProxy? _gameRulesProxy;
    private bool _runThisTick = false;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(map => 
        {
            bShowingServerGraphic = false;
            _gameRulesProxy = null; // 換圖時清空 proxy 緩存
            ClearAllTimers();
        });
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        
        currentImageHtml = $"<img src='{Config.Image}' style='width: {Config.ImageWidth}px; height: {Config.ImageHeight}px;'>";

        RegisterListener<Listeners.OnTick>(() =>
        {
            // ==========================================
            // 1. 控制每 N 個 Tick 發送一次 HTML 圖片，保護伺服器效能
            // ==========================================
            if (bShowingServerGraphic)
            {
                int tickInterval = Config.UpdateTicks <= 0 ? 1 : Config.UpdateTicks;
                if (Server.TickCount % tickInterval == 0)
                {
                    foreach (var player in Utilities.GetPlayers())
                    {
                        if (IsPlayerValid(player))
                        {
                            player.PrintToCenterHtml(currentImageHtml);
                        }
                    }
                }
            }

            // ==========================================
            // 2. 不閃爍的底層邏輯 (欺騙遊戲引擎，壓制原生 UI)
            // ==========================================
            _runThisTick = !_runThisTick;

            if (!_runThisTick) return;

            var proxy = GetGameRulesProxy();

            if (proxy == null || !proxy.IsValid) return;

            var gameRules = proxy.GameRules;
            if (gameRules == null) return;

            // 確保暖身期間不觸發這段覆寫邏輯
            if (gameRules.WarmupPeriod) return;

            float currentTime = Server.CurrentTime;
            float restartTime = gameRules.RestartRoundTime;

            bool expectedState = restartTime < currentTime;

            // 如果狀態不符，強制覆寫並通知客戶端，防止原生 UI 蓋掉 HTML 圖片
            if (gameRules.GameRestart != expectedState)
            {
                gameRules.GameRestart = expectedState;
                Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
            }
        });
    }

    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // 回合一開始，立刻砍掉任何可能還在背景跑的舊回合計時器
        ClearAllTimers();

        // 將 0.5 秒的延遲計時器存起來
        _delayTimer = AddTimer(1.5f, () =>
        {
            if (!IsLive())
            {
                return;
            }

            var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
            if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
            {
                if (!gameRulesProxy.GameRules.FreezePeriod)
                {
                    return;
                }
            }

            bShowingServerGraphic = true;

            // 將關閉 HUD 的計時器存起來
            _displayTimer = AddTimer(Config.DisplayDuration, () =>
            {
                if (bShowingServerGraphic)
                {
                    CloseHUD();
                }
            });
        });

        return HookResult.Continue;
    }

    private void CloseHUD()
    {
        bShowingServerGraphic = false; 

        // 【新增】：計時器結束時，主動對全體玩家發送空白字串，瞬間抹除客戶端上的殘影！
        foreach (var player in Utilities.GetPlayers())
        {
            if (IsPlayerValid(player))
            {
                player.PrintToCenterHtml(" "); 
            }
        }
    }

    private void ClearAllTimers()
    {
        _delayTimer?.Kill();
        _delayTimer = null;

        _displayTimer?.Kill();
        _displayTimer = null;
    }

    #region Helpers
    public static bool IsPlayerValid(CCSPlayerController? player)
    {
        // 嚴格的安全檢查
        return player != null
            && player.IsValid
            && !player.IsBot
            && !player.IsHLTV
            && player.PlayerPawn != null
            && player.PlayerPawn.IsValid
            && player.PlayerPawn.Value != null
            && player.PlayerPawn.Value.IsValid;
    }

    // 從不閃爍版本移植過來的 GameRules 高效獲取與緩存方法
    private CCSGameRulesProxy? GetGameRulesProxy()
    {
        if (_gameRulesProxy != null && _gameRulesProxy.IsValid)
        {
            return _gameRulesProxy;
        }

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
        {
            _gameRulesProxy = entity;
            return _gameRulesProxy;
        }

        _gameRulesProxy = null;
        return null;
    }

    private bool IsLive()
    {
        // 判斷是否為正式比賽的底層檢查
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
