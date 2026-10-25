using System;
using System.Runtime.InteropServices;
using BigWalk.AIO.State;
using DearImGuiInjectionPlus;
using HouseHouse.Medal;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using ImGuiNET;
using DII = DearImGuiInjectionPlus.DearImGuiInjectionPlus;

namespace BigWalk.AIO.Core;

/// <summary>
/// IL2CPP-injected MonoBehaviour executing on the Unity main scripting thread.
/// Responsible for:
/// 1. Sampling engine state into lock-free telemetry, minimap, and full world map snapshot pipelines.
/// 2. Continuous assertion of diagnostic overrides (Fullbright with Enviro 3 coordination, Slope Traversability, Fall/Daze suppression, Speed/Jump tunings).
/// 3. Remapped Free-Cam flight controls (Arrow Keys + Elevation keys) decoupled from WASD.
/// 4. Polling keyboard hotkeys (Menu: Insert, Island Map: M, FreeCam: F2).
/// 5. Priming built-in cheat systems (CameraCheatMover, PlayerCheater).
/// </summary>
public class BigWalkAIORunner : MonoBehaviour
{
    public static BigWalkAIORunner Instance { get; private set; }

    private string _cachedSceneName = "DefaultScene";
    private float _nextSceneCheckTime;
    private float _nextWorldScanTime;
    private float _mockAngle;
    internal static bool _isQuitting;

    // --- Baseline Caching for State Restoration ---
    // Fullbright & Enviro 3 Baselines
    private bool _hasRenderSettingsBaseline;
    private AmbientMode _baseAmbientMode;
    private Color _baseAmbientLight;
    private float _baseAmbientIntensity;
    private bool _baseFog;
    private float _baseFogDensity;

    private bool _hasEnviroLightingBaseline;
    private bool _baseEnviroSetAmbient;
    private bool _baseEnviroSetDirect;
    private float _baseEnviroAmbientMod;
    private float _baseEnviroDirectMod;

    private bool _hasDirectLightBaseline;
    private float _baseDirectLightIntensity;

    // Slope & Climb Baselines
    private bool _hasGroundBaseline;
    private bool _baseLimitSlope;
    private bool _baseLimitWater;
    private bool _baseSteepMaterialLimits;
    private float _baseSteepMaterialMaxGrip;
    private float _baseAngleFullSteep;
    private float _baseAngleJumpLimitSteep;
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

    // Pre-allocated World Objects Cache for Map & Minimap (Zero GC allocation per frame)
    private readonly Vector3[] _cachedLandmarkPositions = new Vector3[MinimapSnapshot.MaxLandmarks];
    private int _cachedLandmarkCount;

    private readonly Vector3[] _cachedMedalPositions = new Vector3[MinimapSnapshot.MaxMedals];
    private readonly int[] _cachedMedalTypes = new int[MinimapSnapshot.MaxMedals];
    private int _cachedMedalCount;

    private readonly Vector3[] _cachedGourdPositions = new Vector3[MinimapSnapshot.MaxGourds];
    private readonly int[] _cachedGourdPropIds = new int[MinimapSnapshot.MaxGourds];
    private int _cachedGourdCount;

    public BigWalkAIORunner(IntPtr handle) : base(handle) { }

