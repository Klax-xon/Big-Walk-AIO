using System;
using System.Numerics;
using BigWalk.AIO.State;
using BigWalk.AIO.Utils;
using DearImGuiInjectionPlus.BepInEx;
using ImGuiNET;
using UnityEngine;

namespace BigWalk.AIO.Tabs;

/// <summary>
/// Vision & Free-Cam Diagnostics Tab.
/// Implements:
/// - Feature 2: Local-Only Fullbright & Volumetric Fog Neutralizer (RenderSettings overrides)
/// - Feature 6: Built-in Free-Cam Integration (CameraCheatMover & PlayerCheater detachment)
/// </summary>
public class VisionTab : IAioTab
{
    public string TabName => "Vision & Free-Cam";

    private readonly PlayerControlState _state;
    private readonly GuiConsole _console;

    public VisionTab(PlayerControlState state, GuiConsole console)
    {
        _state = state;
        _console = console;
    }

    public void Draw()
    {
        DrawFullbrightSection();
        ImGui.Separator();

        DrawFreeCamSection();
    }

    private void DrawFullbrightSection()
    {
        if (ImGui.CollapsingHeader("Local Fullbright & Fog Neutralizer (Feature 2)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Overrides ambient illumination and volumetric fog for underground/night exploration:");

            bool fullbright = _state.FullbrightActive;
            if (ImGui.Checkbox("Enable Ambient Fullbright", ref fullbright))
            {
                _state.FullbrightActive = fullbright;
                _console.LogInfo($"Fullbright toggled: {(fullbright ? "ENABLED" : "DISABLED")}");
            }

            ImGui.SameLine();

            bool noFog = _state.DisableFog;
            if (ImGui.Checkbox("Disable Scene Fog", ref noFog))
            {
                _state.DisableFog = noFog;
                _console.LogInfo($"Fog neutralization toggled: {(noFog ? "ENABLED" : "DISABLED")}");
            }

            // Fullbright Intensity Slider
            ImGui.SetNextItemWidth(-140f);
            float intensity = _state.FullbrightIntensity;
            if (ImGui.SliderFloat("Light Intensity", ref intensity, 1.0f, 5.0f, "%.1fx"))
            {
                _state.FullbrightIntensity = intensity;
            }

            ImGui.Spacing();
            ImGui.BulletText("Forces RenderSettings.ambientMode to Flat and ambientLight to Pure White");
            ImGui.BulletText("Asserted continuously in LateUpdate to override Enviro 3 dynamic time cycles");
            ImGui.BulletText("Automatically restores original lighting & fog parameters upon deactivation");
        }
    }

    private void DrawFreeCamSection()
    {
        if (ImGui.CollapsingHeader("Cinematic Free-Camera Fixture (Feature 6)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Hooks the game's internal CameraCheatMover for unconstrained 6DoF observation:");

            // Status Badge
            ImGui.TextUnformatted("Camera Status:");
            ImGui.SameLine();
            if (_state.FreeCamDetached)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.20f, 0.65f, 0.35f, 0.85f));
                ImGui.SmallButton("[DETACHED - FREE FLIGHT]");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.35f, 0.35f, 0.35f, 0.85f));
                ImGui.SmallButton("[ATTACHED - PLAYER PIVOT]");
                ImGui.PopStyleColor();
            }

            ImGui.Spacing();

            // Toggle Button
            string btnLabel = _state.FreeCamDetached ? "Attach Camera Back to Player" : "Detach Camera (Start Free Flight) [F2]";
            if (ImGui.Button(btnLabel, new System.Numerics.Vector2(-1, 34)))
            {
                _state.ToggleFreeCam(msg => _console.LogInfo(msg));
            }

            ImGui.Spacing();

            // Sliders for Free-Cam Moving Speed & Sensitivity
            ImGui.SetNextItemWidth(-140f);
            float speed = _state.FreeCamSpeed;
            if (ImGui.SliderFloat("Flight Movement Speed", ref speed, 2.0f, 60.0f, "%.1f m/s"))
            {
                _state.FreeCamSpeed = speed;
            }

            ImGui.SetNextItemWidth(-140f);
            float sens = _state.FreeCamSensitivity;
            if (ImGui.SliderFloat("Mouse Sensitivity", ref sens, 0.5f, 10.0f, "%.1fx"))
            {
                _state.FreeCamSensitivity = sens;
            }

            ImGui.Spacing();

            // Teleport Character to Camera Position Action
            if (ImGui.Button("Warp Character to Camera Location", new System.Numerics.Vector2(-1, 30)))
            {
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    try
                    {
                        var player = WorldManager.localPlayerCharacter;
                        if (player != null && Camera.main != null)
                        {
                            UnityEngine.Vector3 camPos = Camera.main.transform.position;
                            player.transform.position = camPos;
                            if (player.rb != null)
                            {
                                player.rb.linearVelocity = UnityEngine.Vector3.zero;
                            }
                            _console.LogInfo($"Teleported character to camera at ({camPos.x:F1}, {camPos.y:F1}, {camPos.z:F1}).");
                        }
                    }
                    catch (Exception ex)
                    {
                        _console.LogError($"Failed to warp to camera: {ex.Message}");
                    }
                });
            }

            ImGui.Spacing();
            ImGui.BulletText("Controls during free-cam: Standard WASD keys + Mouse look");
            ImGui.BulletText("Press [F2] anytime in-game to toggle free camera instantaneously");
        }
    }
}
