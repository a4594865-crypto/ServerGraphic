using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using System.Linq;
using System.Text.Json.Serialization;

namespace ServerGraphic
{
    // 設定檔類別：現在包含了圖片網址、秒數，以及圖片的寬度與高度！
    public class ServerGraphicConfig : BasePluginConfig
    {
        [JsonPropertyName("DisplayDuration")]
        public float DisplayDuration { get; set; } = 7.0f; // 預設顯示 5 秒

        [JsonPropertyName("ImageUrl")]
        public string ImageUrl { get; set; } = "https://pub-d9ae6a92fc9e4608a18e6c1f443e953e.r2.dev/logo10.png"; // 請換成你的網址

        [JsonPropertyName("ImageWidth")]
        public int ImageWidth { get; set; } = 250; // 預設寬度 250px

        [JsonPropertyName("ImageHeight")]
        public int ImageHeight { get; set; } = 35; // 預設高度 35px
    }

    public class ServerGraphic : BasePlugin, IPluginConfig<ServerGraphicConfig>
    {
        public override string ModuleName => "Server Graphic";
        public override string ModuleVersion => "1.0.15c"; // 加入寬高自訂設定檔功能
        public override string ModuleAuthor => "Your Name";

        public ServerGraphicConfig Config { get; set; } = new();

        private bool _bShowingServerGraphic = false;
        private CounterStrikeSharp.API.Modules.Timers.Timer? _graphicTimer = null;
        private int _cachedMaxPlayers = 64; 
        private string _htmlContent = "";

        public void OnConfigParsed(ServerGraphicConfig config)
        {
            Config = config;
            // 這裡動態讀取設定檔裡的寬高，組合出 HTML 代碼
            _htmlContent = $"<img src='{Config.ImageUrl}' style='width:{Config.ImageWidth}px; height:{Config.ImageHeight}px;'>";
        }

        public override void Load(bool hotReload)
        {
            RegisterEventHandler<EventRoundStart>(OnEventRoundStart);
            RegisterListener<Listeners.OnTick>(OnTick);
        }

        private HookResult OnEventRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            _cachedMaxPlayers = Server.MaxPlayers;

            AddTimer(0.5f, () =>
            {
                var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
                if (gameRules == null) return;

                if (!gameRules.FreezePeriod) return;

                _bShowingServerGraphic = true;
                _graphicTimer?.Kill();

                _graphicTimer = AddTimer(Config.DisplayDuration, () =>
                {
                    _bShowingServerGraphic = false;
                    _graphicTimer = null;
                });
            });

            return HookResult.Continue;
        }

        private void OnTick()
        {
            if (!_bShowingServerGraphic) return;

            for (int i = 1; i <= _cachedMaxPlayers; i++)
            {
                var player = Utilities.GetPlayerFromSlot(i);

                if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
                {
                    player.PrintToCenterHtml(_htmlContent);
                }
            }
        }
    }
}
