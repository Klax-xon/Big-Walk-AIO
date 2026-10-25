using System;
using BigWalk.AIO.State;
using DearImGuiInjectionPlus;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BigWalk.AIO.Core;

/// <summary>
/// IL2CPP-injected MonoBehaviour executing on the Unity main scripting thread.
/// Responsible for:
/// 1. Sampling engine state into lock-free telemetry & minimap snapshot pipelines.
/// 2. Continuous assertion of diagnostic overrides (Fullbright, Slope Traversability, Fall/Daze suppression, Speed/Jump tunings).
/// 3. Polling keyboard hotkeys (Menu: Insert, Radar: M, FreeCam: F2).
/// 4. Priming built-in cheat systems (CameraCheatMover, PlayerCheater).
/// </summary>
public class BigWalkAIORunner : MonoBehaviour
{
    private string _cachedSceneName = "DefaultScene";
    private float _nextSceneCheckTime;
    private float _nextLandmarkScanTime;
    private float _mockAngle;
    internal static bool _isQuitting;

    // --- Baseline Caching for State Restoration ---
    // Fullbright Baselines
    private bool _hasRenderSettingsBaseline;
    private AmbientMode _baseAmbientMode;
    private Color _baseAmbientLight;
    private float _baseAmbientIntensity;
    private bool _baseFog;
    private float _baseFogDensity;

    // Slope & Climb Baselines
    private bool _hasGroundBaseline;
    private bool _baseLimitSlope;
    private bool _baseLimitWater;
    private bool _baseSteepMaterialLimits;
    private float _baseAngleFullSteep;
    private float _baseAngleNoGrip;
    private float _baseAngleFullGrip;
    private float _baseAngleFullUphill;
    private float _baseAngleFullDownhill;

    // Fall & Daze Baselines
    private bool _hasFallerBaseline;
    private bool _baseIgnoreFalling;
    private float _baseSitPauseDuration;

    // Locomotion Tunings Baselines
    private IntPtr _lastTunedPlayerPtr = IntPtr.Zero;
    private bool _hasTuningsBaseline;
    private float _baseForwardSpeed = 4.0f;
    private float _baseForwardSprintSpeed = 7.0f;
    private float _baseSwimSpeed = 3.0f;
    private float _baseSwimSprintSpeed = 5.0f;
    private float _baseCrouchSpeed = 2.0f;
    private float _baseJumpForce = 5.0f;
    private float _baseMaxUpwardsVel = 7.0f;

    // Pre-allocated landmark cache for Minimap
    private readonly Vector3[] _cachedLandmarkPositions = new Vector3[MinimapSnapshot.MaxLandmarks];
    private int _cachedLandmarkCount;

    public BigWalkAIORunner(IntPtr handle) : base(handle) { }