    private void Awake()
    {
        Instance = this;
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
            // 1. Hotkey Detection on the Unity Main Thread
            HandleHotkeys();

            // 2. Continuous Diagnostic Fixture Assertions
            AssertFullbright();
            AssertTraversability();
            AssertFallAndDaze();
            AssertLocomotionTunings();
            AssertFreeCamPriming();
            HandleFreeCamFlightControls();
            AssertTimeFreeze();
            AssertInputSuppression();

            // 3. Map Texture Pipeline Update
            MapTextureManager.Update();

            // 4. Telemetry Snapshot Publishing
            PublishTelemetrySnapshot();

            // 5. Minimap Radar & Full World Map Snapshot Publishing
            PublishMinimapSnapshot();
        }
        catch (Exception ex)
        {
            Log.Error($"[BigWalkAIORunner] Exception in Update: {ex}");
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_INSERT = 0x2D;
    private const int VK_M = 0x4D;
    private bool _wasInsertDown;
    private bool _wasMDown;

    private static int KeyCodeToVk(KeyCode key)
    {
        if (key >= KeyCode.A && key <= KeyCode.Z) return 0x41 + (key - KeyCode.A);
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return 0x30 + (key - KeyCode.Alpha0);
        if (key >= KeyCode.F1 && key <= KeyCode.F12) return 0x70 + (key - KeyCode.F1);
        return key switch
        {
            KeyCode.Tab => 0x09,
            KeyCode.Space => 0x20,
            KeyCode.Insert => 0x2D,
            KeyCode.Return => 0x0D,
            KeyCode.Escape => 0x1B,
            _ => 0
        };
    }

    #region Hotkey Handling
    private void HandleHotkeys()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;

        // Main Menu Toggle (Insert) - Poll Win32 hardware key state to bypass WndProc hook swallowing
        bool insertPressed = false;
        try
        {
            short insertState = GetAsyncKeyState(VK_INSERT);
            bool isInsertDown = (insertState & 0x8000) != 0;
            if (isInsertDown && !_wasInsertDown)
            {
                insertPressed = true;
            }
            _wasInsertDown = isInsertDown;
        }
        catch { }

        // Also check configured toggle key via Unity Input if user remapped from Insert
        var toggleKey = BigWalkAIOPlugin.ToggleKeyConfig?.Value ?? KeyCode.Insert;
        if (toggleKey != KeyCode.Insert && Input.GetKeyDown(toggleKey))
        {
            insertPressed = true;
        }

        if (insertPressed)
        {
            var win = BigWalkAIOPlugin.Instance?.Window;
            if (win != null)
            {
                win.IsOpen = !win.IsOpen;
                if (win.IsOpen)
                {
                    var map = BigWalkAIOPlugin.Instance?.FullMapWindow;
                    if (map != null) map.IsOpen = false;
                }
                Log.Info($"[Hotkey] Toggle Main Menu -> {(win.IsOpen ? "OPEN" : "CLOSED")}");
            }
        }

        // Fullscreen Island Map Toggle (M) - Poll Win32 hardware key state & Unity Input
        bool mPressed = false;
        try
        {
            bool isTyping = DII.IsCursorVisible && ImGui.GetIO().WantTextInput;
            if (!isTyping)
            {
                var mapKey = BigWalkAIOPlugin.MinimapKeyConfig?.Value ?? KeyCode.M;
                int vk = KeyCodeToVk(mapKey);
                if (vk == 0) vk = VK_M;

                short mState = GetAsyncKeyState(vk);
                bool isMDown = (mState & 0x8000) != 0;
                if (isMDown && !_wasMDown)
                {
                    mPressed = true;
                }
                _wasMDown = isMDown;
            }
            else
            {
                _wasMDown = true;
            }
        }
        catch { }

        var minimapKey = BigWalkAIOPlugin.MinimapKeyConfig?.Value ?? KeyCode.M;
        if (minimapKey != KeyCode.M && Input.GetKeyDown(minimapKey))
        {
            mPressed = true;
        }
        else if (Input.GetKeyDown(KeyCode.M))
        {
            mPressed = true;
        }

        if (mPressed)
        {
            var fullMap = BigWalkAIOPlugin.Instance?.FullMapWindow;
            if (fullMap != null)
            {
                fullMap.IsOpen = !fullMap.IsOpen;
                if (fullMap.IsOpen)
                {
                    var win = BigWalkAIOPlugin.Instance?.Window;
                    if (win != null) win.IsOpen = false;
                }
                Log.Info($"[Hotkey] Toggle Big Map -> {(fullMap.IsOpen ? "OPEN" : "CLOSED")}");
            }
        }

        // Free-Cam Toggle (F2)
        if (Input.GetKeyDown(KeyCode.F2))
        {
            state?.ToggleFreeCam(msg => Log.Info(msg));
        }
    }
    #endregion

    #region Feature 2: Fullbright & Fog Assertion (with Enviro 3 Coordination)
    private void AssertFullbright()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;
        if (state == null) return;

