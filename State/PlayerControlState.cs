using System;
using System.Collections.Generic;
using DearImGuiInjectionPlus.BepInEx;
using UnityEngine;

namespace BigWalk.AIO.State;

/// <summary>
/// Manages live game control states, diagnostic overrides, and tuning parameters.
/// Changes triggered in the ImGui interface are persisted here and safely dispatched
/// or asserted asynchronously on the Unity main thread via <see cref="UnityMainThreadDispatcher"/>.
/// </summary>
public class PlayerControlState
{
    // --- Feature 1: Lightweight HUD Minimap Settings ---
    public bool HudMinimapVisible { get; set; } = true;
    public float MinimapZoom { get; set; } = 0.50f;
    public float MinimapRadius { get; set; } = 130f;
    public bool MinimapHeadingUp { get; set; } = false;
    public bool MinimapShowLandmarks { get; set; } = true;
    public bool MinimapShowPeers { get; set; } = true;
    public bool MinimapShowCoordinates { get; set; } = true;

    // --- Feature 1b: Expanded Fullscreen Map ('M' Hotkey) Settings & Filters ---
    private bool _fullMapOpen = false;
    public bool FullMapOpen
    {
        get => _fullMapOpen;
        set
        {
            if (_fullMapOpen != value)
            {
                _fullMapOpen = value;
                var mapWin = BigWalkAIOPlugin.Instance?.FullMapWindow;
                if (mapWin != null && mapWin.IsOpen != value)
                {
                    mapWin.IsOpen = value;
                }
            }
        }
    }
    public bool FullMapShowPlayers { get; set; } = true;
    public bool FullMapShowLandmarks { get; set; } = true;
    public bool FullMapShowMedals { get; set; } = true;
    public bool FullMapShowGourds { get; set; } = true;
    public float FullMapZoom { get; set; } = 1.0f;
    public float FullMapPanX { get; set; } = 0.0f;
    public float FullMapPanY { get; set; } = 0.0f;

    // --- Feature 2: Fullbright & Fog Neutralizer ---
    public bool FullbrightActive { get; set; } = false;
    public float FullbrightIntensity { get; set; } = 1.0f;
    public bool DisableFog { get; set; } = true;

    // --- Feature 3: Slope & Climb Traversability Bypass ---
    public bool InfiniteClimbActive { get; set; } = false;

    // --- Feature 4: Fall Hazard & Daze Suppression ---
    public bool NoFallDazeActive { get; set; } = false;

    // --- Feature 5: Kinematic Locomotion Tuning ---
    public float SpeedMultiplier { get; set; } = 1.0f;
    public float JumpMultiplier { get; set; } = 1.0f;

    // --- Feature 6: Free-Cam Integration ---
    public bool FreeCamDetached { get; set; } = false;
    public float FreeCamSpeed { get; set; } = 15.0f;
    public float FreeCamSensitivity { get; set; } = 3.0f;

    // --- General Sandbox Toggles ---
    public bool FreezeTime { get; set; } = false;
    public float FieldOfView { get; set; } = 90.0f;

