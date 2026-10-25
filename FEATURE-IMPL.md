# Feature Implementation Specifications (FEATURE-IMPL.md)
## Universal Unity QA Testbed & Runtime Diagnostic Modules
**Target Runtime:** Unity IL2CPP x64 (.NET 6.0 CoreCLR)  
**Host Framework:** BepInEx 6.x IL2CPP (`BepInEx.Unity.IL2CPP` v6.0.0-be.790+)  
**UI Engine & Presentation:** `DearImGuiInjectionPlus` (DirectX 11 / DirectX 12 Swapchain Detour)  
**Target Application:** *Big Walk* (`Shipmates.Scripts` / `Assembly-CSharp`)  

---

## Architectural Context & Interop Contracts

All diagnostic fixtures, QA tools, and runtime test modules specified in this document strictly adhere to the foundational architecture defined in `GENERAL-DESC-6.x.md`:

1. **Strict Three-Thread Execution Boundary:**
   - **Win32 Message Thread:** Subclass message pump handles raw window events. Never invoke Unity or ImGui APIs here.
   - **Unity Main Thread (`MyPluginRunner : MonoBehaviour`):** Executes on the engine frame tick (`Update`, `FixedUpdate`). Responsible for polling entities, reading native pointers, updating telemetry, and mutating game states.
   - **DirectX Present Thread (`IImGuiWindow.OnRender`):** Executes inside the graphics swapchain hook. **Strictly prohibited** from accessing `UnityEngine.Object` instances, component properties, or static engine APIs directly.
2. **Cross-Thread Synchronization Primitives:**
   - **Engine $\to$ UI (Telemetry Publishing):** Use `StructSnapshotPipeline<T>` (triple-buffered atomic index exchange for value types) or `StateSnapshotPipeline<T>` (atomic reference exchange for reference objects).
   - **UI $\to$ Engine (Action Dispatching):** Marshal any game mutation triggered by ImGui widgets to the Unity main thread exclusively via non-blocking `UnityMainThreadDispatcher.Enqueue(() => { ... })`. Never use synchronous blocking waits (`InvokeOnMainThread`), which cause engine deadlocks.
3. **IL2CPP Memory & Type Rules:**
   - All custom runner classes inheriting from `MonoBehaviour` must declare `public MyRunner(IntPtr handle) : base(handle)` and register via `ClassInjector.RegisterTypeInIl2Cpp<MyRunner>()` before instantiation.
   - Store and track entity identities across frames via unmanaged pointers (`Il2CppObjectBase.Pointer`) or stable IDs (`netIdentity.netId`), avoiding managed wrapper churn.
   - Zero per-frame string allocations: cache handles and throttle scene queries to 1 Hz.

---

## Modular Implementation Specifications

### Feature 1: Interactive ImGui Minimap & World Radar Overlay
- **Category:** Navigation/Map
- **Objective & Purpose:** Solves spatial disorientation during boundary and terrain navigation testing by rendering a real-time 2D radar overlay with live player positions, heading indicators, and landmark anchors.
- **Target Components & Resolution:**
  - Class: `PlayerCharacter` / `WorldManager` / `GourdPositionData`
  - Access Path:
    - Local Player: `WorldManager.localPlayerCharacter`
    - All Players: `PlayerCharacter.allPlayerCharacters` (`Il2CppSystem.Collections.Generic.List<PlayerCharacter>`)
    - Landmarks / Gourds: `GourdMap.positionData.gourdListings` or `UnityEngine.Terrain.activeTerrain`
  - Key Fields / Methods:
    - `player.transform.position` (`Vector3`)
    - `player.transform.forward` (`Vector3`)
    - `player.isLocalPlayer` (`bool`)
    - `player.playerNetworking.username` (`string`)
    - `GourdPositionData.GourdListing.position` (`Vector3`)
