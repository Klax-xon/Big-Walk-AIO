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
            ImGui.TextDisabled("Dynamically scale local player kinematic velocities via continuous engine assertion:");

            // Speed Multiplier
            ImGui.SetNextItemWidth(-140f);
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
            ImGui.SetNextItemWidth(-140f);
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
            ImGui.TextDisabled("Overrides surface slope angles and sliding resistance in PlayerGround:");

            bool climb = _state.InfiniteClimbActive;
            if (ImGui.Checkbox("Infinite Climb & Traversability Bypass", ref climb))
            {
                _state.InfiniteClimbActive = climb;
                _console.LogInfo($"Infinite Climb toggled: {(climb ? "ENABLED" : "DISABLED")}");
            }

            ImGui.BulletText("Forces slope limits to 90.0° angles (enables scaling sheer vertical cliffs)");
            ImGui.BulletText("Neutralizes water movement drag and steep surface grip penalties");
            ImGui.BulletText("Restores native engine physics angles seamlessly when toggled off");
        }
    }

    private void DrawFallAndVitalitySection()
    {
        if (ImGui.CollapsingHeader("Fall Hazard & Daze Suppression (Feature 4)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Prevents incapacitated knockdown states and fall penalties in PlayerFaller:");

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
        if (ImGui.CollapsingHeader("Vitality & General Sandbox Overrides", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("##LocoGeneralGrid", 2, ImGuiTableFlags.None))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                bool god = _state.GodMode;
                if (ImGui.Checkbox("God Mode (Invulnerability)", ref god))
                {
                    _state.GodMode = god;
                    _console.LogInfo($"God Mode: {(god ? "ENABLED" : "DISABLED")}");
                }

                ImGui.TableNextColumn();
                bool stamina = _state.InfiniteStamina;
                if (ImGui.Checkbox("Infinite Stamina", ref stamina))
                {
                    _state.InfiniteStamina = stamina;
                    _console.LogInfo($"Infinite Stamina: {(stamina ? "ENABLED" : "DISABLED")}");
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                bool freeze = _state.FreezeTime;
                if (ImGui.Checkbox("Freeze Time (timeScale = 0)", ref freeze))
                {
                    _state.ApplyFreezeTime(freeze, msg => _console.LogInfo(msg));
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-80f);
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
