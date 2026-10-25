using System;
using System.Numerics;
using BigWalk.AIO.State;
using DearImGuiInjectionPlus;
using DII = global::DearImGuiInjectionPlus.DearImGuiInjectionPlus;
using ImGuiNET;

namespace BigWalk.AIO.Tabs;

/// <summary>
/// Telemetry & Live Monitor Tab.
/// Demonstrates zero-allocation reading from <see cref="StructSnapshotPipeline{T}"/>,
/// live status readouts, diagnostic badges, and a rolling frame-time sparkline plot.
/// </summary>
public class TelemetryTab : IAioTab
{
    public string TabName => "Telemetry & Live Monitor";

    private readonly StructSnapshotPipeline<GameTelemetrySnapshot> _pipeline;
    private const int HistoryCapacity = 100;
    private readonly float[] _frameTimeHistory = new float[HistoryCapacity];
    private int _historyOffset;

    // Pre-cached color vectors to avoid per-frame heap allocations
    private static readonly Vector4 BadgeBgOnline = new(0.12f, 0.40f, 0.20f, 0.85f);
    private static readonly Vector4 BadgeTextOnline = new(0.30f, 1.00f, 0.50f, 1.00f);
    private static readonly Vector4 BadgeBgStandby = new(0.40f, 0.30f, 0.10f, 0.85f);
    private static readonly Vector4 BadgeTextStandby = new(1.00f, 0.80f, 0.20f, 1.00f);
    private static readonly Vector4 BadgeBgInactive = new(0.30f, 0.30f, 0.30f, 0.85f);
    private static readonly Vector4 BadgeTextInactive = new(0.70f, 0.70f, 0.70f, 1.00f);

    private static readonly Vector4 FpsGood = new(0.20f, 0.90f, 0.40f, 1.00f);
    private static readonly Vector4 FpsWarn = new(1.00f, 0.80f, 0.20f, 1.00f);
    private static readonly Vector4 FpsBad = new(1.00f, 0.30f, 0.30f, 1.00f);

    public TelemetryTab(StructSnapshotPipeline<GameTelemetrySnapshot> pipeline)
    {
        _pipeline = pipeline;
    }

    public void Draw()
    {
        var snapshot = _pipeline.GetLatest();

        // Push latest frame time into rolling buffer
        float deltaMs = snapshot.DeltaTime * 1000f;
        _frameTimeHistory[_historyOffset] = deltaMs;
        _historyOffset = (_historyOffset + 1) % HistoryCapacity;

        DrawHeaderBadges(snapshot);
        ImGui.Separator();

        DrawFeatureStatusBadges(snapshot);
        ImGui.Separator();

        DrawPerformanceSection(snapshot, deltaMs);
        ImGui.Separator();

        DrawPlayerTelemetrySection(snapshot);
        ImGui.Separator();

        DrawEnvironmentSection(snapshot);
    }

    private static void DrawHeaderBadges(in GameTelemetrySnapshot snapshot)
    {
        ImGui.TextUnformatted("System Health:");
        ImGui.SameLine();
        DrawBadge("LOCK-FREE PIPELINE", "ACTIVE", BadgeBgOnline, BadgeTextOnline);
        ImGui.SameLine();
        DrawBadge("IL2CPP INTEROP", "STABLE", BadgeBgOnline, BadgeTextOnline);
        ImGui.SameLine();

        bool isCapturing = DII.WantCaptureMouse || DII.WantCaptureKeyboard;
        if (isCapturing)
        {
            DrawBadge("INPUT HOOK", "CAPTURING", BadgeBgStandby, BadgeTextStandby);
        }
        else
        {
            DrawBadge("INPUT HOOK", "STANDBY", BadgeBgOnline, BadgeTextOnline);
        }
    }

    private static void DrawFeatureStatusBadges(in GameTelemetrySnapshot snapshot)
    {
        ImGui.TextUnformatted("Active Fixtures:");
        ImGui.SameLine();

        if (snapshot.InfiniteClimbActive)
            DrawBadge("INFINITE CLIMB", "ACTIVE", BadgeBgOnline, BadgeTextOnline);
        else
            DrawBadge("INFINITE CLIMB", "OFF", BadgeBgInactive, BadgeTextInactive);

        ImGui.SameLine();
        if (snapshot.NoFallDazeActive)
            DrawBadge("FALL DAZE", "SUPPRESSED", BadgeBgOnline, BadgeTextOnline);
        else
            DrawBadge("FALL DAZE", "NORMAL", BadgeBgInactive, BadgeTextInactive);

        ImGui.Spacing();
        ImGui.TextUnformatted("Visual & Flight:");
        ImGui.SameLine();

        if (snapshot.FullbrightActive)
            DrawBadge("FULLBRIGHT", "ACTIVE", BadgeBgOnline, BadgeTextOnline);
        else
            DrawBadge("FULLBRIGHT", "OFF", BadgeBgInactive, BadgeTextInactive);

        ImGui.SameLine();
        if (snapshot.FreeCamActive)
            DrawBadge("FREE-CAM", "DETACHED", BadgeBgStandby, BadgeTextStandby);
        else
            DrawBadge("FREE-CAM", "ATTACHED", BadgeBgInactive, BadgeTextInactive);
    }

