using System;
using System.Numerics;
using BigWalk.AIO.Core;
using BigWalk.AIO.State;
using DearImGuiInjectionPlus;
using HouseHouse.Medal;
using ImGuiNET;

namespace BigWalk.AIO.UI;

/// <summary>
/// Expanded Fullscreen / Centered Interactive World Map Overlay ('M' Hotkey).
/// Re-architected with a dedicated 260px left sidebar and responsive canvas pane:
/// - Sidebar Tabs:
///   - [Display]: Zoom, panning presets, interactive texture candidate browser/selector, layer toggles, and live telemetry.
///   - [Filters]: Category toggles (Players, Landmarks, Medals/Quests, Gourds) with auto-wrapping subtitles.
///   - [Legend]: Entity counters, marker color keys, and coordinate reference.
/// - Right Canvas:
///   - Authentic island map texture projection or tactical topological world grid
///   - Interactive pan (click-and-drag) and zoom (mouse scroll / slider)
///   - Live player telemetry and hover tooltips
/// </summary>
public class FullMapWindow : IImGuiWindow
{
    public string Id => "bigwalk_aio_fullmap";
    public string Title => "Big Walk - Island World Map ('M' to Close)";

    private bool _isOpen;
    public bool IsOpen
    {
        get => _controlState != null ? _controlState.FullMapOpen : _isOpen;
        set
        {
            if (_isOpen != value)
            {
                _isOpen = value;
                if (_controlState != null)
                {
                    _controlState.FullMapOpen = value;
                }
                if (!value)
                {
                    BigWalkAIOPlugin.PendingClearImGuiFocus = true;
                }
                BigWalkAIOPlugin.SyncCursorState();
            }
        }
    }

    private readonly StructSnapshotPipeline<MinimapSnapshot> _pipeline;
    private readonly PlayerControlState _controlState;

    // Interaction & Window Control State
    private bool _lockWindow = true;
    private bool _followPlayer = false;
    private bool _showTexture = true;
    private bool _showGrid = true;
    private bool _flipTextureY = false;
    private float _textureAlpha = 1.0f;

    public FullMapWindow(StructSnapshotPipeline<MinimapSnapshot> pipeline, PlayerControlState controlState)
    {
        _pipeline = pipeline;
        _controlState = controlState;
    }

    public void OnRender()
    {
        if (!IsOpen) return;

        // Hotkey shortcut to close the map overlay with Escape while ImGui has focus
        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            IsOpen = false;
            return;
        }

        var io = ImGui.GetIO();
        Vector2 defaultSize = new Vector2(MathF.Min(io.DisplaySize.X * 0.90f, 1150f), MathF.Min(io.DisplaySize.Y * 0.85f, 850f));
        ImGui.SetNextWindowSize(defaultSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(700, 480), io.DisplaySize);
        ImGui.SetNextWindowPos(io.DisplaySize * 0.5f, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;
        if (_lockWindow)
        {
            windowFlags |= ImGuiWindowFlags.NoMove;
        }

        bool open = IsOpen;
        if (ImGui.Begin(Title, ref open, windowFlags))
        {
            IsOpen = open;
            var snapshot = _pipeline.GetLatest();

            // Continuous player centering when Follow Mode is active
            if (_followPlayer && snapshot.HasLocalPlayer)
            {
                _controlState.FullMapPanX = -snapshot.LocalPosition.x;
                _controlState.FullMapPanY = -snapshot.LocalPosition.z;
            }

            // Left Sidebar Child (Fixed 260px)
            ImGui.BeginChild("MapSidebar", new Vector2(260, 0), true);
            DrawSidebar(in snapshot);
            ImGui.EndChild();

            ImGui.SameLine();

            // Right Canvas Child (Fills remaining space)
            if (ImGui.BeginChild("MapCanvasArea", Vector2.Zero, false, ImGuiWindowFlags.NoScrollbar))
            {
                DrawMapCanvas(in snapshot);
                ImGui.EndChild();
            }
        }
        else
        {
            IsOpen = open;
        }

        ImGui.End();
    }

