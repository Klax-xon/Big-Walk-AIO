using System;
using System.Numerics;
using BigWalk.AIO.State;
using BigWalk.AIO.UI;
using BigWalk.AIO.Utils;
using DearImGuiInjectionPlus;
using DearImGuiInjectionPlus.Windows;
using ImGuiNET;

namespace BigWalk.AIO.Tabs;

/// <summary>
/// Settings & Preferences Tab.
/// Provides runtime UI scale and DPI adjustments, dynamic visual theme switching,
/// input confinement / hotkey behavior settings, radar minimap preferences, and framework telemetry.
/// </summary>
public class SettingsTab : IAioTab
{
    public string TabName => "Settings & Preferences";

    private readonly PlayerControlState _state;
    private readonly GuiConsole _console;
    private readonly Action<GuiTheme> _onThemeChanged;
    private readonly Action<float> _onScaleChanged;

    public SettingsTab(
        PlayerControlState state,
        GuiConsole console,
        Action<GuiTheme> onThemeChanged,
        Action<float> onScaleChanged)
    {
        _state = state;
        _console = console;
        _onThemeChanged = onThemeChanged;
        _onScaleChanged = onScaleChanged;
    }

    public void Draw()
    {
        DrawScaleSection();
        ImGui.Separator();

        DrawThemeSection();
        ImGui.Separator();

        DrawMinimapPreferencesSection();
        ImGui.Separator();

        DrawInputAndHotkeySection();
        ImGui.Separator();

        DrawFrameworkDiagnosticsSection();
    }