    private void Awake()
    {
        _isQuitting = false;
        try
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(activeScene.name))
            {
                _cachedSceneName = activeScene.name;
            }
        }
        catch
        {
            _cachedSceneName = "ActiveScene";
        }
    }

    private void Update()
    {
        try
        {
            var controlState = BigWalkAIOPlugin.Instance?.ControlState;
            var localPlayer = WorldManager.localPlayerCharacter;

            // 1. Hotkey Detection on the Unity Main Thread
            HandleHotkeys(controlState);

            // 2. Continuous Diagnostic Fixture Assertions
            if (controlState != null)
            {
                AssertFullbright(controlState);
                AssertTraversability(controlState, localPlayer);
                AssertFallAndDaze(controlState, localPlayer);
                AssertLocomotionTunings(controlState, localPlayer);
                AssertFreeCamPriming(controlState, localPlayer);
                AssertTimeFreeze(controlState);
            }

            // 3. Telemetry Snapshot Publishing (Zero-allocation triple buffer)
            PublishTelemetrySnapshot(controlState, localPlayer);

            // 4. Minimap Radar Snapshot Publishing
            PublishMinimapSnapshot(localPlayer);
        }
        catch (Exception ex)
        {
            Log.Error($"[BigWalkAIORunner] Exception in Update: {ex}");
        }
    }

    #region Hotkey Handling
    private void HandleHotkeys(PlayerControlState state)
    {
        // Menu Toggle (Insert)
        var toggleKey = BigWalkAIOPlugin.ToggleKeyConfig?.Value ?? KeyCode.Insert;
        if (toggleKey == KeyCode.F1) toggleKey = KeyCode.Insert;

        if (Input.GetKeyDown(toggleKey) || Input.GetKeyDown(KeyCode.Insert))
        {
            global::DearImGuiInjectionPlus.DearImGuiInjectionPlus.ToggleCursorVisibility();
        }

        // Radar / Minimap Toggle (M)
        var minimapKey = BigWalkAIOPlugin.MinimapKeyConfig?.Value ?? KeyCode.M;
        if (Input.GetKeyDown(minimapKey))
        {
            if (BigWalkAIOPlugin.Instance?.MinimapWindow != null)
            {
                BigWalkAIOPlugin.Instance.MinimapWindow.IsOpen = !BigWalkAIOPlugin.Instance.MinimapWindow.IsOpen;
            }
            else if (state != null)
            {
                state.MinimapVisible = !state.MinimapVisible;
            }
        }

        // Free-Cam Toggle (F2)
        if (Input.GetKeyDown(KeyCode.F2))
        {
            state?.ToggleFreeCam(msg => Log.Info(msg));
        }
    }
    #endregion

    #region Feature 2: Fullbright & Fog Assertion
    private void AssertFullbright(PlayerControlState state)
    {
        if (state.FullbrightActive)
        {
            if (!_hasRenderSettingsBaseline)
            {
                _baseAmbientMode = RenderSettings.ambientMode;
                _baseAmbientLight = RenderSettings.ambientLight;
                _baseAmbientIntensity = RenderSettings.ambientIntensity;
                _baseFog = RenderSettings.fog;
                _baseFogDensity = RenderSettings.fogDensity;
                _hasRenderSettingsBaseline = true;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.ambientIntensity = state.FullbrightIntensity;

            if (state.DisableFog)
            {
                RenderSettings.fog = false;
                RenderSettings.fogDensity = 0.0f;
            }
        }
        else if (_hasRenderSettingsBaseline)
        {
            RenderSettings.ambientMode = _baseAmbientMode;
            RenderSettings.ambientLight = _baseAmbientLight;
            RenderSettings.ambientIntensity = _baseAmbientIntensity;
            RenderSettings.fog = _baseFog;
            RenderSettings.fogDensity = _baseFogDensity;
            _hasRenderSettingsBaseline = false;
        }
    }
    #endregion

    #region Feature 3: Slope & Climb Traversability Assertion
    private void AssertTraversability(PlayerControlState state, PlayerCharacter player)
    {
        var ground = player?.ground;
        if (ground == null)
        {
            _hasGroundBaseline = false;
            return;
        }

        if (state.InfiniteClimbActive)
        {
            if (!_hasGroundBaseline)
            {
                _baseLimitSlope = ground.limitSlopeMovement;
                _baseLimitWater = ground.limitWaterMovement;
                _baseSteepMaterialLimits = ground.steepMaterialLimitsGrip;
                _baseAngleFullSteep = ground.angleFullSteep;
                _baseAngleNoGrip = ground.angleNoGrip;
                _baseAngleFullGrip = ground.angleFullGrip;
                _baseAngleFullUphill = ground.angleFullUphill;
                _baseAngleFullDownhill = ground.angleFullDownhill;
                _hasGroundBaseline = true;
            }

            ground.limitSlopeMovement = false;
            ground.limitWaterMovement = false;
            ground.steepMaterialLimitsGrip = false;
            ground.angleFullSteep = 90.0f;
            ground.angleNoGrip = 90.0f;
            ground.angleFullGrip = 90.0f;
            ground.angleFullUphill = 90.0f;
            ground.angleFullDownhill = 90.0f;
        }
        else if (_hasGroundBaseline)
        {
            ground.limitSlopeMovement = _baseLimitSlope;
            ground.limitWaterMovement = _baseLimitWater;
            ground.steepMaterialLimitsGrip = _baseSteepMaterialLimits;
            ground.angleFullSteep = _baseAngleFullSteep;
            ground.angleNoGrip = _baseAngleNoGrip;
            ground.angleFullGrip = _baseAngleFullGrip;
            ground.angleFullUphill = _baseAngleFullUphill;
            ground.angleFullDownhill = _baseAngleFullDownhill;
            _hasGroundBaseline = false;
        }
    }
    #endregion

    #region Feature 4: Fall Hazard & Daze Assertion
    private void AssertFallAndDaze(PlayerControlState state, PlayerCharacter player)
    {
        var faller = player?.faller;
        if (faller == null)
        {
            _hasFallerBaseline = false;
            return;
        }

        if (state.NoFallDazeActive)
        {
            if (!_hasFallerBaseline)
            {
                _baseIgnoreFalling = faller.ignoreFalling;
                _baseSitPauseDuration = faller.sitPauseDuration;
                _hasFallerBaseline = true;
            }

            faller.ignoreFalling = true;
            faller.sitPauseDuration = 0.0f;

            if (faller.isDazed)
            {
                faller.ClearNextFall();
            }
        }
        else if (_hasFallerBaseline)
        {
            faller.ignoreFalling = _baseIgnoreFalling;
            faller.sitPauseDuration = _baseSitPauseDuration;
            _hasFallerBaseline = false;
        }
    }
    #endregion

    #region Feature 5: Kinematic Locomotion Tunings Assertion
    private void AssertLocomotionTunings(PlayerControlState state, PlayerCharacter player)
    {
        var tunings = player?.tunings;
        if (tunings == null)
        {
            _hasTuningsBaseline = false;
            _lastTunedPlayerPtr = IntPtr.Zero;
            return;
        }

        IntPtr currentPtr = player.Pointer;
        if (currentPtr != _lastTunedPlayerPtr || !_hasTuningsBaseline)
        {
            _lastTunedPlayerPtr = currentPtr;
            _baseForwardSpeed = tunings.forwardSpeed > 0f ? tunings.forwardSpeed : 4.0f;
            _baseForwardSprintSpeed = tunings.forwardSprintSpeed > 0f ? tunings.forwardSprintSpeed : 7.0f;
            _baseSwimSpeed = tunings.swimForwardSpeed > 0f ? tunings.swimForwardSpeed : 3.0f;
            _baseSwimSprintSpeed = tunings.swimForwardSprintSpeed > 0f ? tunings.swimForwardSprintSpeed : 5.0f;
            _baseCrouchSpeed = tunings.crouchForwardSpeed > 0f ? tunings.crouchForwardSpeed : 2.0f;
            _baseJumpForce = tunings.jumpForce > 0f ? tunings.jumpForce : 5.0f;
            _baseMaxUpwardsVel = tunings.maxUpwardsVelocity > 0f ? tunings.maxUpwardsVelocity : 7.0f;
            _hasTuningsBaseline = true;
        }

        float sMult = state.SpeedMultiplier;
        float jMult = state.JumpMultiplier;

        tunings.forwardSpeed = _baseForwardSpeed * sMult;
        tunings.forwardSprintSpeed = _baseForwardSprintSpeed * sMult;
        tunings.swimForwardSpeed = _baseSwimSpeed * sMult;
        tunings.swimForwardSprintSpeed = _baseSwimSprintSpeed * sMult;
        tunings.crouchForwardSpeed = _baseCrouchSpeed * sMult;
        tunings.jumpForce = _baseJumpForce * jMult;
        tunings.maxUpwardsVelocity = _baseMaxUpwardsVel * jMult;
    }
    #endregion

    #region Feature 6: Free-Cam Assertion
    private void AssertFreeCamPriming(PlayerControlState state, PlayerCharacter player)
    {
        var cheater = player?.cheater;
        if (cheater != null)
        {
            cheater.cheatTextIsPrimed = true;
            cheater.cheatButtonsIsPrimed = true;

            var camMover = cheater.cameraCheatMover;
            if (camMover != null)
            {
                state.FreeCamDetached = camMover._detached;
                if (camMover._detached)
                {
                    camMover.movingSpeed = state.FreeCamSpeed;
                    camMover.sensitivity = state.FreeCamSensitivity;
                }
            }
        }
    }
    #endregion

    #region Time Freeze Assertion
    private void AssertTimeFreeze(PlayerControlState state)
    {
        if (state.FreezeTime && Time.timeScale != 0f)
        {
            Time.timeScale = 0f;
        }
    }
    #endregion

    #region Telemetry Snapshot Publishing
    private void PublishTelemetrySnapshot(PlayerControlState controlState, PlayerCharacter localPlayer)
    {
        var pipeline = BigWalkAIOPlugin.Instance?.TelemetryPipeline;
        if (pipeline == null) return;

        float now = Time.unscaledTime;
        if (now >= _nextSceneCheckTime)
        {
            _nextSceneCheckTime = now + 1.0f;
            try
            {
                var scene = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(scene.name))
                {
                    _cachedSceneName = scene.name;
                }
            }
            catch
            {
                // Ignored during transitions
            }
        }

        float dt = Time.unscaledDeltaTime;
        float fps = dt > 0.0001f ? (1.0f / dt) : 60.0f;

        Vector3 playerPos = Vector3.zero;
        Vector3 playerVel = Vector3.zero;
        bool isGrounded = true;
        bool hasChar = false;

        if (localPlayer != null)
        {
            hasChar = true;
            playerPos = localPlayer.transform.position;
            if (localPlayer.rb != null)
            {
                playerVel = localPlayer.rb.linearVelocity;
            }
            if (localPlayer.ground != null)
            {
                isGrounded = localPlayer.ground.isGrounded;
            }
        }
        else if (Camera.main != null)
        {
            playerPos = Camera.main.transform.position;
        }
        else
        {
            _mockAngle += dt * 0.5f;
            playerPos = new Vector3(Mathf.Cos(_mockAngle) * 15f, 1.2f, Mathf.Sin(_mockAngle) * 15f);
            playerVel = new Vector3(-Mathf.Sin(_mockAngle) * 7.5f, 0f, Mathf.Cos(_mockAngle) * 7.5f);
        }

        var snapshot = new GameTelemetrySnapshot
        {
            Fps = fps,
            DeltaTime = dt,
            SmoothDeltaTime = Time.smoothDeltaTime,
            HasCharacter = hasChar,
            PlayerPosition = playerPos,
            PlayerVelocity = playerVel,
            PlayerHealth = controlState?.GodMode == true ? 100.0f : 85.0f,
            PlayerMaxHealth = 100.0f,
            ActiveEnemies = 0,
            IsPlayerGrounded = isGrounded,
            IsGamePaused = Mathf.Approximately(Time.timeScale, 0f),
            FrameCount = (ulong)Time.frameCount,
            InfiniteClimbActive = controlState?.InfiniteClimbActive ?? false,
            NoFallDazeActive = controlState?.NoFallDazeActive ?? false,
            FullbrightActive = controlState?.FullbrightActive ?? false,
            FreeCamActive = controlState?.FreeCamDetached ?? false,
            SpeedMultiplier = controlState?.SpeedMultiplier ?? 1.0f,
            JumpMultiplier = controlState?.JumpMultiplier ?? 1.0f,
            SceneName = _cachedSceneName,
            UnityVersion = Application.unityVersion,
            GraphicsBackend = SystemInfo.graphicsDeviceType.ToString()
        };

        pipeline.Publish(in snapshot);
    }
    #endregion

    #region Feature 1: Minimap Radar Snapshot Publishing
    private void PublishMinimapSnapshot(PlayerCharacter localPlayer)
    {
        var minimapPipeline = BigWalkAIOPlugin.Instance?.MinimapPipeline;
        if (minimapPipeline == null) return;

        // Periodically refresh landmark coordinates (every 5 seconds)
        float now = Time.unscaledTime;
        if (now >= _nextLandmarkScanTime)
        {
            _nextLandmarkScanTime = now + 5.0f;
            ScanLandmarks();
        }

        var snapshot = MinimapSnapshot.CreateDefault();

        if (localPlayer != null)
        {
            snapshot.HasLocalPlayer = true;
            snapshot.LocalPosition = localPlayer.transform.position;
            snapshot.LocalHeading = localPlayer.transform.eulerAngles.y;

            // Gather peers from PlayerCharacter.allPlayerCharacters
            var all = PlayerCharacter.allPlayerCharacters;
            if (all != null)
            {
                int count = 0;
                for (int i = 0; i < all.Count && count < MinimapSnapshot.MaxPeers; i++)
                {
                    var peer = all[i];
                    if (peer != null && !peer.isLocalPlayer)
                    {
                        snapshot.SetPeer(count, peer.transform.position, peer.transform.eulerAngles.y);
                        count++;
                    }
                }
                snapshot.PeerCount = count;
            }

            // Populate cached landmarks
            snapshot.LandmarkCount = _cachedLandmarkCount;
            for (int i = 0; i < _cachedLandmarkCount; i++)
            {
                snapshot.SetLandmark(i, _cachedLandmarkPositions[i]);
            }
        }

        minimapPipeline.Publish(in snapshot);
    }

    private void ScanLandmarks()
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

            if (posData != null && posData.gourdListings != null)
            {
                var listings = posData.gourdListings;
                int count = 0;
                for (int i = 0; i < listings.Count && count < MinimapSnapshot.MaxLandmarks; i++)
                {
                    var listing = listings[i];
                    Vector3 pos = listing.position;
                    if (pos != Vector3.zero)
                    {
                        _cachedLandmarkPositions[count] = pos;
                        count++;
                    }
                }
                _cachedLandmarkCount = count;
            }
        }
        catch
        {
            // Fallback during scene transitions
        }
    }
    #endregion

    private void OnApplicationQuit()
    {
        _isQuitting = true;
        BigWalkAIOPlugin.Instance?.Teardown();
    }

    private void OnDestroy()
    {
        if (!_isQuitting)
        {
            Log.Warning("[BigWalkAIORunner] Runner destroyed outside of application quit. Respawning...");
            BigWalkAIOPlugin.Instance?.EnsureRunnerAlive();
            return;
        }

        BigWalkAIOPlugin.Instance?.Teardown();
    }
}