    private void DrawSidebar(in MinimapSnapshot snapshot)
    {
        if (ImGui.BeginTabBar("MapSidebarTabs", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("Display"))
            {
                DrawDisplayTab(in snapshot);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Filters"))
            {
                DrawFiltersTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Legend"))
            {
                DrawLegendTab(in snapshot);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawDisplayTab(in MinimapSnapshot snapshot)
    {
        ImGui.Spacing();

        // 1. Navigation & Zoom
        ImGui.TextUnformatted("Navigation & Zoom:");
        float zoom = _controlState.FullMapZoom;
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("##FmZoom", ref zoom, 0.2f, 4.0f, "Zoom: %.2fx"))
        {
            _controlState.FullMapZoom = zoom;
        }

        if (ImGui.SmallButton("0.5x")) _controlState.FullMapZoom = 0.5f;
        ImGui.SameLine();
        if (ImGui.SmallButton("1.0x")) _controlState.FullMapZoom = 1.0f;
        ImGui.SameLine();
        if (ImGui.SmallButton("2.0x")) _controlState.FullMapZoom = 2.0f;
        ImGui.SameLine();
        if (ImGui.SmallButton("3.5x")) _controlState.FullMapZoom = 3.5f;

        ImGui.Spacing();

        if (ImGui.Button("Center on Player##FmCenter", new Vector2(-1, 26)))
        {
            if (snapshot.HasLocalPlayer)
            {
                _controlState.FullMapPanX = -snapshot.LocalPosition.x;
                _controlState.FullMapPanY = -snapshot.LocalPosition.z;
            }
        }

        ImGui.Checkbox("Follow Local Player##FmFollow", ref _followPlayer);

        if (ImGui.Button("Reset View##FmReset", new Vector2(-1, 24)))
        {
            _followPlayer = false;
            _controlState.FullMapZoom = 1.0f;
            _controlState.FullMapPanX = 0f;
            _controlState.FullMapPanY = 0f;
        }

        ImGui.Checkbox("Lock Window Position##FmLock", ref _lockWindow);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 2. Interactive Candidate Texture Selector
        ImGui.TextUnformatted("Map Texture Selector:");
        var candidates = MapTextureManager.Candidates;
        if (candidates != null && candidates.Count > 0)
        {
            int currentIdx = MapTextureManager.SelectedCandidateIndex;
            if (currentIdx < 0 || currentIdx >= candidates.Count) currentIdx = 0;

            string preview = candidates[currentIdx].DisplayLabel;
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo("##CandidateCombo", preview))
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    bool isSelected = (i == currentIdx);
                    if (ImGui.Selectable(candidates[i].DisplayLabel, isSelected))
                    {
                        MapTextureManager.SelectCandidate(i);
                    }
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            // Quick step buttons
            if (ImGui.Button("< Prev##PrevTex", new Vector2(60, 24)))
            {
                int prev = (currentIdx - 1 + candidates.Count) % candidates.Count;
                MapTextureManager.SelectCandidate(prev);
            }
            ImGui.SameLine();
            if (ImGui.Button("Next >##NextTex", new Vector2(60, 24)))
            {
                int next = (currentIdx + 1) % candidates.Count;
                MapTextureManager.SelectCandidate(next);
            }
            ImGui.SameLine();
            ImGui.TextDisabled($"[{currentIdx + 1}/{candidates.Count}]");
        }
        else
        {
            TextDisabledWrapped("No candidate textures found in memory. Press Rescan below.");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 3. Layer Toggles
        ImGui.TextUnformatted("Layer Visibility:");
        IntPtr srv = MapTextureManager.GetShaderResourceView();
        bool hasTexture = srv != IntPtr.Zero;

        if (hasTexture)
        {
            ImGui.Checkbox("Render Map Texture##FmTex", ref _showTexture);
            if (_showTexture)
            {
                ImGui.Indent(12f);
                ImGui.Checkbox("Flip Texture Y##FmFlipY", ref _flipTextureY);
                ImGui.SetNextItemWidth(-1f);
                ImGui.SliderFloat("##FmAlpha", ref _textureAlpha, 0.1f, 1.0f, "Opacity: %.0f%%");
                ImGui.Unindent(12f);
            }
        }
        else
        {
            TextDisabledWrapped("Map Texture: [Scanning / Fallback]");
        }

        ImGui.Checkbox("Topological Grid##FmGrid", ref _showGrid);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 4. Texture Diagnostics & Controls
        ImGui.TextUnformatted("Texture Diagnostics:");
        if (ImGui.Button("Force Rescan Textures##FmRescan", new Vector2(-1, 26)))
        {
            MapTextureManager.ForceRescan();
        }

        if (hasTexture)
        {
            ImGui.TextColored(new Vector4(0.2f, 1f, 0.4f, 1f), "Status: Texture Active");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f), "Status: Tactical Grid");
        }

        ImGui.TextWrapped($"Bound: {MapTextureManager.TextureName}");
        ImGui.TextWrapped($"Dimensions: {MapTextureManager.Width}x{MapTextureManager.Height} ({MapTextureManager.TextureFormat})");
        ImGui.Spacing();
        ImGui.TextWrapped($"Source: {MapTextureManager.SourceInfo}");
    }

    private void DrawFiltersTab()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Entity Categories:");

        bool showPlayers = _controlState.FullMapShowPlayers;
        if (ImGui.Checkbox("Players & Peers##FmPlayers", ref showPlayers))
            _controlState.FullMapShowPlayers = showPlayers;
        TextDisabledWrapped("Local player marker & connected peers");

        ImGui.Spacing();

        bool showLandmarks = _controlState.FullMapShowLandmarks;
        if (ImGui.Checkbox("Landmarks & Towers##FmLandmarks", ref showLandmarks))
            _controlState.FullMapShowLandmarks = showLandmarks;
        TextDisabledWrapped("Island reference points & radio towers");

        ImGui.Spacing();

        bool showMedals = _controlState.FullMapShowMedals;
        if (ImGui.Checkbox("Quests / Medals##FmMedals", ref showMedals))
            _controlState.FullMapShowMedals = showMedals;
        TextDisabledWrapped("Active quest targets & challenge medals");

        ImGui.Spacing();

        bool showGourds = _controlState.FullMapShowGourds;
        if (ImGui.Checkbox("Props / Gourds##FmGourds", ref showGourds))
            _controlState.FullMapShowGourds = showGourds;
        TextDisabledWrapped("Cataloged puzzle gourds & interactables");
    }

    private void DrawLegendTab(in MinimapSnapshot snapshot)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Entities & Legend:");

        // Local Player
        ImGui.TextColored(new Vector4(0.2f, 1f, 0.4f, 1f), "▲ Local Player (YOU)");
        TextDisabledWrapped(snapshot.HasLocalPlayer ? "Status: Spawned & Tracked" : "Status: Waiting for character spawn");
        ImGui.Spacing();

        // Peers
        ImGui.TextColored(new Vector4(0f, 0.75f, 1f, 1f), $"● Network Peers ({snapshot.PeerCount})");
        TextDisabledWrapped("Connected multiplayer party members");
        ImGui.Spacing();

        // Quest Medals
        ImGui.TextColored(new Vector4(1f, 0.65f, 0f, 1f), $"◆ Quests / Medals ({snapshot.MedalCount})");
        TextDisabledWrapped("Challenge objectives & puzzle goals");
        ImGui.Spacing();

        // Gourds
        ImGui.TextColored(new Vector4(1f, 0.85f, 0f, 1f), $"● Gourds & Props ({snapshot.GourdCount})");
        TextDisabledWrapped("Collectable gourds & saveable props");
        ImGui.Spacing();

        // Landmarks
        ImGui.TextColored(new Vector4(0.82f, 0.40f, 1f, 1f), $"◆ Landmarks ({snapshot.LandmarkCount})");
        TextDisabledWrapped("Major island geographical features");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextUnformatted("Coordinate System:");
        TextDisabledWrapped("• Island Center: (0, 0)");
        TextDisabledWrapped("• +X = East (Right)");
        TextDisabledWrapped("• +Z = North (Up)");
        TextDisabledWrapped("• +Y = Altitude (Upwards)");
    }

