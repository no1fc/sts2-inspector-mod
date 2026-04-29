using Godot;

namespace TestMode1
{
    public class ModSettings
    {
        private const string SettingsPath = "user://sts2_inspector_settings.cfg";

        public static ModSettings Instance { get; private set; }

        public bool ShowDeck { get; private set; } = true;
        public bool ShowHand { get; private set; } = true;
        public bool ShowRelics { get; private set; } = true;
        public bool ShowPotions { get; private set; } = true;
        public bool ShowBuffs   { get; private set; } = true;
        public float PanelX { get; private set; } = 1600f;
        public float PanelY { get; private set; } = 300f;
        public float PollIntervalSeconds { get; private set; } = 1.5f;
        public string ImageBasePath { get; private set; } =
            @"C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2";

        public static ModSettings Load()
        {
            var settings = new ModSettings();
            var cfg = new ConfigFile();

            if (cfg.Load(SettingsPath) == Error.Ok)
            {
                settings.ShowDeck    = (bool)cfg.GetValue("display", "show_deck",    true);
                settings.ShowHand    = (bool)cfg.GetValue("display", "show_hand",    true);
                settings.ShowRelics  = (bool)cfg.GetValue("display", "show_relics",  true);
                settings.ShowPotions = (bool)cfg.GetValue("display", "show_potions", true);
                settings.ShowBuffs   = (bool)cfg.GetValue("display", "show_buffs",   true);
                settings.PanelX      = (float)cfg.GetValue("display", "panel_x",    1600f);
                settings.PanelY      = (float)cfg.GetValue("display", "panel_y",    300f);
                settings.PollIntervalSeconds = (float)cfg.GetValue("performance", "poll_interval_seconds", 1.5f);
                settings.ImageBasePath = (string)cfg.GetValue("assets", "image_base_path",
                    @"C:\AI_Folder\ClaudeCode\SlaytheSpire2\data_sts2");
            }

            Instance = settings;
            return settings;
        }

        public void Save()
        {
            var cfg = new ConfigFile();
            cfg.SetValue("display", "show_deck",    ShowDeck);
            cfg.SetValue("display", "show_hand",    ShowHand);
            cfg.SetValue("display", "show_relics",  ShowRelics);
            cfg.SetValue("display", "show_potions", ShowPotions);
            cfg.SetValue("display", "show_buffs",   ShowBuffs);
            cfg.SetValue("display", "panel_x",      PanelX);
            cfg.SetValue("display", "panel_y",      PanelY);
            cfg.SetValue("performance", "poll_interval_seconds", PollIntervalSeconds);
            cfg.Save(SettingsPath);
        }

        public void SetToggle(string category, bool value)
        {
            switch (category)
            {
                case "deck":    ShowDeck    = value; break;
                case "hand":    ShowHand    = value; break;
                case "relics":  ShowRelics  = value; break;
                case "potions": ShowPotions = value; break;
                case "buffs":   ShowBuffs   = value; break;
            }
            Save();
        }

        public void SetPanelPosition(float x, float y)
        {
            PanelX = x;
            PanelY = y;
            Save();
        }
    }
}
