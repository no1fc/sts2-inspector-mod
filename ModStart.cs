using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using TestMode1;
using TestMode1.UI;

[ModInitializer("ModInit")]
public static class ModStart
{
    public static void ModInit()
    {
        GD.Print("[Inspector] Initializing...");

        TestMode1.ModSettings.Load();

        var harmony = new Harmony("testmode1.inspector");
        harmony.PatchAll();

        var bootstrapper = new InspectorBootstrapper();
        (Engine.GetMainLoop() as SceneTree)?.Root.CallDeferred("add_child", bootstrapper);

        GD.Print("[Inspector] Initialized.");
    }
}

public partial class InspectorBootstrapper : Node
{
    public override void _Ready()
    {
        var canvas = new CanvasLayer { Layer = 128 };
        AddChild(canvas);

        _ = new ImageCache(TestMode1.ModSettings.Instance.ImageBasePath);

        var tooltip = new ImageTooltip();
        canvas.AddChild(tooltip);

        var overlay = new OverlayPanel();
        var popup   = new PlayerDetailPopup();
        canvas.AddChild(overlay);
        canvas.AddChild(popup);
        popup.Initialize(tooltip);

        popup.Position = new Vector2(600f, 200f);

        var poller = new PlayerDataPoller();
        AddChild(poller);

        poller.PlayerUpdated += snap =>
        {
            overlay.UpdatePlayer(snap);
            popup.RefreshIfShowing(snap);
        };

        poller.PlayerRemoved += id => overlay.RemovePlayer(id);

        overlay.PlayerRowClicked += id =>
        {
            var snap = poller.GetLastSnapshot(id);
            if (snap != null) popup.ShowPlayer(snap);
        };

        overlay.ToggleChanged += () => popup.ForceRefreshCurrent();

        GD.Print("[Inspector] UI ready.");
    }
}