    private static void TextDisabledWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawMapCanvas(in MinimapSnapshot snapshot)
    {
        Vector2 canvasP0 = ImGui.GetCursorScreenPos();
        Vector2 canvasSize = ImGui.GetContentRegionAvail();

        // Reserve 26px for bottom coordinate footer
        float footerHeight = 26f;
        canvasSize.Y = MathF.Max(canvasSize.Y - footerHeight, 100f);

        if (canvasSize.X < 50 || canvasSize.Y < 50) return;

        // Allocate invisible button spanning the entire canvas.
        // Captures mouse events so dragging pans the map instead of displacing the ImGui window!
        ImGui.InvisibleButton("##mapCanvasDragSurface", canvasSize);
        bool isCanvasHovered = ImGui.IsItemHovered();
        bool isCanvasActive = ImGui.IsItemActive();
        bool isHovered = isCanvasHovered;

        var io = ImGui.GetIO();

        // Scroll wheel zooming
        if (isCanvasHovered && io.MouseWheel != 0f)
        {
            float newZoom = Math.Clamp(_controlState.FullMapZoom * (1.0f + io.MouseWheel * 0.15f), 0.2f, 4.0f);
            _controlState.FullMapZoom = newZoom;
        }

        // Click-and-drag panning (Left click on active canvas, or Right click while hovered)
        if ((isCanvasActive && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f)) ||
            (isCanvasHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Right, 0f)))
        {
            Vector2 dragDelta = io.MouseDelta;
            if (dragDelta.X != 0f || dragDelta.Y != 0f)
            {
                _followPlayer = false;
                _controlState.FullMapPanX += dragDelta.X / _controlState.FullMapZoom;
                _controlState.FullMapPanY -= dragDelta.Y / _controlState.FullMapZoom;
            }
        }

        var drawList = ImGui.GetWindowDrawList();
        Vector2 canvasCenter = canvasP0 + canvasSize * 0.5f;

        // Clip everything within canvas rectangle
        drawList.PushClipRect(canvasP0, canvasP0 + canvasSize, true);

        // Canvas Background (Deep Void Charcoal)
        drawList.AddRectFilled(canvasP0, canvasP0 + canvasSize, 0xFF0E1217);

        float zoom = _controlState.FullMapZoom;
        Vector2 pan = new Vector2(_controlState.FullMapPanX, _controlState.FullMapPanY);

        // World (0, 0) projected on screen:
        // World coordinates: +X is East (right), +Z is North (up, so -Y on screen)
        Vector2 worldOriginOnScreen = canvasCenter + new Vector2(pan.X, -pan.Y) * zoom;

        float worldExtent = 1200f; // -1200m to +1200m island region

        // --- Render Authentic Island Map Texture (if available and enabled) ---
        IntPtr srv = MapTextureManager.GetShaderResourceView();
        if (srv != IntPtr.Zero && _showTexture)
        {
            // Island extent bounds on screen:
            Vector2 texMin = new Vector2(worldOriginOnScreen.X - worldExtent * zoom, worldOriginOnScreen.Y - worldExtent * zoom);
            Vector2 texMax = new Vector2(worldOriginOnScreen.X + worldExtent * zoom, worldOriginOnScreen.Y + worldExtent * zoom);

            // Neutral backing rectangle so transparent / alpha textures don't blend into pure void
            drawList.AddRectFilled(texMin, texMax, 0xFF181D26);

            Vector2 uv0 = _flipTextureY ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            Vector2 uv1 = _flipTextureY ? new Vector2(1f, 0f) : new Vector2(1f, 1f);

            uint tintCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, _textureAlpha));
            drawList.AddImage(srv, texMin, texMax, uv0, uv1, tintCol);
        }

        // --- Render Scaled Tactical Topological World Grid ---
        if (_showGrid || srv == IntPtr.Zero)
        {
            uint colGridMinor = srv != IntPtr.Zero ? 0x14FFFFFFu : 0x18FFFFFFu;
            uint colGridMajor = srv != IntPtr.Zero ? 0x2AFFFFFFu : 0x38FFFFFFu;
            uint colAxis = srv != IntPtr.Zero ? 0x6600E699u : 0x8800E699u;
            uint colBorder = 0x6600E699u;
            uint colOceanBorder = 0x4400BFFFu;

            // Draw island extent bounds rectangle
            Vector2 bMin = worldOriginOnScreen + new Vector2(-worldExtent, -worldExtent) * zoom;
            Vector2 bMax = worldOriginOnScreen + new Vector2(worldExtent, worldExtent) * zoom;
            drawList.AddRect(bMin, bMax, colBorder, 0f, ImDrawFlags.None, 1.5f);

            // Draw outer ocean navigation boundary
            float oceanExtent = worldExtent * 1.25f;
            Vector2 oMin = worldOriginOnScreen + new Vector2(-oceanExtent, -oceanExtent) * zoom;
            Vector2 oMax = worldOriginOnScreen + new Vector2(oceanExtent, oceanExtent) * zoom;
            drawList.AddRect(oMin, oMax, colOceanBorder, 0f, ImDrawFlags.None, 1.0f);

            // Vertical grid lines (X steps every 50m / 200m)
            for (float wx = -worldExtent; wx <= worldExtent; wx += 50f)
            {
                float sx = worldOriginOnScreen.X + wx * zoom;
                if (sx < canvasP0.X || sx > canvasP0.X + canvasSize.X) continue;

                bool isAxis = MathF.Abs(wx) < 1f;
                bool isMajor = MathF.Abs(wx % 200f) < 1f;

                uint c = isAxis ? colAxis : (isMajor ? colGridMajor : colGridMinor);
                float th = isAxis ? 1.5f : 1.0f;
                drawList.AddLine(new Vector2(sx, canvasP0.Y), new Vector2(sx, canvasP0.Y + canvasSize.Y), c, th);

                if (isMajor && !isAxis)
                {
                    drawList.AddText(new Vector2(sx + 3f, canvasP0.Y + 4f), 0x55FFFFFF, $"{wx:F0}");
                }
            }

            // Horizontal grid lines (Z steps every 50m / 200m, screen Y = -Z)
            for (float wz = -worldExtent; wz <= worldExtent; wz += 50f)
            {
                float sy = worldOriginOnScreen.Y - wz * zoom;
                if (sy < canvasP0.Y || sy > canvasP0.Y + canvasSize.Y) continue;

                bool isAxis = MathF.Abs(wz) < 1f;
                bool isMajor = MathF.Abs(wz % 200f) < 1f;

                uint c = isAxis ? colAxis : (isMajor ? colGridMajor : colGridMinor);
                float th = isAxis ? 1.5f : 1.0f;
                drawList.AddLine(new Vector2(canvasP0.X, sy), new Vector2(canvasP0.X + canvasSize.X, sy), c, th);

                if (isMajor && !isAxis)
                {
                    drawList.AddText(new Vector2(canvasP0.X + 4f, sy - 14f), 0x55FFFFFF, $"{wz:F0}");
                }
            }

            // Central Origin Crosshairs & Marker (0, 0)
            drawList.AddCircleFilled(worldOriginOnScreen, 4.5f, colAxis);
            drawList.AddCircle(worldOriginOnScreen, 12f, 0x5500E699, 16, 1.0f);
            drawList.AddText(worldOriginOnScreen + new Vector2(7f, -14f), colAxis, "(0,0) Island Origin");
        }

        // Compass Rose in Top-Right Corner of Canvas
        Vector2 compassCenter = canvasP0 + new Vector2(canvasSize.X - 35f, 35f);
        drawList.AddCircleFilled(compassCenter, 18f, 0xCC111822);
        drawList.AddCircle(compassCenter, 18f, 0x8800E699, 32, 1.0f);
        drawList.AddText(compassCenter + new Vector2(-4f, -16f), 0xFF00FF99, "N");
        drawList.AddText(compassCenter + new Vector2(8f, -6f), 0x88FFFFFF, "E");
        drawList.AddText(compassCenter + new Vector2(-4f, 4f), 0x88FFFFFF, "S");
        drawList.AddText(compassCenter + new Vector2(-16f, -6f), 0x88FFFFFF, "W");

        // Tooltip tracking
        string hoveredTooltip = null;

        // --- Render Category Markers ---

        // 1. Quests / Medals (Orange/Gold Star/Shield)
        if (_controlState.FullMapShowMedals)
        {
            uint colMedal = 0xFFFFA500;
            for (int i = 0; i < snapshot.MedalCount; i++)
            {
                var mPos = snapshot.GetMedalPosition(i);
                Vector2 sp = worldOriginOnScreen + new Vector2(mPos.x, -mPos.z) * zoom;
                if (sp.X < canvasP0.X - 20f || sp.X > canvasP0.X + canvasSize.X + 20f ||
                    sp.Y < canvasP0.Y - 20f || sp.Y > canvasP0.Y + canvasSize.Y + 20f)
                    continue;

                // Diamond icon
                drawList.AddQuadFilled(
                    new Vector2(sp.X, sp.Y - 6f),
                    new Vector2(sp.X + 6f, sp.Y),
                    new Vector2(sp.X, sp.Y + 6f),
                    new Vector2(sp.X - 6f, sp.Y),
                    colMedal);
                drawList.AddCircle(sp, 8f, 0xAAFFA500, 16, 1.0f);

                int mType = snapshot.GetMedalType(i);
                string mName = mType >= 0 ? ((MedalType)mType).ToString() : $"Medal #{i + 1}";
                drawList.AddText(new Vector2(sp.X + 9f, sp.Y - 7f), 0xEEFFA500, mName);

                if (isHovered && Vector2.Distance(io.MousePos, sp) <= 12f)
                {
                    float dist = snapshot.HasLocalPlayer ? UnityEngine.Vector3.Distance(snapshot.LocalPosition, mPos) : 0f;
                    hoveredTooltip = $"[Quest Medal] {mName}\nWorld Pos: ({mPos.x:F1}, {mPos.y:F1}, {mPos.z:F1})\nDistance: {dist:F1} m";
                }
            }
        }

        // 2. World Props / Gourds (Emerald / Yellow Gems)
        if (_controlState.FullMapShowGourds)
        {
            uint colGourd = 0xFFFFD700;
            for (int i = 0; i < snapshot.GourdCount; i++)
            {
                var gPos = snapshot.GetGourdPosition(i);
                Vector2 sp = worldOriginOnScreen + new Vector2(gPos.x, -gPos.z) * zoom;
                if (sp.X < canvasP0.X - 20f || sp.X > canvasP0.X + canvasSize.X + 20f ||
                    sp.Y < canvasP0.Y - 20f || sp.Y > canvasP0.Y + canvasSize.Y + 20f)
                    continue;

                drawList.AddCircleFilled(sp, 4.0f, colGourd, 16);
                drawList.AddCircle(sp, 6.0f, 0x88FFD700, 16, 1.0f);

                int propId = snapshot.GetGourdPropId(i);
                string gName = propId >= 0 ? ((SaveablePropName)propId).ToString() : $"Gourd #{i + 1}";
                if (gName.StartsWith("gourd", StringComparison.OrdinalIgnoreCase))
                    gName = gName.Substring(5);

                drawList.AddText(new Vector2(sp.X + 7f, sp.Y - 6f), 0xCCFFD700, gName);

                if (isHovered && Vector2.Distance(io.MousePos, sp) <= 10f)
                {
                    float dist = snapshot.HasLocalPlayer ? UnityEngine.Vector3.Distance(snapshot.LocalPosition, gPos) : 0f;
                    hoveredTooltip = $"[Gourd Prop] {gName}\nWorld Pos: ({gPos.x:F1}, {gPos.y:F1}, {gPos.z:F1})\nDistance: {dist:F1} m";
                }
            }
        }

        // 3. Landmarks / Points of Interest (Purple / Violet Diamond Pins)
        if (_controlState.FullMapShowLandmarks)
        {
            uint colLandmark = 0xFFD066FF;
            for (int i = 0; i < snapshot.LandmarkCount; i++)
            {
                var lPos = snapshot.GetLandmarkPosition(i);
                Vector2 sp = worldOriginOnScreen + new Vector2(lPos.x, -lPos.z) * zoom;
                if (sp.X < canvasP0.X - 20f || sp.X > canvasP0.X + canvasSize.X + 20f ||
                    sp.Y < canvasP0.Y - 20f || sp.Y > canvasP0.Y + canvasSize.Y + 20f)
                    continue;

                drawList.AddQuadFilled(
                    new Vector2(sp.X, sp.Y - 5f),
                    new Vector2(sp.X + 5f, sp.Y),
                    new Vector2(sp.X, sp.Y + 5f),
                    new Vector2(sp.X - 5f, sp.Y),
                    colLandmark);

                string lName = $"POI #{i + 1}";
                drawList.AddText(new Vector2(sp.X + 8f, sp.Y - 6f), 0xCCD066FF, lName);

                if (isHovered && Vector2.Distance(io.MousePos, sp) <= 10f)
                {
                    float dist = snapshot.HasLocalPlayer ? UnityEngine.Vector3.Distance(snapshot.LocalPosition, lPos) : 0f;
                    hoveredTooltip = $"[Landmark] {lName}\nWorld Pos: ({lPos.x:F1}, {lPos.y:F1}, {lPos.z:F1})\nDistance: {dist:F1} m";
                }
            }
        }

        // 4. Connected Network Peers (Electric Cyan Circles with Heading Ticks)
        if (_controlState.FullMapShowPlayers)
        {
            uint colPeer = 0xFF00BFFF;
            uint colPeerHeading = 0xFF87CEEB;

            for (int i = 0; i < snapshot.PeerCount; i++)
            {
                var pPos = snapshot.GetPeerPosition(i);
                Vector2 sp = worldOriginOnScreen + new Vector2(pPos.x, -pPos.z) * zoom;
                if (sp.X < canvasP0.X - 20f || sp.X > canvasP0.X + canvasSize.X + 20f ||
                    sp.Y < canvasP0.Y - 20f || sp.Y > canvasP0.Y + canvasSize.Y + 20f)
                    continue;

                drawList.AddCircleFilled(sp, 5.5f, colPeer, 16);
                drawList.AddCircle(sp, 7.5f, 0x8800BFFF, 16, 1.0f);

                float pHeadingRad = snapshot.GetPeerHeading(i) * (MathF.PI / 180f);
                Vector2 pForward = new Vector2(MathF.Sin(pHeadingRad), -MathF.Cos(pHeadingRad));
                drawList.AddLine(sp, sp + pForward * 11f, colPeerHeading, 2.0f);

                string peerName = $"Peer #{i + 1}";
                drawList.AddText(new Vector2(sp.X + 9f, sp.Y - 7f), 0xEE87CEEB, peerName);

                if (isHovered && Vector2.Distance(io.MousePos, sp) <= 12f)
                {
                    float dist = snapshot.HasLocalPlayer ? UnityEngine.Vector3.Distance(snapshot.LocalPosition, pPos) : 0f;
                    hoveredTooltip = $"[Peer Player] {peerName}\nHeading: {snapshot.GetPeerHeading(i):F0}°\nWorld Pos: ({pPos.x:F1}, {pPos.y:F1}, {pPos.z:F1})\nDistance: {dist:F1} m";
                }
            }
        }

        // 5. Local Player Marker (Bright Lime-Green Triangle + Heading Cone)
        if (_controlState.FullMapShowPlayers && snapshot.HasLocalPlayer)
        {
            uint colPlayer = 0xFF00FF66;
            uint colPlayerHeading = 0xFF33FFAA;
            Vector2 playerScreenPos = worldOriginOnScreen + new Vector2(snapshot.LocalPosition.x, -snapshot.LocalPosition.z) * zoom;

            float headingRad = snapshot.LocalHeading * (MathF.PI / 180f);
            Vector2 forward = new Vector2(MathF.Sin(headingRad), -MathF.Cos(headingRad));
            // Clockwise 90 deg in screen coords (X right, Y down): (-Y, X)
            Vector2 right = new Vector2(-forward.Y, forward.X);

            // Heading pointer cone
            Vector2 tip = playerScreenPos + forward * 14f;
            Vector2 baseL = playerScreenPos - forward * 7f - right * 7f;
            Vector2 baseR = playerScreenPos - forward * 7f + right * 7f;

            drawList.AddTriangleFilled(tip, baseL, baseR, colPlayer);
            drawList.AddCircleFilled(playerScreenPos, 3.5f, 0xFFFFFFFF);
            drawList.AddCircle(playerScreenPos, 14f, colPlayerHeading, 24, 1.5f);

            drawList.AddText(new Vector2(playerScreenPos.X + 16f, playerScreenPos.Y - 8f), colPlayer, "YOU (Local Player)");

            if (isHovered && Vector2.Distance(io.MousePos, playerScreenPos) <= 16f)
            {
                hoveredTooltip = $"[Local Player]\nHeading: {snapshot.LocalHeading:F1}°\nWorld: ({snapshot.LocalPosition.x:F1}, {snapshot.LocalPosition.y:F1}, {snapshot.LocalPosition.z:F1})";
            }
        }

        drawList.PopClipRect();

        // Advance cursor past canvas
        ImGui.SetCursorScreenPos(new Vector2(canvasP0.X, canvasP0.Y + canvasSize.Y + 4f));

        // --- Bottom Coordinate & Telemetry Footer ---
        DrawCoordinatesFooter(in snapshot, worldOriginOnScreen, zoom, canvasP0, canvasSize, io);

        // Display Hover Tooltip
        if (!string.IsNullOrEmpty(hoveredTooltip))
        {
            ImGui.SetTooltip(hoveredTooltip);
        }
    }

    private static void DrawCoordinatesFooter(
        in MinimapSnapshot snapshot,
        Vector2 worldOriginOnScreen,
        float zoom,
        Vector2 canvasP0,
        Vector2 canvasSize,
        ImGuiIOPtr io)
    {
        if (snapshot.HasLocalPlayer)
        {
            string cardinal = GetCardinalDirection(snapshot.LocalHeading);
            ImGui.TextColored(new Vector4(0.2f, 1.0f, 0.4f, 1.0f),
                $"Local: X: {snapshot.LocalPosition.x:F1} | Y (Alt): {snapshot.LocalPosition.y:F1} | Z: {snapshot.LocalPosition.z:F1}  [{cardinal} {snapshot.LocalHeading:F0}°]");
        }
        else
        {
            TextDisabledWrapped("Local Player: Waiting for scene spawn...");
        }

        // Cursor World Coordinates on right
        if (io.MousePos.X >= canvasP0.X && io.MousePos.X <= canvasP0.X + canvasSize.X &&
            io.MousePos.Y >= canvasP0.Y && io.MousePos.Y <= canvasP0.Y + canvasSize.Y)
        {
            float cursorWorldX = (io.MousePos.X - worldOriginOnScreen.X) / zoom;
            float cursorWorldZ = -(io.MousePos.Y - worldOriginOnScreen.Y) / zoom;
            ImGui.SameLine(canvasSize.X - 220f);
            ImGui.TextDisabled($"Cursor World: ({cursorWorldX:F0}, {cursorWorldZ:F0})");
        }
    }

    private static string GetCardinalDirection(float degrees)
    {
        degrees = (degrees % 360f + 360f) % 360f;
        return degrees switch
        {
            >= 337.5f or < 22.5f => "N",
            >= 22.5f and < 67.5f => "NE",
            >= 67.5f and < 112.5f => "E",
            >= 112.5f and < 157.5f => "SE",
            >= 157.5f and < 202.5f => "S",
            >= 202.5f and < 247.5f => "SW",
            >= 247.5f and < 292.5f => "W",
            _ => "NW"
        };
    }
}