- **Implementation Strategy:**
  - Main Thread (`MyPluginRunner.Update`): Queries `PlayerCharacter.allPlayerCharacters` and `WorldManager.localPlayerCharacter`, extracts positions $(X, Z)$ and heading angles, and publishes a `MinimapTelemetrySnapshot` struct to `StructSnapshotPipeline<MinimapTelemetrySnapshot>`. Polls `KeyCode.M` to toggle visibility.
  - Present Thread (`MinimapWindow : IImGuiWindow`): In `OnRender()`, retrieves the latest snapshot, establishes a circular or rectangular clipping canvas using `ImGui.GetWindowDrawList()`, transforms world $(X, Z)$ coordinates relative to the local player or terrain origin into 2D canvas space, and draws dots/lines via `ImDrawList.AddCircleFilled()`, `AddTriangleFilled()` (for player heading), and `AddText()`.
- **Synchronization Scope:** Local-Only (Visual overlay rendered strictly within DirectX swapchain; no network transmission).
- **Pseudo-Code Blueprint:**
  ```csharp
  // Unity Main Thread: Ingest & Publish
  var local = WorldManager.localPlayerCharacter;
  if (local == null) return;
  var snapshot = new MinimapSnapshot { LocalPos = local.transform.position, Heading = local.transform.eulerAngles.y };
  var all = PlayerCharacter.allPlayerCharacters;
  for (int i = 0; i < all.Count && i < 16; i++) {
      if (all[i] != null && !all[i].isLocalPlayer)
          snapshot.PeerPositions[snapshot.PeerCount++] = all[i].transform.position;
  }
  _minimapPipeline.Publish(ref snapshot);

  // DirectX Present Thread: ImDrawList Canvas Projection
  var snap = _minimapPipeline.GetLatest();
  Vector2 p0 = ImGui.GetCursorScreenPos(), sz = ImGui.GetContentRegionAvail();
  Vector2 center = p0 + sz * 0.5f; float zoom = 0.5f;
  var draw = ImGui.GetWindowDrawList();
  draw.AddCircleFilled(center, 5f, 0xFF00FF00); // Local player dot
  for (int i = 0; i < snap.PeerCount; i++) {
      Vector2 peerCanvas = center + new Vector2(snap.PeerPositions[i].x - snap.LocalPos.x, -(snap.PeerPositions[i].z - snap.LocalPos.z)) * zoom;
      draw.AddCircleFilled(peerCanvas, 4f, 0xFF0088FF); // Peer dot
  }
  ```

---

### Feature 2: Local-Only Fullbright & Fog Neutralizer
- **Category:** Vision/Diagnostics
- **Objective & Purpose:** Overrides scene ambient illumination, skybox dimming, and volumetric fog to guarantee maximum visual clarity when evaluating underground structures, caves, night cycles, and dense vegetation.
- **Target Components & Resolution:**
  - Class: `UnityEngine.RenderSettings` / `Shipmates.RemotePlayerDebug.ToggleEnviroRendererFeatureOperation` / `LobbyEnviroNetworking`
  - Access Path:
    - Ambient Lighting & Fog: `UnityEngine.RenderSettings` static properties
    - Built-in URP Features: `Shipmates.RemotePlayerDebug.RemotePlayerDebugOperations.CreateOperations()`
    - Enviro Atmosphere: `LobbyEnviroNetworking.syncTime` / `Enviro3.Runtime`
  - Key Fields / Methods:
    - `RenderSettings.ambientLight` (`Color`)
    - `RenderSettings.ambientMode` (`UnityEngine.Rendering.AmbientMode`)
    - `RenderSettings.ambientIntensity` (`float`)
    - `RenderSettings.fog` (`bool`)
    - `RenderSettings.fogDensity` (`float`)
- **Implementation Strategy:**
  - Main Thread (`MyPluginRunner.Update`): When Fullbright toggle is enabled, continuously assert desired lighting values in `LateUpdate` to override Enviro 3's dynamic day-night evaluator:
    `RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white; RenderSettings.ambientIntensity = 2.0f; RenderSettings.fog = false;`
  - Built-in URP Feature Toggling: Execute the built-in `ToggleEnviroRendererFeatureOperation` or `ToggleVolumetricClouds` operation to strip volumetric haze without modifying shader assets.
  - Restoration: Cache baseline `ambientLight`, `ambientMode`, and `fog` upon toggle activation; restore original values on deactivation.
