using System;
using System.Numerics;
using BigWalk.AIO.State;
using DearImGuiInjectionPlus;
using ImGuiNET;

namespace BigWalk.AIO.UI;

/// <summary>
/// Lightweight HUD Radar Minimap Overlay.
/// Pin-point 2D radar overlay projected in screen space with zero GC allocations.
/// Supports both fixed North-Up and dynamic Heading-Up tracking modes.
/// </summary>
public class MinimapWindow : IImGuiWindow
{
    public string Id => "bigwalk_aio_hud_minimap";
    public string Title => "World Radar";
    public bool IsOpen
    {
        get => _controlState?.HudMinimapVisible ?? false;
        set
        {
            if (_controlState != null) _controlState.HudMinimapVisible = value;
        }
    }

    private readonly StructSnapshotPipeline<MinimapSnapshot> _pipeline;
    private readonly PlayerControlState _controlState;

    public MinimapWindow(StructSnapshotPipeline<MinimapSnapshot> pipeline, PlayerControlState controlState)
    {
        _pipeline = pipeline;
        _controlState = controlState;
    }

    public void OnRender()
    {
        if (!IsOpen || _controlState?.HudMinimapVisible == false)
            return;

        var io = ImGui.GetIO();
        // Default position: top right corner with padding
        ImGui.SetNextWindowPos(new Vector2(io.DisplaySize.X - 250f, 25f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(230f, 260f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(160f, 180f), new Vector2(500f, 500f));

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

        bool open = IsOpen;
        if (ImGui.Begin("World Radar##HUD", ref open, flags))
        {
            IsOpen = open;
            var snapshot = _pipeline.GetLatest();

            DrawControlsHeader();
            ImGui.Separator();
            DrawRadarCanvas(in snapshot);
        }
        else
        {
            IsOpen = open;
        }

        ImGui.End();
    }

    private void DrawControlsHeader()
    {
        float zoom = _controlState.MinimapZoom;
        ImGui.SetNextItemWidth(65f);
        if (ImGui.SliderFloat("##Zoom", ref zoom, 0.1f, 2.0f, "%.1fx"))
        {
            _controlState.MinimapZoom = zoom;
        }

        ImGui.SameLine();

        bool headingUp = _controlState.MinimapHeadingUp;
        if (ImGui.Checkbox(headingUp ? "Heading" : "North", ref headingUp))
        {
            _controlState.MinimapHeadingUp = headingUp;
        }

        ImGui.SameLine();

        if (ImGui.SmallButton("Full [M]"))
        {
            _controlState.FullMapOpen = !_controlState.FullMapOpen;
        }
    }

    private void DrawRadarCanvas(in MinimapSnapshot snapshot)
    {
        Vector2 canvasP0 = ImGui.GetCursorScreenPos();
        Vector2 canvasSize = ImGui.GetContentRegionAvail();

        if (canvasSize.X < 40 || canvasSize.Y < 40) return;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 center = canvasP0 + canvasSize * 0.5f;
        float radius = MathF.Min(canvasSize.X, canvasSize.Y) * 0.47f;

        // 1. Radar Circle Background (Dark Obsidian Void)
        uint colBg = 0xEE0B0F14;
        uint colBorder = 0x9900E699;
        uint colRing = 0x3300E699;
        uint colAxis = 0x22FFFFFF;

        drawList.AddCircleFilled(center, radius, colBg, 64);
        drawList.AddCircle(center, radius, colBorder, 64, 1.5f);

        // 2. Concentric Range Rings
        drawList.AddCircle(center, radius * 0.33f, colRing, 32, 1.0f);
        drawList.AddCircle(center, radius * 0.66f, colRing, 48, 1.0f);

        // 3. Radar Axes
        drawList.AddLine(new Vector2(center.X - radius, center.Y), new Vector2(center.X + radius, center.Y), colAxis, 1f);
        drawList.AddLine(new Vector2(center.X, center.Y - radius), new Vector2(center.X, center.Y + radius), colAxis, 1f);

        // 4. North "N" Indicator
        // In North-Up: North is fixed straight UP (0, -radius)
        // In Heading-Up: Map rotates by -heading, so North rotates by -heading
        float rotOffset = _controlState.MinimapHeadingUp ? snapshot.LocalHeading * (MathF.PI / 180f) : 0f;
        Vector2 northPos = center + RotateVector(new Vector2(0, -radius + 10f), -rotOffset);
        drawList.AddText(northPos - new Vector2(4f, 6f), 0xFF00FF99, "N");

        if (!snapshot.HasLocalPlayer)
        {
            string waitingMsg = "Waiting...";
            Vector2 textSz = ImGui.CalcTextSize(waitingMsg);
            drawList.AddText(center - textSz * 0.5f, 0x88FFFFFF, waitingMsg);
            return;
        }

        float zoom = _controlState.MinimapZoom;

        // 5. Draw Quest Landmarks & Gourds (Gold / Orange Diamonds)
        if (_controlState.MinimapShowLandmarks)
        {
            uint colGourd = 0xFFFFD700;
            for (int i = 0; i < snapshot.GourdCount; i++)
            {
                var gPos = snapshot.GetGourdPosition(i);
                float dx = gPos.x - snapshot.LocalPosition.x;
                float dz = gPos.z - snapshot.LocalPosition.z;

                Vector2 offset = new Vector2(dx, -dz) * zoom;
                if (_controlState.MinimapHeadingUp)
                {
                    // Rotate world offset by -rotOffset so player heading remains Up
                    offset = RotateVector(offset, -rotOffset);
                }

                if (offset.LengthSquared() <= (radius - 4f) * (radius - 4f))
                {
                    Vector2 p = center + offset;
                    drawList.AddQuadFilled(
                        new Vector2(p.X, p.Y - 3f),
                        new Vector2(p.X + 3f, p.Y),
                        new Vector2(p.X, p.Y + 3f),
                        new Vector2(p.X - 3f, p.Y),
                        colGourd);
                }
            }

            // Quests / Medals (Orange)
            uint colMedal = 0xFFFFA500;
            for (int i = 0; i < snapshot.MedalCount; i++)
            {
                var mPos = snapshot.GetMedalPosition(i);
                float dx = mPos.x - snapshot.LocalPosition.x;
                float dz = mPos.z - snapshot.LocalPosition.z;

                Vector2 offset = new Vector2(dx, -dz) * zoom;
                if (_controlState.MinimapHeadingUp)
                {
                    offset = RotateVector(offset, -rotOffset);
                }

                if (offset.LengthSquared() <= (radius - 4f) * (radius - 4f))
                {
                    Vector2 p = center + offset;
                    drawList.AddCircleFilled(p, 3.5f, colMedal, 12);
                }
            }
        }

        // 6. Draw Connected Peers (Electric Blue Dots with Heading Ticks)
        if (_controlState.MinimapShowPeers)
        {
            uint colPeer = 0xFF00BFFF;
            uint colPeerHeading = 0xFF87CEEB;

            for (int i = 0; i < snapshot.PeerCount; i++)
            {
                var pPos = snapshot.GetPeerPosition(i);
                float dx = pPos.x - snapshot.LocalPosition.x;
                float dz = pPos.z - snapshot.LocalPosition.z;

                Vector2 offset = new Vector2(dx, -dz) * zoom;
                if (_controlState.MinimapHeadingUp)
                {
                    offset = RotateVector(offset, -rotOffset);
                }

                if (offset.LengthSquared() <= (radius - 4f) * (radius - 4f))
                {
                    Vector2 p = center + offset;
                    drawList.AddCircleFilled(p, 4f, colPeer, 16);

                    // Peer Direction tick
                    float peerHeadingDeg = snapshot.GetPeerHeading(i) - (_controlState.MinimapHeadingUp ? snapshot.LocalHeading : 0f);
                    float peerHeadingRad = peerHeadingDeg * (MathF.PI / 180f);
                    Vector2 dir = new Vector2(MathF.Sin(peerHeadingRad), -MathF.Cos(peerHeadingRad)) * 7f;
                    drawList.AddLine(p, p + dir, colPeerHeading, 1.5f);
                }
            }
        }

        // 7. Draw Local Player (Center Lime-Green Directional Triangle)
        uint colPlayer = 0xFF00FF66;
        float playerHeadingRad = _controlState.MinimapHeadingUp ? 0f : snapshot.LocalHeading * (MathF.PI / 180f);

        Vector2 forward = new Vector2(MathF.Sin(playerHeadingRad), -MathF.Cos(playerHeadingRad));
        // Clockwise 90 deg in screen space (X right, Y down): (-Y, X)
        Vector2 right = new Vector2(-forward.Y, forward.X);

        Vector2 tip = center + forward * 8f;
        Vector2 baseL = center - forward * 5f - right * 5f;
        Vector2 baseR = center - forward * 5f + right * 5f;

        drawList.AddTriangleFilled(tip, baseL, baseR, colPlayer);
        drawList.AddCircleFilled(center, 2.0f, 0xFFFFFFFF);

        // 8. Footer Coordinates
        if (_controlState.MinimapShowCoordinates)
        {
            string coords = $"X: {snapshot.LocalPosition.x:F0} | Z: {snapshot.LocalPosition.z:F0}";
            drawList.AddText(new Vector2(canvasP0.X + 4f, canvasP0.Y + canvasSize.Y - 16f), 0x99FFFFFF, coords);
        }
    }

    private static Vector2 RotateVector(Vector2 v, float angleRad)
    {
        float cos = MathF.Cos(angleRad);
        float sin = MathF.Sin(angleRad);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