    private void DrawPerformanceSection(in GameTelemetrySnapshot snapshot, float deltaMs)
    {
        if (ImGui.CollapsingHeader("Live Performance & Frame Times", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // FPS colored readout
            Vector4 fpsColor = snapshot.Fps switch
            {
                >= 55.0f => FpsGood,
                >= 30.0f => FpsWarn,
                _ => FpsBad
            };

            ImGui.TextUnformatted("Engine Framerate:");
            ImGui.SameLine();
            ImGui.TextColored(fpsColor, $"{snapshot.Fps:F1} FPS");
            ImGui.SameLine();
            ImGui.TextDisabled($"({deltaMs:F2} ms / frame) | Frame #{snapshot.FrameCount}");

            // Live Sparkline / PlotLines with zero per-frame collection allocations
            ImGui.PlotLines(
                "##FrameTimeSparkline",
                ref _frameTimeHistory[0],
                HistoryCapacity,
                _historyOffset,
                $"{deltaMs:F1} ms",
                0.0f,
                33.3f, // 0 to ~30 FPS line
                new Vector2(-1, 65));

            ImGui.TextWrapped("Sparkline Range: 0.0 ms to 33.3 ms (60 FPS ~ 16.6 ms, 30 FPS ~ 33.3 ms)");
        }
    }

    private static void DrawPlayerTelemetrySection(in GameTelemetrySnapshot snapshot)
    {
        if (ImGui.CollapsingHeader("Player Coordinates & Status", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("TelemetryGrid", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 180f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableHeadersRow();

                // Character Attached
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Local Player Attached");
                ImGui.TableNextColumn();
                ImGui.TextColored(snapshot.HasCharacter ? FpsGood : FpsWarn, snapshot.HasCharacter ? "YES (WorldManager.localPlayerCharacter)" : "NO (Waiting for scene spawn)");

                // Position
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("World Position (X, Y, Z)");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"X: {snapshot.PlayerPosition.x:F2}, Y: {snapshot.PlayerPosition.y:F2}, Z: {snapshot.PlayerPosition.z:F2}");

                // Velocity
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Velocity");
                ImGui.TableNextColumn();
                float speed = snapshot.PlayerVelocity.magnitude;
                ImGui.TextUnformatted($"Speed: {speed:F2} m/s (X: {snapshot.PlayerVelocity.x:F1}, Y: {snapshot.PlayerVelocity.y:F1}, Z: {snapshot.PlayerVelocity.z:F1})");

                // Grounded status
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Grounded Status");
                ImGui.TableNextColumn();
                if (snapshot.IsPlayerGrounded)
                {
                    ImGui.TextColored(FpsGood, "Grounded [YES]");
                }
                else
                {
                    ImGui.TextColored(FpsWarn, "Airborne [NO]");
                }

                // Locomotion Multipliers
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Tuning Multipliers");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"Speed: {snapshot.SpeedMultiplier:F2}x | Jump: {snapshot.JumpMultiplier:F2}x");

                ImGui.EndTable();
            }
        }
    }

    private static void DrawEnvironmentSection(in GameTelemetrySnapshot snapshot)
    {
        if (ImGui.CollapsingHeader("Runtime & Environment", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.LabelText("Active Scene", snapshot.SceneName ?? "N/A");
            ImGui.LabelText("Unity Version", snapshot.UnityVersion ?? "N/A");
            ImGui.LabelText("Graphics Backend", snapshot.GraphicsBackend ?? "N/A");
            ImGui.LabelText("Game Pause State", snapshot.IsGamePaused ? "PAUSED (Time.timeScale == 0)" : "RUNNING");
        }
    }

    private static void DrawBadge(string category, string status, Vector4 bg, Vector4 text)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg);
        ImGui.PushStyleColor(ImGuiCol.Text, text);
        ImGui.SmallButton($"[{category}: {status}]");
        ImGui.PopStyleColor(4);
    }
}