- **Synchronization Scope:** Local-Only (Engine rendering properties and URP scriptable renderer features execute strictly on the local GPU pipeline without triggering Mirror RPCs).
- **Pseudo-Code Blueprint:**
  ```csharp
  // Unity Main Thread (LateUpdate continuous assertion):
  if (_fullbrightActive)
  {
      RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
      RenderSettings.ambientLight = Color.white;
      RenderSettings.ambientIntensity = 2.5f;
      RenderSettings.fog = false;
      RenderSettings.fogDensity = 0.0f;
  }
  ```

---

### Feature 3: Slope & Climb Traversability Bypass
- **Category:** Locomotion
- **Objective & Purpose:** Neutralizes surface inclination resistance, sliding physics, and water wading friction, allowing testers to traverse 90-degree cliffs, deep trenches, and sheer mountain faces unhindered.
- **Target Components & Resolution:**
  - Class: `PlayerGround`
  - Access Path: `WorldManager.localPlayerCharacter.ground` (or `PlayerCharacter.ground`)
  - Key Fields / Methods:
    - `ground.limitSlopeMovement` (`bool`)
    - `ground.limitWaterMovement` (`bool`)
    - `ground.steepMaterialLimitsGrip` (`bool`)
    - `ground.angleFullSteep` (`float`, baseline ~45.0)
    - `ground.angleNoGrip` (`float`, baseline ~50.0)
    - `ground.angleFullGrip` (`float`, baseline ~35.0)
    - `ground.angleFullUphill` (`float`, baseline ~40.0)
    - `ground.angleFullDownhill` (`float`, baseline ~45.0)
- **Implementation Strategy:**
  - Main Thread (`MyPluginRunner.Update`): When traversability bypass is enabled, continuously set `ground.limitSlopeMovement = false`, `ground.limitWaterMovement = false`, `ground.steepMaterialLimitsGrip = false`, and expand `ground.angleNoGrip = 90f` and `ground.angleFullSteep = 90f`.
  - State Restoration: On toggle deactivation, restore the cached baseline slope limit parameters.
- **Synchronization Scope:** Local-Only (Player kinematic character movement is evaluated client-side in `PlayerGround.Update` / `FixedUpdate`; peer movement replication is fed via position synchronizers).
- **Pseudo-Code Blueprint:**
  ```csharp
  // Unity Main Thread: Ingested via Dispatcher
  var ground = WorldManager.localPlayerCharacter?.ground;
  if (ground != null && _infiniteClimbActive)
  {
      ground.limitSlopeMovement = false;
      ground.limitWaterMovement = false;
      ground.steepMaterialLimitsGrip = false;
      ground.angleFullSteep = 90.0f;
      ground.angleNoGrip = 90.0f;
      ground.angleFullGrip = 90.0f;
  }
  ```

---

### Feature 4: Fall Hazard & Daze Suppression
- **Category:** Vitality
- **Objective & Purpose:** Prevents fall damage, knockdown timers, recovery delays, and dazed states when dropping from extreme altitudes or leaping between terrain cliffs during level stress tests.
- **Target Components & Resolution:**
  - Class: `PlayerFaller`
  - Access Path: `WorldManager.localPlayerCharacter.faller` (or `PlayerCharacter.faller`)
  - Key Fields / Methods:
    - `faller.ignoreFalling` (`bool`)
    - `faller.isDazed` (`bool`)
    - `faller.isInDanger` (`bool`)
    - `faller.sitPauseDuration` (`float`)
    - `faller.ClearNextFall()` (`void`)
- **Implementation Strategy:**
  - Direct Built-in State Mutation: `PlayerFaller` possesses a native `ignoreFalling` flag designed by the developers. When active, assert `faller.ignoreFalling = true` and `faller.sitPauseDuration = 0f`.
  - Instant Recovery Assertion: If `faller.isDazed` triggers, invoke `faller.ClearNextFall()` and reset `faller.isDazed = false` to break immediately out of the incapacitated sitting animation state.
