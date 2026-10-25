using System;
using System.Collections.Generic;
using System.Numerics;
using BigWalk.AIO.State;
using BigWalk.AIO.Utils;
using ImGuiNET;

namespace BigWalk.AIO.Tabs;

/// <summary>
/// Developer Operation Runner Tab (Feature 7).
/// Exposes the game's internal remote debugging command catalog (Shipmates.RemotePlayerDebug),
/// enabling instantaneous execution of native rendering, collision, and performance operations.
/// </summary>
public class DebugOperationsTab : IAioTab
{
    public string TabName => "Debug Operations";

    private readonly PlayerControlState _state;
    private readonly GuiConsole _console;

    // Filter string for searching operations
    private string _filterText = string.Empty;

    // Catalog of known native debug operations in Big Walk
    private static readonly (string Key, string Description)[] Operations = new[]
    {
        ("ToggleMaxLight", "Removes light budget limits for maximum scene illumination"),
        ("ToggleVolumetricClouds", "Enables/disables heavy volumetric cloud layers"),
        ("ToggleOcean", "Toggles Crest dynamic ocean mesh simulation & waves"),
        ("ToggleTerrain", "Toggles island terrain mesh rendering"),
        ("ToggleTerrainColliders", "Enables/disables physics colliders on the world terrain"),
        ("ToggleLandmarks", "Toggles visibility of distant island navigation landmarks"),
        ("ToggleTrees", "Toggles procedural and baked tree instances"),
        ("ToggleRocks", "Toggles world rock formations and boulders"),
        ("ToggleDetails", "Toggles terrain grass, flowers, and detail meshes"),
        ("ToggleHbao", "Toggles Horizon-Based Ambient Occlusion post-processing"),
        ("ToggleBloom", "Toggles HDR bloom lighting post-processing"),
        ("ToggleSleeping", "Toggles character sleep/resting state"),
        ("ToggleRendererFeature", "Toggles scriptable URP renderer features"),
        ("ToggleFullScreenPassRendererFeature", "Toggles full-screen post pass effects"),
        ("ToggleShadowCachingOption", "Toggles static shadow map caching"),
        ("StopEnviroTime", "Freezes Enviro 3 dynamic day/night time progression"),
        ("AddColliderUnderPlayer", "Spawns a temporary horizontal collision plane beneath the player")
    };

    public DebugOperationsTab(PlayerControlState state, GuiConsole console)
    {
        _state = state;
        _console = console;
    }

    public void Draw()
    {
        if (ImGui.CollapsingHeader("Native Game Operations Catalog (Feature 7)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Directly execute Big Walk's internal Shipmates.RemotePlayerDebug command suite:");

            // Search filter bar
            ImGui.SetNextItemWidth(-100f);
            ImGui.InputTextWithHint("##OpSearch", "Filter operations...", ref _filterText, 64);

            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##OpClear"))
            {
                _filterText = string.Empty;
            }

            ImGui.Spacing();

            // 2-Column Responsive Operations Table
            if (ImGui.BeginTable("##OperationsGrid", 2, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Operation Command", ImGuiTableColumnFlags.WidthFixed, 220f);
                ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                for (int i = 0; i < Operations.Length; i++)
                {
                    var (key, desc) = Operations[i];
                    if (!string.IsNullOrEmpty(_filterText) &&
                        !key.Contains(_filterText, StringComparison.OrdinalIgnoreCase) &&
                        !desc.Contains(_filterText, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();

                    if (ImGui.Button($"{key}##OpBtn_{i}", new System.Numerics.Vector2(-1, 28)))
                    {
                        string opKey = key;
                        _state.ExecuteDebugOperation(opKey, msg => _console.LogInfo(msg));
                    }

                    ImGui.TableNextColumn();
                    ImGui.TextDisabled(desc);
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.BulletText("Dispatches RemotePlayerRequest to RemotePlayerDebugReceiver on the Unity main thread");
            ImGui.BulletText("Non-destructive: commands toggle internal engine states and can be triggered repeatedly");
        }
    }
}