    private void DrawScaleSection()
    {
        if (ImGui.CollapsingHeader("Display Scaling & High-DPI Adjustments", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var io = ImGui.GetIO();
            float currentScale = io.FontGlobalScale;

            ImGui.TextWrapped("Adjust the global font and interface scale dynamically for 1080p, 1440p, or 4K displays:");
            ImGui.Spacing();

            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(MathF.Max(avail * 0.52f, 150f));
            if (ImGui.SliderFloat("UI / Font Scale", ref currentScale, 0.75f, 2.50f, "%.2fx"))
            {
                io.FontGlobalScale = currentScale;
                _onScaleChanged?.Invoke(currentScale);
            }

            ImGui.Spacing();
            ImGui.TextUnformatted("Quick Resolution Presets:");
            ImGui.SameLine();

            if (ImGui.Button("1080p (1.0x)"))
            {
                io.FontGlobalScale = 1.0f;
                _onScaleChanged?.Invoke(1.0f);
                _console.LogInfo("UI Scale set to 1.0x (1080p standard)");
            }
            ImGui.SameLine();

            if (ImGui.Button("1440p (1.25x)"))
            {
                io.FontGlobalScale = 1.25f;
                _onScaleChanged?.Invoke(1.25f);
                _console.LogInfo("UI Scale set to 1.25x (1440p standard)");
            }
            ImGui.SameLine();

            if (ImGui.Button("4K UHD (2.0x)"))
            {
                io.FontGlobalScale = 2.00f;
                _onScaleChanged?.Invoke(2.00f);
                _console.LogInfo("UI Scale set to 2.0x (4K UHD standard)");
            }
        }
    }

    private void DrawThemeSection()
    {
        if (ImGui.CollapsingHeader("Dynamic Visual Theme", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Select between 2 fully custom designed themes and pure vanilla Dear ImGui classic:");
            ImGui.Spacing();

            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(MathF.Max(avail * 0.52f, 150f));
            int current = (int)ThemeManager.CurrentTheme;
            if (ImGui.Combo("Active Theme", ref current, ThemeManager.ThemeNames, ThemeManager.ThemeNames.Length))
            {
                var newTheme = (GuiTheme)current;
                ThemeManager.ApplyTheme(newTheme);
                _onThemeChanged?.Invoke(newTheme);
                _console.LogInfo($"Theme switched to: {ThemeManager.ThemeNames[current]}");
            }

            ImGui.Spacing();

            // Quick Theme Overview
            switch (ThemeManager.CurrentTheme)
            {
                case GuiTheme.ModernDark:
                    ImGui.TextColored(new System.Numerics.Vector4(0.35f, 0.65f, 1.00f, 1.0f), "Theme: Modern Sleek Dark");
                    ImGui.BulletText("Deep slate/charcoal backgrounds with subtle slate borders");
                    ImGui.BulletText("Ergonomic 7px window and 4px control corner rounding");
                    break;

                case GuiTheme.HighContrastAccent:
                    ImGui.TextColored(new System.Numerics.Vector4(0.00f, 1.00f, 0.62f, 1.0f), "Theme: High-Contrast Emerald Accent");
                    ImGui.BulletText("Obsidian deep void backgrounds with glowing emerald highlights");
                    ImGui.BulletText("Sharp 4px geometry with 1.5px high-contrast borders");
                    break;

                case GuiTheme.ClassicImGui:
                    ImGui.TextColored(new System.Numerics.Vector4(0.9f, 0.9f, 0.9f, 1.0f), "Theme: Default ImGui Classic");
                    ImGui.BulletText("Original Omar Cornut / Dear ImGui classic styling");
                    break;
            }
        }
    }

    private void DrawMinimapPreferencesSection()
    {
        if (ImGui.CollapsingHeader("World Radar & Map Preferences", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Configure real-time 2D radar overlay and full world map projection parameters:");
            ImGui.Spacing();

            bool hudVisible = _state.HudMinimapVisible;
            if (ImGui.Checkbox("Show Corner HUD Minimap", ref hudVisible))
            {
                _state.HudMinimapVisible = hudVisible;
            }

            bool fullMap = _state.FullMapOpen;
            if (ImGui.Checkbox("Open Full World Map (Hotkey: M)", ref fullMap))
            {
                _state.FullMapOpen = fullMap;
            }

            bool headingUp = _state.MinimapHeadingUp;
            if (ImGui.Checkbox("HUD Heading-Up Rotation", ref headingUp))
            {
                _state.MinimapHeadingUp = headingUp;
            }

            ImGui.Spacing();

            float avail = ImGui.GetContentRegionAvail().X;
            ImGui.SetNextItemWidth(MathF.Max(avail * 0.52f, 150f));
            float zoom = _state.MinimapZoom;
            if (ImGui.SliderFloat("Radar Canvas Zoom", ref zoom, 0.1f, 2.0f, "%.2fx"))
            {
                _state.MinimapZoom = zoom;
            }

            ImGui.Spacing();

            bool showGourds = _state.MinimapShowLandmarks;
            if (ImGui.Checkbox("Render Gourd & Quest Landmarks", ref showGourds))
            {
                _state.MinimapShowLandmarks = showGourds;
            }

            ImGui.SameLine();

            bool showPeers = _state.MinimapShowPeers;
            if (ImGui.Checkbox("Render Networked Peer Players", ref showPeers))
            {
                _state.MinimapShowPeers = showPeers;
            }

            bool showCoords = _state.MinimapShowCoordinates;
            if (ImGui.Checkbox("Render Live World Coordinates Footer", ref showCoords))
            {
                _state.MinimapShowCoordinates = showCoords;
            }
        }
    }

    private void DrawInputAndHotkeySection()
    {
        if (ImGui.CollapsingHeader("Input Confinement & Hotkey Preferences", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.LabelText("Main Menu Toggle Hotkey", "Insert");
            ImGui.LabelText("Island World Map Hotkey", "M");
            ImGui.LabelText("Cinematic Free-Cam Toggle Hotkey", "F2");

            ImGui.Spacing();

            // Auto-Manage Cursor Toggle
            bool autoManage = BigWalkAIOPlugin.AutoManageCursorConfig?.Value ?? true;
            if (ImGui.Checkbox("Auto-Manage Cursor Visibility on Window Open/Close", ref autoManage))
            {
                if (BigWalkAIOPlugin.AutoManageCursorConfig != null)
                {
                    BigWalkAIOPlugin.AutoManageCursorConfig.Value = autoManage;
                }
                _console.LogInfo($"Auto-manage cursor set to: {autoManage}");
            }
            ImGui.TextWrapped("When enabled, closing the window releases the cursor and seamlessly unblocks game input.");

            ImGui.Spacing();

            // Block All Inputs Toggle
            bool blockAll = DearImGuiInjectionPlus.DearImGuiInjectionPlus.BlockAllInputsWhenOpen;
            if (ImGui.Checkbox("Block All Game Inputs When Menu Is Open", ref blockAll))
            {
                DearImGuiInjectionPlus.DearImGuiInjectionPlus.BlockAllInputsWhenOpen = blockAll;
                _console.LogInfo($"BlockAllInputsWhenOpen set to: {blockAll}");
            }
            ImGui.TextWrapped("When enabled, all mouse clicks, movements, and keystrokes are swallowed while the UI is open.");
        }
    }

    private static void DrawFrameworkDiagnosticsSection()
    {
        if (ImGui.CollapsingHeader("Framework Telemetry & Win32 Interop", ImGuiTreeNodeFlags.None))
        {
            IntPtr hwnd = global::DearImGuiInjectionPlus.DearImGuiInjectionPlus.WindowHandle;
            bool isUnicode = hwnd != IntPtr.Zero && User32.IsWindowUnicode(hwnd);

            ImGui.LabelText("Subclassed HWND", $"0x{hwnd.ToInt64():X8}");
            ImGui.LabelText("Win32 IsWindowUnicode", isUnicode ? "TRUE (UTF-16 Native)" : "FALSE (ANSI)");
            ImGui.LabelText("Cursor Visible", global::DearImGuiInjectionPlus.DearImGuiInjectionPlus.IsCursorVisible ? "TRUE" : "FALSE");
            ImGui.LabelText("WantCaptureMouse", global::DearImGuiInjectionPlus.DearImGuiInjectionPlus.WantCaptureMouse ? "TRUE" : "FALSE");
            ImGui.LabelText("WantCaptureKeyboard", global::DearImGuiInjectionPlus.DearImGuiInjectionPlus.WantCaptureKeyboard ? "TRUE" : "FALSE");
            ImGui.LabelText("Input Blocking Mode", global::DearImGuiInjectionPlus.DearImGuiInjectionPlus.BlockAllInputsWhenOpen ? "BLOCK ALL WHEN OPEN" : "SELECTIVE CAPTURE");
        }
    }
}