    // --- Feature 8: Prop & Gourd Teleportation Dispatchers ---
    public void TeleportToCoordinates(Vector3 targetPos, Action<string> onLogged = null)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                var player = WorldManager.localPlayerCharacter;
                if (player != null)
                {
                    Vector3 dest = targetPos + Vector3.up * 1.5f;
                    player.transform.position = dest;
                    if (player.rb != null)
                    {
                        player.rb.linearVelocity = Vector3.zero;
                        player.rb.angularVelocity = Vector3.zero;
                    }
                    onLogged?.Invoke($"[Teleport] Local player translocated to ({dest.x:F1}, {dest.y:F1}, {dest.z:F1}).");
                }
                else
                {
                    onLogged?.Invoke("[Teleport Warning] No local player character active in scene.");
                }
            }
            catch (Exception ex)
            {
                onLogged?.Invoke($"[Teleport Error] Failed: {ex.Message}");
            }
        });
    }

    public void TeleportToGourd(SaveablePropName gourdName, Action<string> onLogged = null)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                var gourdMap = UnityEngine.Object.FindObjectOfType<GourdMap>();
                var posData = gourdMap != null ? gourdMap.positionData : null;
                if (posData == null)
                {
                    var allData = UnityEngine.Resources.FindObjectsOfTypeAll<GourdPositionData>();
                    if (allData != null && allData.Length > 0) posData = allData[0];
                }

                if (posData != null)
                {
                    Vector3 pos = posData.GetPosition(gourdName);
                    if (pos != Vector3.zero)
                    {
                        var player = WorldManager.localPlayerCharacter;
                        if (player != null)
                        {
                            Vector3 dest = pos + Vector3.up * 1.5f;
                            player.transform.position = dest;
                            if (player.rb != null)
                            {
                                player.rb.linearVelocity = Vector3.zero;
                                player.rb.angularVelocity = Vector3.zero;
                            }
                            onLogged?.Invoke($"[Teleport] Warped to '{gourdName}' at ({dest.x:F1}, {dest.y:F1}, {dest.z:F1}).");
                            return;
                        }
                    }
                    onLogged?.Invoke($"[Teleport] Gourd '{gourdName}' has no valid coordinates recorded in position data.");
                }
                else
                {
                    onLogged?.Invoke("[Teleport Warning] GourdPositionData catalog could not be located in scene.");
                }
            }
            catch (Exception ex)
            {
                onLogged?.Invoke($"[Teleport Error] Exception: {ex.Message}");
            }
        });
    }

    // --- Feature 6: Free-Cam Toggle Dispatcher ---
    public void ToggleFreeCam(Action<string> onLogged = null)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                var player = WorldManager.localPlayerCharacter;
                var cheater = player?.cheater;
                var camMover = cheater?.cameraCheatMover ?? UnityEngine.Object.FindObjectOfType<CameraCheatMover>();

                if (camMover != null)
                {
                    if (camMover._detached)
                    {
                        camMover.Attach();
                        camMover.movingSpeed = 10f; // Restore default speed
                        FreeCamDetached = false;
                        onLogged?.Invoke("[FreeCam] Attached camera back to player head.");
                    }
                    else
                    {
                        if (cheater != null)
                        {
                            cheater.cheatTextIsPrimed = true;
                            cheater.cheatButtonsIsPrimed = true;
                        }
                        // Zero out native movingSpeed so WASD does not simultaneously move the camera and character
                        camMover.movingSpeed = 0f;
                        camMover.sensitivity = FreeCamSensitivity;
                        camMover.Detach();
                        FreeCamDetached = true;
                        onLogged?.Invoke($"[FreeCam] Camera detached for free flight (Speed: {FreeCamSpeed:F1}, Sens: {FreeCamSensitivity:F1}). Controls: Arrow Keys + PgUp/PgDn (or R/F).");
                    }
                }
                else
                {
                    onLogged?.Invoke("[FreeCam Warning] CameraCheatMover component not found on player or scene.");
                }
            }
            catch (Exception ex)
            {
                onLogged?.Invoke($"[FreeCam Error] Failed to toggle: {ex.Message}");
            }
        });
    }

    // --- Feature 7: Native Developer Operation Runner Dispatcher ---
    public void ExecuteDebugOperation(string operationKey, Action<string> onLogged = null)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            try
            {
                var handler = Shipmates.RemotePlayerDebug.RemotePlayerDebugReceiver.GetHandler();
                if (handler == null)
                {
                    handler = Shipmates.RemotePlayerDebug.RemotePlayerDebugHandler.CreateNew();
                }

                if (handler != null && handler.ops != null && handler.ops.TryGetValue(operationKey, out var op))
                {
                    var req = new Shipmates.RemotePlayerDebug.RemotePlayerRequest();
                    var resp = op.Execute(req);
                    onLogged?.Invoke($"[DebugOp] Executed '{operationKey}' -> Response: {resp?.name ?? "OK"}");
                }
                else
                {
                    onLogged?.Invoke($"[DebugOp Warning] Operation '{operationKey}' not registered in RemotePlayerDebugHandler.");
                }
            }
            catch (Exception ex)
            {
                onLogged?.Invoke($"[DebugOp Error] Exception running '{operationKey}': {ex.Message}");
            }
        });
    }

    // --- Time & FOV Dispatchers ---
    public void ApplyFreezeTime(bool freeze, Action<string> onLogged = null)
    {
        FreezeTime = freeze;
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            Time.timeScale = freeze ? 0.0f : 1.0f;
            onLogged?.Invoke($"[Controls] Game time scale set to {(freeze ? "0.0 (Frozen)" : "1.0 (Normal)")}");
        });
    }

    public void ApplyFov(float fov, Action<string> onLogged = null)
    {
        FieldOfView = fov;
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            if (Camera.main != null)
            {
                Camera.main.fieldOfView = fov;
            }
            onLogged?.Invoke($"[Controls] Main Camera FOV set to {fov:F1}°");
        });
    }
}