        if (state.FullbrightActive)
        {
            // 1. Cache engine baseline parameters
            if (!_hasRenderSettingsBaseline)
            {
                _baseAmbientMode = RenderSettings.ambientMode;
                _baseAmbientLight = RenderSettings.ambientLight;
                _baseAmbientIntensity = RenderSettings.ambientIntensity;
                _baseFog = RenderSettings.fog;
                _baseFogDensity = RenderSettings.fogDensity;
                _hasRenderSettingsBaseline = true;
            }

            // 2. Cache & suppress Enviro 3 dynamic ambient/direct lighting updates
            try
            {
                var enviroManager = Enviro.EnviroManager.instance;
                if (enviroManager != null)
                {
                    var enviroLighting = enviroManager.Lighting;
                    var enviroSettings = enviroLighting != null ? enviroLighting.Settings : null;
                    if (enviroSettings != null)
                    {
                        if (!_hasEnviroLightingBaseline)
                        {
                            _baseEnviroSetAmbient = enviroSettings.setAmbientLighting;
                            _baseEnviroSetDirect = enviroSettings.setDirectLighting;
                            _baseEnviroAmbientMod = enviroSettings.ambientIntensityModifier;
                            _baseEnviroDirectMod = enviroSettings.directLightIntensityModifier;
                            _hasEnviroLightingBaseline = true;
                        }

                        // Suppress internal ~3 second ambient and direct update loops
                        enviroSettings.setAmbientLighting = false;
                        enviroSettings.setDirectLighting = false;
                    }

                    var enviroObjects = enviroManager.Objects;
                    if (enviroObjects != null && enviroObjects.directionalLight != null)
                    {
                        if (!_hasDirectLightBaseline)
                        {
                            _baseDirectLightIntensity = enviroObjects.directionalLight.intensity;
                            _hasDirectLightBaseline = true;
                        }
                        // Scale directional light by slider
                        enviroObjects.directionalLight.intensity = MathF.Max(_baseDirectLightIntensity, 1.0f) * state.FullbrightIntensity;
                    }
                }
            }
            catch
            {
                // Enviro 3 safe fallback during scene transitions
            }

            // 3. Continuous assertion of ambient flat illumination (scaled by slider)
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * state.FullbrightIntensity;
            RenderSettings.ambientIntensity = state.FullbrightIntensity;

            if (state.DisableFog)
            {
                RenderSettings.fog = false;
                RenderSettings.fogDensity = 0.0f;
            }
        }
        else if (_hasRenderSettingsBaseline)
        {
            // Restore RenderSettings
            RenderSettings.ambientMode = _baseAmbientMode;
            RenderSettings.ambientLight = _baseAmbientLight;
            RenderSettings.ambientIntensity = _baseAmbientIntensity;
            RenderSettings.fog = _baseFog;
            RenderSettings.fogDensity = _baseFogDensity;
            _hasRenderSettingsBaseline = false;

            // Restore Enviro 3 lighting parameters
            try
            {
                var enviroManager = Enviro.EnviroManager.instance;
                if (enviroManager != null)
                {
                    var enviroLighting = enviroManager.Lighting;
                    var enviroSettings = enviroLighting != null ? enviroLighting.Settings : null;
                    if (enviroSettings != null && _hasEnviroLightingBaseline)
                    {
                        enviroSettings.setAmbientLighting = _baseEnviroSetAmbient;
                        enviroSettings.setDirectLighting = _baseEnviroSetDirect;
                        enviroSettings.ambientIntensityModifier = _baseEnviroAmbientMod;
                        enviroSettings.directLightIntensityModifier = _baseEnviroDirectMod;
                        enviroLighting.UpdateAmbientLighting(true);
                        enviroLighting.UpdateDirectLighting();
                    }

                    var enviroObjects = enviroManager.Objects;
                    if (enviroObjects != null && enviroObjects.directionalLight != null && _hasDirectLightBaseline)
                    {
                        enviroObjects.directionalLight.intensity = _baseDirectLightIntensity;
                    }
                }
            }
            catch
            {
                // Fallback
            }

            _hasEnviroLightingBaseline = false;
            _hasDirectLightBaseline = false;
        }
    }
    #endregion

    #region Feature 3: Slope & Climb Traversability Assertion (Ground Snapping Preserved)
    private void AssertTraversability()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;
        var player = WorldManager.localPlayerCharacter;
        var ground = player?.ground;
        if (ground == null || state == null)
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
                _baseSteepMaterialMaxGrip = ground.steepMaterialMaxGrip;
                _baseAngleFullSteep = ground.angleFullSteep;
                _baseAngleJumpLimitSteep = ground.angleJumpLimitSteep;
                _baseAngleNoGrip = ground.angleNoGrip;
                _baseAngleFullGrip = ground.angleFullGrip;
                _baseAngleFullUphill = ground.angleFullUphill;
                _baseAngleFullDownhill = ground.angleFullDownhill;
                _hasGroundBaseline = true;
            }

            // CRITICAL FIX: Keep limitSlopeMovement = true so ground snapping and slope normal logic remain fully active.
            ground.limitSlopeMovement = true;
            ground.limitWaterMovement = false;
            ground.steepMaterialLimitsGrip = false;
            ground.steepMaterialMaxGrip = 1.0f;

            // Clamping slope grip angles to 89.0° enables walking up nearly vertical surfaces
            // while preserving angleNoGrip > angleFullGrip to avoid division-by-zero / NaN velocity vectors.
            ground.angleFullGrip = 88.0f;
            ground.angleNoGrip = 89.5f;
            ground.angleFullSteep = 89.0f;
            ground.angleJumpLimitSteep = 89.0f;
            ground.angleFullUphill = 89.0f;
            ground.angleFullDownhill = 89.0f;
        }
        else if (_hasGroundBaseline)
        {
            // Restore native physics angles
            ground.limitSlopeMovement = _baseLimitSlope;
            ground.limitWaterMovement = _baseLimitWater;
            ground.steepMaterialLimitsGrip = _baseSteepMaterialLimits;
            ground.steepMaterialMaxGrip = _baseSteepMaterialMaxGrip;
            ground.angleFullSteep = _baseAngleFullSteep;
            ground.angleJumpLimitSteep = _baseAngleJumpLimitSteep;
            ground.angleNoGrip = _baseAngleNoGrip;
            ground.angleFullGrip = _baseAngleFullGrip;
            ground.angleFullUphill = _baseAngleFullUphill;
            ground.angleFullDownhill = _baseAngleFullDownhill;
            _hasGroundBaseline = false;
        }
    }
    #endregion

    #region Feature 4: Fall Hazard & Daze Assertion
    private void AssertFallAndDaze()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;
        var player = WorldManager.localPlayerCharacter;
        var faller = player?.faller;
        if (faller == null || state == null)
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
    private void AssertLocomotionTunings()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;
        var player = WorldManager.localPlayerCharacter;
        var tunings = player?.tunings;
        if (tunings == null || state == null)
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

    #region Feature 6: Free-Cam Assertion & Remapped Arrow Key Flight Controls
    private void AssertFreeCamPriming()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;
        var player = WorldManager.localPlayerCharacter;
        var cheater = player?.cheater;
        if (cheater != null && state != null)
        {
            cheater.cheatTextIsPrimed = true;
            cheater.cheatButtonsIsPrimed = true;

            var camMover = cheater.cameraCheatMover;
            if (camMover != null)
            {
                state.FreeCamDetached = camMover._detached;
                if (camMover._detached)
                {
                    // Zero out movingSpeed so native WASD input does NOT move the camera or character simultaneously
                    camMover.movingSpeed = 0f;
                    camMover.sensitivity = state.FreeCamSensitivity;
                }
            }
        }
    }

    private void HandleFreeCamFlightControls()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;
        var player = WorldManager.localPlayerCharacter;
        if (state == null || !state.FreeCamDetached) return;

        var cheater = player?.cheater;
        var camMover = cheater?.cameraCheatMover ?? UnityEngine.Object.FindObjectOfType<CameraCheatMover>();
        Transform camTransform = camMover != null ? camMover.transform : Camera.main?.transform;

        if (camTransform != null)
        {
            Vector3 moveDir = Vector3.zero;

            // Translation via Arrow Keys
            if (Input.GetKey(KeyCode.UpArrow)) moveDir += camTransform.forward;
            if (Input.GetKey(KeyCode.DownArrow)) moveDir -= camTransform.forward;
            if (Input.GetKey(KeyCode.RightArrow)) moveDir += camTransform.right;
            if (Input.GetKey(KeyCode.LeftArrow)) moveDir -= camTransform.right;

            // Elevation via PageUp/PageDown or R/F or E/Q
            if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.R) || Input.GetKey(KeyCode.E)) moveDir += Vector3.up;
            if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.Q)) moveDir -= Vector3.up;

            if (moveDir != Vector3.zero)
            {
                float boost = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 2.5f : 1.0f;
                camTransform.position += moveDir.normalized * (state.FreeCamSpeed * boost * Time.unscaledDeltaTime);
            }
        }

        // Anchor local player kinematics so character stays completely motionless while flying
        if (player != null && player.rb != null)
        {
            player.rb.linearVelocity = Vector3.zero;
            player.rb.angularVelocity = Vector3.zero;
        }
    }
    #endregion

    #region Time Freeze Assertion
    private void AssertTimeFreeze()
    {
        var state = BigWalkAIOPlugin.Instance?.ControlState;
        if (state != null && state.FreezeTime && Time.timeScale != 0f)
        {
            Time.timeScale = 0f;
        }
    }
    #endregion

    #region Task 3: Input Suppression & Cursor State Management
    private void AssertInputSuppression()
    {
        BigWalkAIOPlugin.SyncCursorState();
    }
    #endregion

    #region Telemetry Snapshot Publishing
    private void PublishTelemetrySnapshot()
    {
        var pipeline = BigWalkAIOPlugin.Instance?.TelemetryPipeline;
        if (pipeline == null) return;

        var controlState = BigWalkAIOPlugin.Instance?.ControlState;
        var localPlayer = WorldManager.localPlayerCharacter;

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

    #region Feature 1: Minimap & Full World Map Snapshot Publishing
    private void PublishMinimapSnapshot()
    {
        var minimapPipeline = BigWalkAIOPlugin.Instance?.MinimapPipeline;
        if (minimapPipeline == null) return;

        var localPlayer = WorldManager.localPlayerCharacter;

        // Periodically refresh world objects (Landmarks, Medals, Gourds) every 4 seconds
        float now = Time.unscaledTime;
        if (now >= _nextWorldScanTime)
        {
            _nextWorldScanTime = now + 4.0f;
            ScanWorldObjects();
        }

        var snapshot = MinimapSnapshot.CreateDefault();

        if (localPlayer != null)
        {
            snapshot.HasLocalPlayer = true;
            snapshot.LocalPosition = localPlayer.transform.position;

            // Accurate heading from player camera or body orientation
            float heading = localPlayer.cameraTransform != null
                ? localPlayer.cameraTransform.eulerAngles.y
                : (Camera.main != null
                    ? Camera.main.transform.eulerAngles.y
                    : localPlayer.transform.eulerAngles.y);

            snapshot.LocalHeading = heading;

            // Gather connected peers from PlayerCharacter.allPlayerCharacters
            var all = PlayerCharacter.allPlayerCharacters;
            if (all != null)
            {
                int count = 0;
                for (int i = 0; i < all.Count && count < MinimapSnapshot.MaxPeers; i++)
                {
                    var peer = all[i];
                    if (peer != null && !peer.isLocalPlayer)
                    {
                        float peerHeading = peer.cameraTransform != null ? peer.cameraTransform.eulerAngles.y : peer.transform.eulerAngles.y;
                        snapshot.SetPeer(count, peer.transform.position, peerHeading);
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

            // Populate cached quest medals
            snapshot.MedalCount = _cachedMedalCount;
            for (int i = 0; i < _cachedMedalCount; i++)
            {
                snapshot.SetMedal(i, _cachedMedalPositions[i], _cachedMedalTypes[i]);
            }

            // Populate cached gourd props
            snapshot.GourdCount = _cachedGourdCount;
            for (int i = 0; i < _cachedGourdCount; i++)
            {
                snapshot.SetGourd(i, _cachedGourdPositions[i], _cachedGourdPropIds[i]);
            }
        }

        minimapPipeline.Publish(in snapshot);
    }

    private void ScanWorldObjects()
    {
        try
        {
            // 1. Scan Gourds via GourdPositionData / GourdMap
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
                for (int i = 0; i < listings.Count && count < MinimapSnapshot.MaxGourds; i++)
                {
                    var listing = listings[i];
                    Vector3 pos = listing.position;
                    if (pos != Vector3.zero)
                    {
                        _cachedGourdPositions[count] = pos;
                        _cachedGourdPropIds[count] = (int)listing.saveablePropName;
                        count++;
                    }
                }
                _cachedGourdCount = count;
            }

            // 2. Scan Quest Medal Zones (MedalZone)
            var medalZones = UnityEngine.Object.FindObjectsOfType<MedalZone>();
            if (medalZones != null)
            {
                int count = 0;
                for (int i = 0; i < medalZones.Length && count < MinimapSnapshot.MaxMedals; i++)
                {
                    var zone = medalZones[i];
                    if (zone != null)
                    {
                        _cachedMedalPositions[count] = zone.transform.position;
                        _cachedMedalTypes[count] = (int)zone.medalType;
                        count++;
                    }
                }
                _cachedMedalCount = count;
            }

            // 3. Scan Landmarks / POIs
            var landmarks = UnityEngine.Object.FindObjectsOfType<LandmarkSaveSetter>();
            if (landmarks != null && landmarks.Length > 0)
            {
                int count = 0;
                for (int i = 0; i < landmarks.Length && count < MinimapSnapshot.MaxLandmarks; i++)
                {
                    var lm = landmarks[i];
                    if (lm != null)
                    {
                        _cachedLandmarkPositions[count] = lm.transform.position;
                        count++;
                    }
                }
                _cachedLandmarkCount = count;
            }
        }
        catch
        {
            // Safeguarded during scene transitions
        }
    }
    #endregion

    private void OnApplicationQuit()
    {
        _isQuitting = true;
        MapTextureManager.Teardown();
        BigWalkAIOPlugin.Instance?.Teardown();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (!_isQuitting)
        {
            Log.Warning("[BigWalkAIORunner] Runner destroyed outside of application quit. Respawning...");
            BigWalkAIOPlugin.Instance?.EnsureRunnerAlive();
            return;
        }

        BigWalkAIOPlugin.Instance?.Teardown();
    }
}
