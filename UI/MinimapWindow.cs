using System;
using System.Numerics;
using BigWalk.AIO.State;
using DearImGuiInjectionPlus;
using ImGuiNET;

namespace BigWalk.AIO.UI;

/// <summary>
/// Interactive DirectX swapchain-rendered 2D radar minimap overlay.
/// Projects 3D world positions of the local player, connected network peers,
/// and quest landmarks / gourds into 2D canvas space with zero GC allocations.
/// </summary>
public class MinimapWindow : IImGuiWindow
{
    public string Id => "bigwalk_aio_minimap";
    public string Title => "World Radar Minimap";
    public bool IsOpen { get; set; } = true;

    private readonly StructSnapshotPipeline<MinimapSnapshot> _pipeline;
    private readonly PlayerControlState _controlState;

    public MinimapWindow(StructSnapshotPipeline<MinimapSnapshot> pipeline, PlayerControlState controlState)
    {
        _pipeline = pipeline;
        _controlState = controlState;
    }

    public void OnRender()
    {
        if (!IsOpen || _controlState?.MinimapVisible == false)
            return;

        ImGui.SetNextWindowSize(new Vector2(280, 310), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(180, 200), new Vector2(800, 800));

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

        if (ImGui.Begin(Title, flags))
        {
            var snapshot = _pipeline.GetLatest();

            // Quick Toolbar at top of radar window
            DrawControlsHeader();

            ImGui.Separator();

            // Render Radar Canvas
            DrawRadarCanvas(in snapshot);
        }

        ImGui.End();
    }

    private void DrawControlsHeader()
    {
        float zoom = _controlState.MinimapZoom;
        ImGui.SetNextItemWidth(90f);
        if (ImGui.SliderFloat("##Zoom", ref zoom, 0.1f, 2.0f, "%.2fx"))
        {
            _controlState.MinimapZoom = zoom;
        }

        ImGui.SameLine();

        bool headingUp = _controlState.MinimapHeadingUp;
        if (ImGui.Checkbox(headingUp ? "Up: Heading" : "Up: North", ref headingUp))
        {
            _controlState.MinimapHeadingUp = headingUp;
        }

        ImGui.SameLine();

        bool landmarks = _controlState.MinimapShowLandmarks;
        if (ImGui.SmallButton(landmarks ? "Gourds [ON]" : "Gourds [OFF]"))
        {
            _controlState.MinimapShowLandmarks = !landmarks;
        }
    }

