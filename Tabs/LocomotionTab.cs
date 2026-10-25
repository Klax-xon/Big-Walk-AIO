using System;
using System.Numerics;
using BigWalk.AIO.State;
using BigWalk.AIO.Utils;
using ImGuiNET;

namespace BigWalk.AIO.Tabs;

/// <summary>
/// Locomotion & Vitality Overrides Tab.
/// Implements:
/// - Feature 3: Slope & Climb Traversability Bypass (Infinite slope climbing, water resistance removal)
/// - Feature 4: Fall Hazard & Daze Suppression (Ignore falling damage, zero sit pause, auto-clear daze)
/// - Feature 5: Kinematic Locomotion Tuning (Speed and Jump force multipliers)
/// - General vitality overrides (God Mode, Infinite Stamina, Time Freeze)
/// </summary>
public class LocomotionTab : IAioTab
{
    public string TabName => "Locomotion & Vitality";

    private readonly PlayerControlState _state;
    private readonly GuiConsole _console;

    public LocomotionTab(PlayerControlState state, GuiConsole console)
    {
        _state = state;
        _console = console;
    }

    public void Draw()
    {
        DrawLocomotionTuningSection();
        ImGui.Separator();

        DrawTraversabilitySection();
        ImGui.Separator();

        DrawFallAndVitalitySection();
        ImGui.Separator();

        DrawGeneralTogglesSection();
    }

    private void DrawLocomotionTuningSection()
    {
        if (ImGui.CollapsingHeader("Kinematic Locomotion Tuning (Speed & Jump)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Dynamically scale local player kinematic velocities via continuous engine assertion:");
            ImGui.Spacing();

            float avail = ImGui.GetContentRegionAvail().X;
            float sliderWidth = MathF.Max(avail * 0.52f, 140f);

            // Speed Multiplier
            ImGui.SetNextItemWidth(sliderWidth);
            float speed = _state.SpeedMultiplier;
            if (ImGui.SliderFloat("Speed Multiplier", ref speed, 1.0f, 5.0f, "%.2fx"))
            {
                _state.SpeedMultiplier = speed;
                _console.LogInfo($"Player speed multiplier set to {speed:F2}x");
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset##ResetSpeed"))
            {
                _state.SpeedMultiplier = 1.0f;
                _console.LogInfo("Speed multiplier reset to 1.0x.");
            }

            // Jump Force Multiplier
            ImGui.SetNextItemWidth(sliderWidth);
            float jump = _state.JumpMultiplier;
            if (ImGui.SliderFloat("Jump Multiplier", ref jump, 1.0f, 5.0f, "%.2fx"))
            {
                _state.JumpMultiplier = jump;
                _console.LogInfo($"Player jump multiplier set to {jump:F2}x");
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset##ResetJump"))
            {
                _state.JumpMultiplier = 1.0f;
                _console.LogInfo("Jump multiplier reset to 1.0x.");
            }

            ImGui.Spacing();

            // Quick Preset Buttons
            ImGui.TextUnformatted("Speed Presets:");
            ImGui.SameLine();
            if (ImGui.SmallButton("1.0x (Normal)")) _state.SpeedMultiplier = 1.0f;
            ImGui.SameLine();
            if (ImGui.SmallButton("1.5x (Swift)")) _state.SpeedMultiplier = 1.5f;
            ImGui.SameLine();
            if (ImGui.SmallButton("2.5x (Sonic)")) _state.SpeedMultiplier = 2.5f;
            ImGui.SameLine();
            if (ImGui.SmallButton("4.0x (Sprint)")) _state.SpeedMultiplier = 4.0f;
        }
    }

    private void DrawTraversabilitySection()
    {
        if (ImGui.CollapsingHeader("Slope & Climb Traversability Bypass (Feature 3)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Overrides surface slope angles and sliding resistance in PlayerGround:");
            ImGui.Spacing();

            bool climb = _state.InfiniteClimbActive;
            if (ImGui.Checkbox("Infinite Climb & Traversability Bypass", ref climb))
            {
                _state.InfiniteClimbActive = climb;
                _console.LogInfo($"Infinite Climb toggled: {(climb ? "ENABLED" : "DISABLED")}");
            }

            ImGui.BulletText("Forces slope limits to 89.0° angles (scales steep cliffs without clipping)");
            ImGui.BulletText("Preserves ground snapping and friction calculation (prevents catching or getting stuck)");
            ImGui.BulletText("Restores native engine physics angles seamlessly when toggled off");
        }
    }

    private void DrawFallAndVitalitySection()
    {
        if (ImGui.CollapsingHeader("Fall Hazard & Daze Suppression (Feature 4)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Prevents incapacitated knockdown states and fall penalties in PlayerFaller:");
            ImGui.Spacing();

            bool noFall = _state.NoFallDazeActive;
            if (ImGui.Checkbox("Suppress Fall Hazard & Incapacitation Daze", ref noFall))
            {
                _state.NoFallDazeActive = noFall;
                _console.LogInfo($"Fall & Daze suppression toggled: {(noFall ? "ENABLED" : "DISABLED")}");
            }

            ImGui.BulletText("Asserts ignoreFalling = true to eliminate fall impact damage");
            ImGui.BulletText("Forces sitPauseDuration = 0.0s to eliminate sitting incapacitation");
            ImGui.BulletText("Automatically calls ClearNextFall() when dazed to break out immediately");
        }
    }

    private void DrawGeneralTogglesSection()
    {
        if (ImGui.CollapsingHeader("Time & Camera Sandbox Overrides", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("##LocoGeneralGrid", 2, ImGuiTableFlags.None))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                bool freeze = _state.FreezeTime;
                if (ImGui.Checkbox("Freeze Time (timeScale = 0)", ref freeze))
                {
                    _state.ApplyFreezeTime(freeze, msg => _console.LogInfo(msg));
                }

                ImGui.TableNextColumn();
                float colAvail = ImGui.GetContentRegionAvail().X;
                ImGui.SetNextItemWidth(MathF.Max(colAvail * 0.50f, 100f));
                float fov = _state.FieldOfView;
                if (ImGui.SliderFloat("Camera FOV", ref fov, 60.0f, 130.0f, "%.0f°"))
                {
                    _state.ApplyFov(fov, msg => _console.LogInfo(msg));
                }

                ImGui.EndTable();
            }
        }
    }
}