- **Synchronization Scope:** Local-Only (Falling impact calculations, daze timers, and sitting pauses are computed locally on the controlling client).
- **Pseudo-Code Blueprint:**
  ```csharp
  // Unity Main Thread: Continuous assertion
  var faller = WorldManager.localPlayerCharacter?.faller;
  if (faller != null && _noFallDazeActive)
  {
      faller.ignoreFalling = true;
      faller.sitPauseDuration = 0.0f;
      if (faller.isDazed)
      {
          faller.ClearNextFall();
          faller.isDazed = false;
      }
  }
  ```

---

### Feature 5: Kinematic Locomotion Tuning (Speed & Jump Multipliers)
- **Category:** Locomotion
- **Objective & Purpose:** Scales base walking, sprinting, swimming, and jumping velocities dynamically via ImGui sliders, allowing rapid exploration of large outdoor maps and vertical geometry verification.
- **Target Components & Resolution:**
  - Class: `PlayerTunings` / `PlayerCharacter` / `PlayerJumper`
  - Access Path:
    - Tunings: `WorldManager.localPlayerCharacter.tunings`
    - Jumper: `WorldManager.localPlayerCharacter.jumper`
  - Key Fields / Methods:
    - `tunings.forwardSpeed` (`float`)
    - `tunings.forwardSprintSpeed` (`float`)
    - `tunings.swimForwardSpeed` (`float`)
    - `tunings.swimForwardSprintSpeed` (`float`)
    - `tunings.crouchForwardSpeed` (`float`)
    - `tunings.jumpForce` (`float`)
    - `tunings.maxUpwardsVelocity` (`float`)
    - `tunings.maxWindUpDuration` (`float`)
- **Implementation Strategy:**
  - Baseline Caching: Upon scene load or first attachment to `WorldManager.localPlayerCharacter`, cache original tuning floats (`_baseWalkSpeed`, `_baseSprintSpeed`, `_baseJumpForce`, `_baseMaxUpwardsVel`).
  - ImGui Sliders: Provide multiplicative sliders for Speed Factor ($1.0\times - 5.0\times$) and Jump Factor ($1.0\times - 5.0\times$).
  - Dispatch Mutation: Whenever the slider values change, dispatch a mutation to the Unity main thread setting:
    `tunings.forwardSpeed = _baseWalkSpeed * speedMultiplier;`
    `tunings.forwardSprintSpeed = _baseSprintSpeed * speedMultiplier;`
    `tunings.jumpForce = _baseJumpForce * jumpMultiplier;`
    `tunings.maxUpwardsVelocity = _baseMaxUpwardsVel * jumpMultiplier;`
- **Synchronization Scope:** Local-Only (Client kinematic movement parameters drive local physics vectors; synchronized peers receive replicated position coordinates).
- **Pseudo-Code Blueprint:**
  ```csharp
  // ImGui Present Thread -> Main Thread Dispatch
  if (ImGui.SliderFloat("Speed Multiplier", ref _speedMult, 1.0f, 5.0f))
  {
      float s = _speedMult;
      UnityMainThreadDispatcher.Enqueue(() => {
          var tunings = WorldManager.localPlayerCharacter?.tunings;
          if (tunings != null) {
              tunings.forwardSpeed = _baseWalkSpeed * s;
              tunings.forwardSprintSpeed = _baseSprintSpeed * s;
          }
      });
  }
  ```

---

