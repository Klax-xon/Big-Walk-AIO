using System;
using System.Collections.Generic;
using System.Numerics;
using BigWalk.AIO.State;
using BigWalk.AIO.Utils;
using ImGuiNET;

namespace BigWalk.AIO.Tabs;

/// <summary>
/// World Gourd & Prop Catalog Teleporter Tab (Feature 8).
/// Ingests all puzzle items, landmarks, and gourds registered across the island,
/// enabling instantaneous spatial warping with safe vertical clearance and velocity zeroing.
/// </summary>
public class WorldTeleportTab : IAioTab
{
    public string TabName => "World & Teleport";

    private readonly PlayerControlState _state;
    private readonly GuiConsole _console;

    // Filter string for searching gourds
    private string _searchFilter = string.Empty;

    // Manual coordinate inputs
    private System.Numerics.Vector3 _manualCoords = new(0f, 10f, 0f);

    // Filtered / full gourd names list
    private static readonly List<SaveablePropName> AllGourds = new();
    private static readonly string[] GourdDisplayNames;
    private int _selectedGourdIndex = 0;

    static WorldTeleportTab()
    {
        // Enumerate all SaveablePropName values that represent puzzle gourds
        var names = Enum.GetValues(typeof(SaveablePropName));
        var displayList = new List<string>();

        foreach (SaveablePropName val in names)
        {
            string str = val.ToString();
            if (str.StartsWith("gourd", StringComparison.OrdinalIgnoreCase))
            {
                AllGourds.Add(val);
                displayList.Add(str);
            }
        }

        GourdDisplayNames = displayList.ToArray();
    }

    public WorldTeleportTab(PlayerControlState state, GuiConsole console)
    {
        _state = state;
        _console = console;
    }

    public void Draw()
    {
        DrawGourdCatalogSection();
        ImGui.Separator();

        DrawManualCoordinatesSection();
    }

    private void DrawGourdCatalogSection()
    {
        if (ImGui.CollapsingHeader("World Gourd & Landmark Teleporter (Feature 8)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Select any registered quest gourd or puzzle landmark to warp directly to its world location:");

            // Search filter
            ImGui.SetNextItemWidth(-120f);
            ImGui.InputTextWithHint("##GourdSearch", "Filter gourds...", ref _searchFilter, 64);

            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##SearchClear"))
            {
                _searchFilter = string.Empty;
            }

            // Gourd Selection Combo/ListBox
            var filteredIndices = GetFilteredIndices();

            if (filteredIndices.Count > 0)
            {
                if (_selectedGourdIndex >= filteredIndices.Count)
                {
                    _selectedGourdIndex = 0;
                }

                int currentGourdIdx = filteredIndices[_selectedGourdIndex];
                string currentName = GourdDisplayNames[currentGourdIdx];

                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("##GourdCombo", currentName))
                {
                    for (int i = 0; i < filteredIndices.Count; i++)
                    {
                        int actualIdx = filteredIndices[i];
                        bool isSelected = (i == _selectedGourdIndex);
                        if (ImGui.Selectable(GourdDisplayNames[actualIdx], isSelected))
                        {
                            _selectedGourdIndex = i;
                        }

                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndCombo();
                }

                ImGui.Spacing();

                // Warp Action Button
                SaveablePropName selectedGourd = AllGourds[currentGourdIdx];
                if (ImGui.Button($"Warp to {selectedGourd} (+1.5m offset)", new System.Numerics.Vector2(-1, 34)))
                {
                    _state.TeleportToGourd(selectedGourd, msg => _console.LogInfo(msg));
                }
            }
            else
            {
                ImGui.TextDisabled("No gourds match the current filter text.");
            }

            ImGui.Spacing();
            ImGui.BulletText("Queries GourdPositionData catalog dynamically for exact world (X, Y, Z)");
            ImGui.BulletText("Zeroes player linear and angular velocity to prevent clipping on arrival");
        }
    }

    private void DrawManualCoordinatesSection()
    {
        if (ImGui.CollapsingHeader("Manual Coordinate Translocation", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("Precision coordinate warping across world coordinates:");

            float x = _manualCoords.X;
            float y = _manualCoords.Y;
            float z = _manualCoords.Z;

            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputFloat3("World Coords (X, Y, Z)", ref _manualCoords))
            {
                // Updated
            }

            ImGui.Spacing();

            if (ImGui.Button("Teleport to Coordinates", new System.Numerics.Vector2(-1, 32)))
            {
                UnityEngine.Vector3 target = new UnityEngine.Vector3(_manualCoords.X, _manualCoords.Y, _manualCoords.Z);
                _state.TeleportToCoordinates(target, msg => _console.LogInfo(msg));
            }

            ImGui.Spacing();

            if (ImGui.Button("Warp to Island Origin (0, 20, 0)", new System.Numerics.Vector2(-1, 28)))
            {
                _state.TeleportToCoordinates(new UnityEngine.Vector3(0f, 20f, 0f), msg => _console.LogInfo(msg));
            }
        }
    }

    private List<int> GetFilteredIndices()
    {
        var result = new List<int>();
        for (int i = 0; i < GourdDisplayNames.Length; i++)
        {
            if (string.IsNullOrEmpty(_searchFilter) || GourdDisplayNames[i].Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(i);
            }
        }
        return result;
    }
}