    private void DrawRadarCanvas(in MinimapSnapshot snapshot)
    {
        Vector2 canvasP0 = ImGui.GetCursorScreenPos();
        Vector2 canvasSize = ImGui.GetContentRegionAvail();

        if (canvasSize.X < 50 || canvasSize.Y < 50)
            return;

        var drawList = ImGui.GetWindowDrawList();
        Vector2 center = canvasP0 + canvasSize * 0.5f;
        float radius = MathF.Min(canvasSize.X, canvasSize.Y) * 0.48f;

        // 1. Radar Circle Background (Dark Obsidian Void)
        uint colBg = 0xEE0B0F14;
        uint colBorder = 0x9900E699;
        uint colRing = 0x3300E699;
        uint colAxis = 0x22FFFFFF;

        drawList.AddCircleFilled(center, radius, colBg, 64);
        drawList.AddCircle(center, radius, colBorder, 64, 1.5f);

        // 2. Concentric Range Rings (e.g. 25%, 50%, 75% range)
        drawList.AddCircle(center, radius * 0.25f, colRing, 32, 1.0f);
        drawList.AddCircle(center, radius * 0.50f, colRing, 48, 1.0f);
        drawList.AddCircle(center, radius * 0.75f, colRing, 64, 1.0f);

        // 3. Radar Axes & Cardinal Markers
        drawList.AddLine(new Vector2(center.X - radius, center.Y), new Vector2(center.X + radius, center.Y), colAxis, 1f);
        drawList.AddLine(new Vector2(center.X, center.Y - radius), new Vector2(center.X, center.Y + radius), colAxis, 1f);

        float rotOffset = _controlState.MinimapHeadingUp ? snapshot.LocalHeading * (MathF.PI / 180f) : 0f;

        Vector2 northPos = center + RotateVector(new Vector2(0, -radius + 12f), rotOffset);
        drawList.AddText(northPos - new Vector2(4f, 6f), 0xFF00FF99, "N");

        if (!snapshot.HasLocalPlayer)
        {
            string waitingMsg = "Waiting for player...";
            Vector2 textSz = ImGui.CalcTextSize(waitingMsg);
            drawList.AddText(center - textSz * 0.5f, 0x88FFFFFF, waitingMsg);
            return;
        }

        float zoom = _controlState.MinimapZoom;

        // 4. Draw Quest Landmarks / Gourds (Yellow/Gold Diamonds)
        if (_controlState.MinimapShowLandmarks)
        {
            uint colGourd = 0xFFFFD700;
            for (int i = 0; i < snapshot.LandmarkCount; i++)
            {
                var lPos = snapshot.GetLandmarkPosition(i);
                float dx = lPos.x - snapshot.LocalPosition.x;
                float dz = lPos.z - snapshot.LocalPosition.z;

                Vector2 offset = new Vector2(dx, -dz) * zoom;
                if (_controlState.MinimapHeadingUp)
                {
                    offset = RotateVector(offset, rotOffset);
                }

                if (offset.LengthSquared() <= (radius - 4f) * (radius - 4f))
                {
                    Vector2 p = center + offset;
                    drawList.AddQuadFilled(
                        new Vector2(p.X, p.Y - 4f),
                        new Vector2(p.X + 4f, p.Y),
                        new Vector2(p.X, p.Y + 4f),
                        new Vector2(p.X - 4f, p.Y),
                        colGourd);
                }
            }
        }

        // 5. Draw Connected Peers (Electric Blue Dots with Heading Ticks)
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
                    offset = RotateVector(offset, rotOffset);
                }

                if (offset.LengthSquared() <= (radius - 4f) * (radius - 4f))
                {
                    Vector2 p = center + offset;
                    drawList.AddCircleFilled(p, 4.5f, colPeer, 16);

                    // Direction tick
                    float peerHeadingRad = (snapshot.GetPeerHeading(i) - (_controlState.MinimapHeadingUp ? snapshot.LocalHeading : 0f)) * (MathF.PI / 180f);
                    Vector2 dir = new Vector2(MathF.Sin(peerHeadingRad), -MathF.Cos(peerHeadingRad)) * 8f;
                    drawList.AddLine(p, p + dir, colPeerHeading, 1.5f);

                    // Peer Index Label
                    drawList.AddText(new Vector2(p.X + 6f, p.Y - 6f), 0xCCFFFFFF, $"P{i + 1}");
                }
            }
        }

        // 6. Draw Local Player (Center Lime-Green Directional Triangle)
        uint colPlayer = 0xFF00FF66;
        float playerHeadingRad = _controlState.MinimapHeadingUp ? 0f : snapshot.LocalHeading * (MathF.PI / 180f);

        Vector2 forward = new Vector2(MathF.Sin(playerHeadingRad), -MathF.Cos(playerHeadingRad));
        Vector2 right = new Vector2(forward.Y, -forward.X);

        Vector2 tip = center + forward * 8f;
        Vector2 baseL = center - forward * 5f - right * 5f;
        Vector2 baseR = center - forward * 5f + right * 5f;

        drawList.AddTriangleFilled(tip, baseL, baseR, colPlayer);
        drawList.AddCircleFilled(center, 2.5f, 0xFFFFFFFF);

        // 7. Footer: Coordinates readout
        if (_controlState.MinimapShowCoordinates)
        {
            string coords = $"X: {snapshot.LocalPosition.x:F0} | Z: {snapshot.LocalPosition.z:F0}";
            drawList.AddText(new Vector2(canvasP0.X + 6f, canvasP0.Y + canvasSize.Y - 18f), 0xAAFFFFFF, coords);
        }
    }

    private static Vector2 RotateVector(Vector2 v, float angleRad)
    {
        float cos = MathF.Cos(angleRad);
        float sin = MathF.Sin(angleRad);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