### Feature 6: Built-in Free-Cam Integration (`CameraCheatMover` & `PlayerCheater`)
- **Category:** Vision/Diagnostics
- **Objective & Purpose:** Integrates the game's internal cinematic free-flight inspection camera (`CameraCheatMover`) into the ImGui overlay, enabling unconstrained 6DoF observation and entity inspection outside player collision bounds.
- **Target Components & Resolution:**
  - Class: `CameraCheatMover` / `PlayerCheater`
  - Access Path:
    - Cheater: `WorldManager.localPlayerCharacter.cheater`
    - FreeCam Mover: `player.cheater.cameraCheatMover` (or `UnityEngine.Object.FindObjectOfType<CameraCheatMover>()`)
  - Key Fields / Methods:
    - `cameraCheatMover.Detach()` (`void`, unlocks camera from player pivot)
    - `cameraCheatMover.Attach()` (`void`, re-parents camera to player head)
    - `cameraCheatMover.movingSpeed` (`float`, free-cam movement speed)
    - `cameraCheatMover.sensitivity` (`float`, mouse look sensitivity)
    - `cameraCheatMover._detached` (`bool`, current detachment state)
    - `cheater.cheatTextIsPrimed` (`bool`)
    - `cheater.cheatButtonsIsPrimed` (`bool`)
- **Implementation Strategy:**
  - Main Thread (`MyPluginRunner`): Ensures `player.cheater.cheatTextIsPrimed = true` and `cheatButtonsIsPrimed = true`.
  - ImGui Toggle: Provide a "Toggle Free Camera" button and hotkey binding.
  - Dispatch: When toggled, invoke `cameraCheatMover.Detach()` to start free flying, or `cameraCheatMover.Attach()` to return control to the player character.
- **Synchronization Scope:** Local-Only (The camera transform is purely client-rendered; player character position remains anchored where detached).
- **Pseudo-Code Blueprint:**
  ```csharp
  // Dispatch from ImGui Button
  UnityMainThreadDispatcher.Enqueue(() => {
      var cheater = WorldManager.localPlayerCharacter?.cheater;
      var camMover = cheater?.cameraCheatMover;
      if (camMover == null) return;
      if (camMover._detached) camMover.Attach();
      else camMover.Detach();
  });
  ```

---

### Feature 7: Built-in Developer Operation Runner (`RemotePlayerDebugOperations`)
- **Category:** Environment
- **Objective & Purpose:** Discovers and exposes the target game's internal remote debugging command catalog (`Shipmates.RemotePlayerDebug`), allowing instant execution of native rendering, collision, and performance toggles.
- **Target Components & Resolution:**
  - Class: `Shipmates.RemotePlayerDebug.RemotePlayerDebugReceiver` / `RemotePlayerDebugOperations` / `RemotePlayerDebugOperationBase`
  - Access Path: `RemotePlayerDebugReceiver.GetHandler().ops` (`Il2CppSystem.Collections.Generic.Dictionary<string, RemotePlayerDebugOperationBase>`)
  - Key Fields / Methods:
    - `RemotePlayerDebugOperations.CreateOperations()` (`Dictionary<string, RemotePlayerDebugOperationBase>`)
    - `RemotePlayerDebugOperationBase.Execute(RemotePlayerRequest req)` (`RemotePlayerResponse`)
    - Operations Catalog:
      * `"ToggleMaxLight"` (`ToggleMaxLightOperation`)
      * `"ToggleVolumetricClouds"` (`ToggleVolumetricClouds`)
      * `"ToggleOcean"` (`ToggleOceanOperation`)
      * `"ToggleTerrain"` (`ToggleTerrainOperation`)
      * `"ToggleTerrainColliders"` (`ToggleTerrainColliders`)
      * `"ToggleLandmarks"` (`ToggleLandmarksOperation`)
      * `"ToggleOcclusionCulling"` (`ToggleOcclusionCulling`)
      * `"ToggleHbao"` (`ToggleHbaoOperation`)
      * `"StopEnviroTime"` (`StopEnviroTimeOperation`)
      * `"AddColliderUnderPlayer"` (`AddColliderUnderPlayerOperation`)
- **Implementation Strategy:**
  - Main Thread Startup: During harness initialization, query `RemotePlayerDebugReceiver.GetHandler()`. If null, instantiate via `RemotePlayerDebugHandler.CreateNew()`. Cache the operation dictionary keys.
  - ImGui Tab (`DebugOperationsTab : ITemplateTab`): Render operation items in a searchable grid or multi-column list.
  - Execution Dispatch: On button click, dispatch `op.Execute(new RemotePlayerRequest())` to the Unity main thread.
