using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;
using HttpUtils;
using System.Linq;
using System;
using System.Threading.Tasks;

namespace ServerGraphic;

public class ServerGraphicConfig : BasePluginConfig
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "LINKTOIMAGE";

    [JsonPropertyName("ImageWidth")]
    public int ImageWidth { get; set; } = 600;

    [JsonPropertyName("ImageHeight")]
    public int ImageHeight { get; set; } = 120;
    
    // 💡 已經刪除 DisplayDuration 和 UpdateTicks，交給引擎原生淡出，配置檔更乾淨
}

public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
{
    public override string ModuleName => "ServerGraphic";
    public override string ModuleVersion => "1.0.5"; // 升級為原生淡出、完美防刀局版
    public override string ModuleAuthor => "unfortunate";

    public ServerGraphicConfig Config { get; set; } = new();

    public override void Load(bool hotReload)
    {
        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading +++ ");
        if (hotReload)
        {
            Console.WriteLine("[INFO] [CS2ServerGraphic] hotReload +++ ");
            Console.WriteLine("[INFO] [CS2ServerGraphic] hotReload --- ");
        }

        Console.WriteLine("[INFO] [CS2ServerGraphic] Loading --- ");
    }

    public void OnConfigParsed(ServerGraphicConfig config)
    {
        Config = config;
        // 💡 已經砍掉 OnTick 狂刷邏輯，完全解放伺服器 CPU
    }

    public void GetServerGraphicUrl()
    {
        Task.Run(async () =>
        {
            try
            {
                string? response = await Utils.HttpGetAsync("modalFeedbackEvent");
                if (response != null)
                {
                    Logger.LogInformation(response);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"后台请求失败: {ex.Message}");
            }
        });
    }

    [GameEventHandler]
    public HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // 1. 判斷是否為 Live 局
        if (!IsLive())
        {
            Logger.LogInformation("[ServerGraphic] 偵測為暖身或刀局，略過 HUD 顯示。");
            return HookResult.Continue;
        }

        Logger.LogInformation("[ServerGraphic] 偵測為 Live 局！準備延遲發送 HUD...");

        string imageHtml = $"<img src='{Config.Image}' width='{Config.ImageWidth}' height='{Config.ImageHeight}'>";

        // 🚨 核心修復：延遲 0.5 秒發送！
        // 避開回合開始瞬間，CS2 引擎強制清空畫面的動作
        AddTimer(0.5f, () =>
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (IsPlayerValid(player))
                {
                    player.PrintToCenterHtml(imageHtml);
                }
            }
            Logger.LogInformation("[ServerGraphic] HUD 圖片發送成功！");
        });
        
        return HookResult.Continue;
    }

    #region Helpers
    public static bool IsPlayerValid(CCSPlayerController? player)
    {
        return player != null
            && player.IsValid
            && !player.IsBot
            && !player.IsHLTV
            && player.PlayerPawn != null
            && player.PlayerPawn.IsValid
            && player.PlayerPawn.Value != null
            && player.PlayerPawn.Value.IsValid;
    }

    private bool IsLive()
    {
        // 1. 攔截內建暖身時間
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (gameRulesProxy != null && gameRulesProxy.GameRules != null)
        {
            if (gameRulesProxy.GameRules.WarmupPeriod)
            {
                return false;
            }
        }

        // 2. 對準你的 CFG：攔截 mp_maxmoney 0
        var maxMoney = ConVar.Find("mp_maxmoney");
        if (maxMoney != null)
        {
            // 避免型別報錯，int 和 float 都嘗試抓取
            try { if (maxMoney.GetPrimitiveValue<int>() == 0) return false; } catch { }
            try { if (maxMoney.GetPrimitiveValue<float>() == 0f) return false; } catch { }
        }

        // 3. 對準你的 CFG：攔截 mp_give_player_c4 0
        var giveC4 = ConVar.Find("mp_give_player_c4");
        if (giveC4 != null)
        {
            // C4 常常被底層當作 bool 處理，所以兩者都相容
            try { if (giveC4.GetPrimitiveValue<int>() == 0) return false; } catch { }
            try { if (giveC4.GetPrimitiveValue<bool>() == false) return false; } catch { } 
        }

        // 4. 對準你的 CFG (終極保險)：攔截 mp_free_armor 1
        // 正規競技局買甲要錢(0)，只有刀局 CFG 裡會設定免費給甲(1)
        var freeArmor = ConVar.Find("mp_free_armor");
        if (freeArmor != null)
        {
            try { if (freeArmor.GetPrimitiveValue<int>() == 1) return false; } catch { }
            try { if (freeArmor.GetPrimitiveValue<bool>() == true) return false; } catch { }
        }

        return true; // 如果以上都沒攔截到，才判定為真正的 Live 局
    }
    #endregion
}
