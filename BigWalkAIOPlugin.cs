using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using BigWalk.AIO.Core;
using PluginLog = BigWalk.AIO.Core.Log;
using DII = global::DearImGuiInjectionPlus.DearImGuiInjectionPlus;
using BigWalk.AIO.State;
using BigWalk.AIO.UI;
using BigWalk.AIO.Utils;
using DearImGuiInjectionPlus;
using Il2CppInterop.Runtime.Injection;
using ImGuiNET;
using UnityEngine;

namespace BigWalk.AIO;

public static class PluginInfo
{
    public const string GUID = "com.bigwalk.aio";
    public const string Name = "Big Walk AIO";
    public const string Version = "1.0.0";
}

/// <summary>
/// BepInEx 6 IL2CPP plugin entry point for Big Walk AIO.
/// Manages configuration bindings, lifecycle teardown, main window registration,
/// and IL2CPP runner injection.
/// </summary>
[BepInDependency(global::DearImGuiInjectionPlus.Metadata.GUID)]
[BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
public class BigWalkAIOPlugin : BasePlugin
{
    public static BigWalkAIOPlugin Instance { get; private set; }

    // Configuration Entries
    public static ConfigEntry<KeyCode> ToggleKeyConfig { get; private set; }
    public static ConfigEntry<KeyCode> MinimapKeyConfig { get; private set; }
    public static ConfigEntry<bool> AutoManageCursorConfig { get; private set; }
    public static ConfigEntry<GuiTheme> SelectedThemeConfig { get; private set; }
    public static ConfigEntry<float> UiScaleConfig { get; private set; }

    // State Pipelines & Model
    public StructSnapshotPipeline<GameTelemetrySnapshot> TelemetryPipeline { get; private set; }
    public StructSnapshotPipeline<MinimapSnapshot> MinimapPipeline { get; private set; }
    public PlayerControlState ControlState { get; private set; }
    public GuiConsole Console { get; private set; }
    public MainWindow Window { get; private set; }
    public MinimapWindow MinimapWindow { get; private set; }
    public FullMapWindow FullMapWindow { get; private set; }

    private GameObject _runnerGo;
    private bool _isTornDown;
    private bool _initialStyleApplied;

    /// <summary>
    /// Flag checked on the DirectX Present hook thread (inside DII.Render) to unconditionally
    /// clear ImGui focus when menu overlays close.
    /// </summary>
    public static bool PendingClearImGuiFocus;

    public override void Load()
    {
        Instance = this;
        PluginLog.Init(base.Log);
        PluginLog.Info($"[{PluginInfo.Name}] Starting Big Walk AIO bootstrap (v{PluginInfo.Version})...");

        // 1. Bind configuration entries - default to Insert for unified framework toggle
        ToggleKeyConfig = Config.Bind(
            "Keybinds",
            "ToggleKey",
            KeyCode.Insert,
            "Keyboard key used to open and close the Big Walk AIO menu (defaults to Insert).");

        // Force-sanitize: if config had F1, migrate immediately to Insert
        if (ToggleKeyConfig.Value == KeyCode.F1)
        {
            ToggleKeyConfig.Value = KeyCode.Insert;
        }

        MinimapKeyConfig = Config.Bind(
            "Keybinds",
            "MinimapKey",
            KeyCode.M,
            "Keyboard key used to toggle the expanded Island World Map overlay (defaults to M).");

        AutoManageCursorConfig = Config.Bind(
            "Input",
            "AutoManageCursor",
            true,
            "Whether opening or closing the window automatically toggles Dear ImGui cursor visibility and input blocking.");

        SelectedThemeConfig = Config.Bind(
            "Theme",
            "SelectedTheme",
            GuiTheme.ModernDark,
            "Active visual theme (0: ModernDark, 1: HighContrastAccent, 2: ClassicImGui).");

        UiScaleConfig = Config.Bind(
            "Display",
            "UiScale",
            1.0f,
            "Global UI and font scaling multiplier.");

        // 2. Initialize state pipelines and console
        TelemetryPipeline = new StructSnapshotPipeline<GameTelemetrySnapshot>(GameTelemetrySnapshot.CreateDefault());
        MinimapPipeline = new StructSnapshotPipeline<MinimapSnapshot>(MinimapSnapshot.CreateDefault());
        ControlState = new PlayerControlState();
        Console = new GuiConsole();

        // 3. Create primary windows
        Window = new MainWindow(
            TelemetryPipeline,
            ControlState,
            Console,
            onThemeChanged: SaveThemePreference,
            onScaleChanged: SaveScalePreference);

        MinimapWindow = new MinimapWindow(MinimapPipeline, ControlState);
        FullMapWindow = new FullMapWindow(MinimapPipeline, ControlState);

        // 4. Register windows (Standalone overlay - no persistent top menu bar)
        DII.RegisterWindow(Window);
        DII.RegisterWindow(MinimapWindow);
        DII.RegisterWindow(FullMapWindow);

        // 5. Hook ImGui render loop for theme initialization and focus clearing
        DII.Render += OnImGuiRenderFrame;

        // 6. Register and spawn IL2CPP MonoBehaviour Runner
        if (!ClassInjector.IsTypeRegisteredInIl2Cpp<BigWalkAIORunner>())
        {
            ClassInjector.RegisterTypeInIl2Cpp<BigWalkAIORunner>();
        }

        SpawnRunner();

        PluginLog.Info($"[{PluginInfo.Name}] Loaded successfully. Press [Insert] to open UI.");
    }

    private void OnImGuiRenderFrame()
    {
        if (!_initialStyleApplied)
        {
            try
            {
                ThemeManager.ApplyTheme(SelectedThemeConfig.Value);
                ImGui.GetIO().FontGlobalScale = UiScaleConfig.Value;
                _initialStyleApplied = true;
                PluginLog.Info($"[{PluginInfo.Name}] Applied theme '{SelectedThemeConfig.Value}' and scale '{UiScaleConfig.Value:F2}x'.");
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"[{PluginInfo.Name}] Error applying initial style: {ex.Message}");
            }
        }

        // Unconditionally clear ImGui active and hovered window focus when menu transitions to closed
        if (PendingClearImGuiFocus)
        {
            try
            {
                ImGui.SetWindowFocus(null);
            }
            catch { }
            PendingClearImGuiFocus = false;
        }
    }

    internal void SaveThemePreference(GuiTheme theme)
    {
        if (SelectedThemeConfig != null)
        {
            SelectedThemeConfig.Value = theme;
        }
    }

    internal void SaveScalePreference(float scale)
    {
        if (UiScaleConfig != null)
        {
            UiScaleConfig.Value = scale;
        }
    }

    /// <summary>
    /// Synchronizes Dear ImGui cursor visibility with open interactive overlay windows (MainWindow / FullMapWindow).
    /// Bypasses PlayerHead look spin during open UI, and lets DearImGuiInjectionPlus handle all cursor detours/restoration.
    /// </summary>
    public static void SyncCursorState()
    {
        var plugin = Instance;
        if (plugin == null || plugin._isTornDown) return;

        bool mainOpen = plugin.Window?.IsOpen ?? false;
        bool mapOpen = plugin.FullMapWindow?.IsOpen ?? false;
        bool shouldBeVisible = mainOpen || mapOpen;

        if (DII.IsCursorVisible != shouldBeVisible)
        {
            DII.SetCursorVisibility(shouldBeVisible);
        }

        var player = WorldManager.localPlayerCharacter;
        if (player != null && player.head != null)
        {
            player.head.bypassUpdate = shouldBeVisible;
        }
    }

    private void SpawnRunner()
    {
        try
        {
            _runnerGo = new GameObject("BigWalkAIO_Runner");
            _runnerGo.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
            _runnerGo.AddComponent<BigWalkAIORunner>();
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[{PluginInfo.Name}] Failed to spawn BigWalkAIORunner: {ex.Message}");
        }
    }

    internal void EnsureRunnerAlive()
    {
        if (_isTornDown || BigWalkAIORunner._isQuitting)
            return;

        try
        {
            if (_runnerGo == null)
            {
                SpawnRunner();
                PluginLog.Info($"[{PluginInfo.Name}] Successfully respawned BigWalkAIORunner GameObject.");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"[{PluginInfo.Name}] Could not respawn runner: {ex.Message}");
        }
    }

    public override bool Unload()
    {
        Teardown();
        return true;
    }

    public void Teardown()
    {
        if (_isTornDown)
            return;

        _isTornDown = true;
        PluginLog.Info($"[{PluginInfo.Name}] Performing clean teardown...");

        DII.Render -= OnImGuiRenderFrame;

        if (Window != null)
        {
            DII.UnregisterWindow(Window);
            Window.IsOpen = false;
        }

        if (MinimapWindow != null)
        {
            DII.UnregisterWindow(MinimapWindow);
            MinimapWindow.IsOpen = false;
        }

        if (FullMapWindow != null)
        {
            DII.UnregisterWindow(FullMapWindow);
            FullMapWindow.IsOpen = false;
        }

        MapTextureManager.Teardown();

        if (_runnerGo != null)
        {
            try
            {
                UnityEngine.Object.Destroy(_runnerGo);
            }
            catch
            {
                // Ignored during engine exit
            }
            _runnerGo = null;
        }

        PluginLog.Info($"[{PluginInfo.Name}] Teardown complete.");
    }
}