- **Synchronization Scope:** Local-Only (Debug operations alter local rendering features, colliders, and time advancement).
- **Pseudo-Code Blueprint:**
  ```csharp
  // ImGui Present Thread: Render catalog buttons
  foreach (var opKey in _cachedOperationNames) {
      if (ImGui.Button(opKey)) {
          string targetOp = opKey;
          UnityMainThreadDispatcher.Enqueue(() => {
              var handler = RemotePlayerDebugReceiver.GetHandler();
              if (handler != null && handler.ops.TryGetValue(targetOp, out var op))
                  op.Execute(new RemotePlayerRequest());
          });
      }
  }
  ```

---

### Feature 8: World Gourd & Prop Catalog Teleporter
- **Category:** Inventory
- **Objective & Purpose:** Catalogs all registered puzzle items, gourds, and quest landmarks across the world, providing coordinate readouts and instantaneous spatial warping to any objective.
- **Target Components & Resolution:**
  - Class: `SaveablePropName` / `GourdPositionData` / `PropInventory` / `Prop`
  - Access Path:
    - Gourd Positions: `GourdMap.positionData` (or `ScriptableObject` instance)
    - Inventory Container: `WorldManager.instance.propInventory` / `PropInventory.instance`
    - Live Scene Props: `UnityEngine.Object.FindObjectsOfType<Prop>()`
  - Key Fields / Methods:
    - `GourdPositionData.GetPosition(SaveablePropName name)` (`Vector3`)
    - `Prop.saveablePropName` (`SaveablePropName`)
    - `Prop.savablePropGuid` (`string`)
    - `PropInventory.AddProp(Prop prop)` (`void`)
    - `player.transform.position` (`Vector3`)
- **Implementation Strategy:**
  - Main Thread: Ingest the complete `SaveablePropName` enum list (over 100 entries, including `gourdCabinFever`, `gourdInvisibleInk`, `gourdObby`, `gourdTesting00-38`). Query `GourdPositionData.GetPosition(enumVal)` to resolve world coordinates.
  - ImGui Present Thread: Render a filterable combo/listbox of gourds with their 3D coordinates.
  - Teleport Dispatch: On "Warp to Gourd" click, dispatch:
    `var target = gourdPos + Vector3.up * 1.5f; WorldManager.localPlayerCharacter.transform.position = target; WorldManager.localPlayerCharacter.rb.linearVelocity = Vector3.zero;`
- **Synchronization Scope:** Client-Replicated (Local client teleports character position; Mirror network synchronizer replicates new transform coordinates to host and connected peers).
- **Pseudo-Code Blueprint:**
  ```csharp
  // ImGui Button Dispatch -> Teleport Local Player
  if (ImGui.Button($"Warp to {selectedGourdName}"))
  {
      Vector3 dest = selectedGourdCoord + Vector3.up * 1.5f;
      UnityMainThreadDispatcher.Enqueue(() => {
          var player = WorldManager.localPlayerCharacter;
          if (player != null) {
              player.transform.position = dest;
              if (player.rb != null) player.rb.linearVelocity = Vector3.zero;
          }
      });
  }
  ```

---

## Architectural Invariants for Downstream Implementation

When implementing these specifications in concrete C# companion assemblies:

1. **Never Call UnityEngine APIs Inside `OnRender()`:** Always read from snapshot pipelines and dispatch mutations via `UnityMainThreadDispatcher.Enqueue()`.
2. **Never Inject Unity UI or OnGUI:** All rendering must execute via Dear ImGui (`DearImGuiInjectionPlus`).
3. **Preserve Native Pointers:** Cache reflection accessors and use unmanaged pointers (`Il2CppObjectBase.Pointer`) to prevent IL2CPP GC wrapper churn.
4. **Isolate Harmony Patches:** If hooking internal methods, use isolated per-method `try/catch` blocks and check `Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod`.
