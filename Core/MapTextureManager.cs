using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BigWalk.AIO.State;
using UnityEngine;

namespace BigWalk.AIO.Core;

/// <summary>
/// Represents a discovered texture candidate for the in-game world map overlay.
/// </summary>
public class MapTextureCandidate
{
    public Texture Texture { get; }
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public string Format { get; }
    public string SourceTier { get; }
    public int Score { get; }
    public string DisplayLabel { get; }

    public MapTextureCandidate(Texture tex, string name, int width, int height, string format, string sourceTier, int score)
    {
        Texture = tex;
        Name = string.IsNullOrEmpty(name) ? "Unnamed" : name;
        Width = width;
        Height = height;
        Format = format;
        SourceTier = sourceTier;
        Score = score;
        DisplayLabel = $"{Name} ({width}x{height}, {format}) [Score: {score}]";
    }
}

/// <summary>
/// Manages runtime discovery, scoring, candidate registration, and Direct3D 11 ShaderResourceView
/// creation for the authentic in-game island world map texture.
/// 
/// Unity objects (PaperMapCamera, FoldingMapMaterialSetter, Texture2D, RenderTexture)
/// are safely resolved and baked on the Unity main thread.
/// Direct3D 11 ShaderResourceViews are created and bound on the Present hook thread.
/// </summary>
public static class MapTextureManager
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateShaderResourceViewDelegate(
        IntPtr pDevice,
        IntPtr pResource,
        IntPtr pDesc,
        out IntPtr ppSRV);

    // Main thread state
    private static Texture _discoveredTexture;
    private static RenderTexture _bakedRenderTexture;
    private static IntPtr _nativeTextureResourcePtr = IntPtr.Zero;
    private static int _textureWidth;
    private static int _textureHeight;
    private static string _textureName = "None";
    private static string _textureFormat = "N/A";
    private static string _textureSourceInfo = "Scanning...";
    private static float _nextScanTime;
    private static bool _forceRescanRequested = true;

    // Candidate registry
    private static MapTextureCandidate[] _candidates = Array.Empty<MapTextureCandidate>();
    private static int _selectedCandidateIndex = -1;
    private static readonly HashSet<IntPtr> _seenPointers = new();

    // Present thread state (DirectX 11)
    private static IntPtr _cachedDevicePtr = IntPtr.Zero;
    private static IntPtr _cachedResourcePtr = IntPtr.Zero;
    private static IntPtr _cachedShaderResourceView = IntPtr.Zero;
    private static bool _deviceResolved;

    public static bool HasNativeTexture => _nativeTextureResourcePtr != IntPtr.Zero;
    public static string TextureName => _textureName;
    public static string TextureFormat => _textureFormat;
    public static int Width => _textureWidth;
    public static int Height => _textureHeight;
    public static string SourceInfo => _textureSourceInfo;

    public static IReadOnlyList<MapTextureCandidate> Candidates => _candidates;
    public static int SelectedCandidateIndex => _selectedCandidateIndex;

    // Strict heuristic filter: Disqualify decals, warning labels, stickers, normal maps, etc.
    private static readonly string[] BlacklistKeywords = new[]
    {
        "laser", "sticker", "warning", "caution", "decal", "normal", "bump",
        "atlas", "mask", "specular", "occlusion", "roughness", "metallic",
        "shadow", "lightmap", "font", "ui", "icon", "button", "gradient",
        "noise", "strap", "case", "cover", "careful", "logo", "glyph",
        "instruction", "vignette", "dirt", "scratch", "emission", "detail"
    };

    // Positive scoring weights for authentic island world map textures
    private static readonly (string Keyword, int Weight)[] PositiveKeywords = new[]
    {
        ("papermap", 120),
        ("mapface", 100),
        ("foldingmap", 90),
        ("islandmap", 80),
        ("reliefmap", 70),
        ("carto", 50),
        ("island", 40),
        ("world", 35),
        ("landscape", 30),
        ("terrain", 30),
        ("topo", 30),
        ("chart", 30),
        ("paper", 25),
        ("map", 20),
        ("ground", 15)
    };

    /// <summary>
    /// Evaluates whether a given texture or material identifier contains blacklisted keywords.
    /// </summary>
    public static bool IsBlacklisted(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        for (int i = 0; i < BlacklistKeywords.Length; i++)
        {
            if (name.Contains(BlacklistKeywords[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes a heuristic quality score for a map texture candidate.
    /// Returns -1 if the texture fails dimension checks, is single-channel/monochrome, or matches blacklisted patterns.
    /// </summary>
    public static int ScoreTexture(Texture tex, string contextName, int baseTierWeight)
    {
        if (tex == null) return -1;
        string texName = tex.name ?? string.Empty;
        if (IsBlacklisted(texName) || IsBlacklisted(contextName))
            return -1;

        // Disqualify monochrome or alpha-only formats from being considered map textures
        if (tex is Texture2D t2d)
        {
            var fmt = t2d.format;
            if (fmt == UnityEngine.TextureFormat.Alpha8 || fmt == UnityEngine.TextureFormat.R8 || fmt == UnityEngine.TextureFormat.R16 ||
                fmt == UnityEngine.TextureFormat.RHalf || fmt == UnityEngine.TextureFormat.RFloat)
            {
                return -1;
            }
        }

        int w = tex.width;
        int h = tex.height;
        if (w < 256 || h < 256)
            return -1;

        int score = baseTierWeight;

        string combined = (texName + " " + contextName).ToLowerInvariant();
        for (int i = 0; i < PositiveKeywords.Length; i++)
        {
            if (combined.Contains(PositiveKeywords[i].Keyword))
            {
                score += PositiveKeywords[i].Weight;
            }
        }

        if (w == h) score += 20; // Square aspect ratio
        if (w >= 1024 && h >= 1024) score += 30; // High resolution
        if (w >= 2048 && h >= 2048) score += 20; // Ultra high resolution

        return score;
    }

    /// <summary>
    /// Selects a specific candidate from the registry and immediately binds it.
    /// </summary>
    public static void SelectCandidate(int index)
    {
        if (index >= 0 && index < _candidates.Length)
        {
            _selectedCandidateIndex = index;
            var c = _candidates[index];
            BindCandidate(c);
        }
    }

    private static void BindCandidate(MapTextureCandidate c)
    {
        if (c?.Texture == null) return;
        try
        {
            var ptr = c.Texture.GetNativeTexturePtr();
            if (ptr != IntPtr.Zero)
            {
                _discoveredTexture = c.Texture;
                _nativeTextureResourcePtr = ptr;
                _textureWidth = c.Width;
                _textureHeight = c.Height;
                _textureName = c.Name;
                _textureFormat = c.Format;
                _textureSourceInfo = $"{c.SourceTier} [{c.Name} {c.Width}x{c.Height}] (Score: {c.Score})";
                Log.Info($"[MapTextureManager] Active map texture switched to: {_textureSourceInfo} Format: {_textureFormat}");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[MapTextureManager] Error binding candidate: {ex.Message}");
        }
    }

    /// <summary>
    /// Forces an immediate scan on the Unity main thread for map textures/cameras.
    /// </summary>
    public static void ForceRescan()
    {
        _forceRescanRequested = true;
        _nextScanTime = 0f;
    }

    /// <summary>
    /// Executed on the Unity main thread (via BigWalkAIORunner.Update).
    /// Discovers, scores, and registers all viable island map candidate textures.
    /// </summary>
    public static void Update()
    {
        float now = Time.unscaledTime;
        if (!_forceRescanRequested && now < _nextScanTime)
            return;

        _nextScanTime = now + 4.0f; // Periodic rescan every 4s if textures spawn later
        _forceRescanRequested = false;

        // If we already have a valid active texture and no force rescan was requested, ensure it's still alive
        if (_discoveredTexture != null && _candidates.Length > 0 && !_forceRescanRequested)
        {
            try
            {
                var ptr = _discoveredTexture.GetNativeTexturePtr();
                if (ptr != IntPtr.Zero)
                {
                    _nativeTextureResourcePtr = ptr;
                    _textureWidth = _discoveredTexture.width;
                    _textureHeight = _discoveredTexture.height;
                    return;
                }
            }
            catch
            {
                _discoveredTexture = null;
                _nativeTextureResourcePtr = IntPtr.Zero;
            }
        }

        try
        {
            var found = new List<MapTextureCandidate>();
            _seenPointers.Clear();

            void AddCandidate(Texture tex, string context, int baseWeight)
            {
                if (tex == null) return;
                try
                {
                    var ptr = tex.GetNativeTexturePtr();
                    if (ptr == IntPtr.Zero || _seenPointers.Contains(ptr)) return;

                    int score = ScoreTexture(tex, context, baseWeight);
                    if (score < 0) return;

                    _seenPointers.Add(ptr);
                    string name = string.IsNullOrEmpty(tex.name) ? "Unnamed" : tex.name;
                    string format = tex is Texture2D t2
                        ? t2.format.ToString()
                        : (tex is RenderTexture rt ? rt.format.ToString() : "DX11");

                    found.Add(new MapTextureCandidate(tex, name, tex.width, tex.height, format, context, score));
                }
                catch { }
            }

            // --- Tier 1: PaperMapCamera Instance, SavedTexture & Live Camera Render ---
            var paperMapCam = UnityEngine.Object.FindObjectOfType<PaperMapCamera>();
            if (paperMapCam == null)
            {
                var allCams = UnityEngine.Resources.FindObjectsOfTypeAll<PaperMapCamera>();
                if (allCams != null && allCams.Length > 0)
                {
                    paperMapCam = allCams[0];
                }
            }

            if (paperMapCam != null)
            {
                // Check savedTexture (Texture2D)
                if (paperMapCam.savedTexture != null)
                {
                    AddCandidate(paperMapCam.savedTexture, "PaperMapCamera.savedTexture", 150);
                }

                // Check replacementTerrainMaterial
                if (paperMapCam.replacementTerrainMaterial != null)
                {
                    var mat = paperMapCam.replacementTerrainMaterial;
                    try
                    {
                        AddCandidate(mat.mainTexture, "PaperMapCamera.replacementMaterial", 60);
                        if (mat.HasProperty("_MainTex"))
                            AddCandidate(mat.GetTexture("_MainTex"), "PaperMapCamera.replacementMaterial._MainTex", 60);
                        if (mat.HasProperty("_BaseMap"))
                            AddCandidate(mat.GetTexture("_BaseMap"), "PaperMapCamera.replacementMaterial._BaseMap", 60);
                    }
                    catch { }
                }

                // Force bake via mapCamera into dedicated RenderTexture
                if (paperMapCam.mapCamera != null)
                {
                    try
                    {
                        paperMapCam.SetCamera();

                        if (_bakedRenderTexture == null || !_bakedRenderTexture.IsCreated())
                        {
                            _bakedRenderTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32)
                            {
                                name = "PaperMapCamera_Baked",
                                filterMode = FilterMode.Bilinear,
                                wrapMode = TextureWrapMode.Clamp
                            };
                            _bakedRenderTexture.Create();
                        }

                        var prevTarget = paperMapCam.mapCamera.targetTexture;
                        paperMapCam.mapCamera.targetTexture = _bakedRenderTexture;
                        paperMapCam.mapCamera.Render();
                        paperMapCam.mapCamera.targetTexture = prevTarget;

                        AddCandidate(_bakedRenderTexture, "PaperMapCamera.Render() [Live Bake]", 160);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MapTextureManager] PaperMapCamera live bake error: {ex.Message}");
                    }
                }
            }

            // --- Tier 2: FoldingMapMaterialSetter (Paper Map Prop Materials & Renderers) ---
            var setters = UnityEngine.Resources.FindObjectsOfTypeAll<FoldingMapMaterialSetter>();
            if (setters != null && setters.Length > 0)
            {
                for (int i = 0; i < setters.Length; i++)
                {
                    var setter = setters[i];
                    if (setter == null) continue;

                    // Audit ALL materials in materials array
                    if (setter.materials != null)
                    {
                        for (int m = 0; m < setter.materials.Length; m++)
                        {
                            var mat = setter.materials[m];
                            if (mat == null) continue;
                            string matName = mat.name ?? $"mat_{m}";

                            try
                            {
                                AddCandidate(mat.mainTexture, $"FoldingMapMaterialSetter.materials[{m}] ({matName})", 100);
                                if (mat.HasProperty("_MainTex"))
                                    AddCandidate(mat.GetTexture("_MainTex"), $"FoldingMapMaterialSetter.materials[{m}] ({matName}._MainTex)", 100);
                                if (mat.HasProperty("_BaseMap"))
                                    AddCandidate(mat.GetTexture("_BaseMap"), $"FoldingMapMaterialSetter.materials[{m}] ({matName}._BaseMap)", 100);
                            }
                            catch { }
                        }
                    }

                    // Audit ALL renderers
                    if (setter.renderers != null)
                    {
                        for (int r = 0; r < setter.renderers.Length; r++)
                        {
                            var rend = setter.renderers[r];
                            if (rend == null) continue;

                            try
                            {
                                var mats = rend.sharedMaterials;
                                if (mats != null)
                                {
                                    for (int m = 0; m < mats.Length; m++)
                                    {
                                        var mat = mats[m];
                                        if (mat == null) continue;
                                        string matName = mat.name ?? $"rend_{r}_mat_{m}";
                                        AddCandidate(mat.mainTexture, $"FoldingMapMaterialSetter.Renderer[{r}] ({matName})", 100);
                                        if (mat.HasProperty("_MainTex"))
                                            AddCandidate(mat.GetTexture("_MainTex"), $"FoldingMapMaterialSetter.Renderer[{r}] ({matName}._MainTex)", 100);
                                        if (mat.HasProperty("_BaseMap"))
                                            AddCandidate(mat.GetTexture("_BaseMap"), $"FoldingMapMaterialSetter.Renderer[{r}] ({matName}._BaseMap)", 100);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // --- Tier 3: Scan all loaded Texture2D assets in memory ---
            var allTextures = UnityEngine.Resources.FindObjectsOfTypeAll<Texture2D>();
            if (allTextures != null && allTextures.Length > 0)
            {
                for (int i = 0; i < allTextures.Length; i++)
                {
                    var tex = allTextures[i];
                    if (tex == null) continue;
                    if (tex.width < 256 || tex.height < 256) continue;
                    string name = tex.name ?? string.Empty;
                    if (IsBlacklisted(name)) continue;

                    AddCandidate(tex, "LoadedTexture2D", 20);
                }
            }

            // Sort candidates: Score descending, resolution descending, alphabetical by name
            found.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0) return cmp;
                cmp = (b.Width * b.Height).CompareTo(a.Width * a.Height);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            // Remember previous selection if possible
            string prevSelectedName = (_selectedCandidateIndex >= 0 && _selectedCandidateIndex < _candidates.Length)
                ? _candidates[_selectedCandidateIndex].Name
                : null;

            _candidates = found.ToArray();

            if (_candidates.Length > 0)
            {
                int newSelectedIdx = 0;
                if (!string.IsNullOrEmpty(prevSelectedName))
                {
                    for (int i = 0; i < _candidates.Length; i++)
                    {
                        if (_candidates[i].Name.Equals(prevSelectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            newSelectedIdx = i;
                            break;
                        }
                    }
                }

                _selectedCandidateIndex = newSelectedIdx;
                BindCandidate(_candidates[newSelectedIdx]);
            }
            else
            {
                _selectedCandidateIndex = -1;
                _textureSourceInfo = "No viable map textures found (Using Grid Fallback)";
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[MapTextureManager] Discovery scan exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Executed on the DirectX Present hook thread (inside ImGui render loops).
    /// Retrieves or creates an ID3D11ShaderResourceView for the active native texture.
    /// Returns IntPtr.Zero if no texture or device is available.
    /// </summary>
    public static IntPtr GetShaderResourceView()
    {
        IntPtr resourcePtr = _nativeTextureResourcePtr;
        if (resourcePtr == IntPtr.Zero)
        {
            ReleaseCurrentSrv();
            return IntPtr.Zero;
        }

        // If the resource hasn't changed and SRV is still valid, return it
        if (_cachedShaderResourceView != IntPtr.Zero && _cachedResourcePtr == resourcePtr)
        {
            return _cachedShaderResourceView;
        }

        // Release previous SRV if resource changed
        ReleaseCurrentSrv();

        // Resolve DirectX 11 device from DearImGuiInjectionPlus
        if (!_deviceResolved || _cachedDevicePtr == IntPtr.Zero)
        {
            _cachedDevicePtr = ResolveD3D11Device();
            _deviceResolved = true;
        }

        if (_cachedDevicePtr == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            // Call ID3D11Device::CreateShaderResourceView (vtable slot 7)
            // Signature: HRESULT CreateShaderResourceView(ID3D11Resource *pResource, const D3D11_SHADER_RESOURCE_VIEW_DESC *pDesc, ID3D11ShaderResourceView **ppSRV)
            unsafe
            {
                IntPtr vtable = *(IntPtr*)_cachedDevicePtr;
                IntPtr createSrvFunc = *(IntPtr*)(vtable + 7 * IntPtr.Size);
                var createSrv = Marshal.GetDelegateForFunctionPointer<CreateShaderResourceViewDelegate>(createSrvFunc);

                int hr = createSrv(_cachedDevicePtr, resourcePtr, IntPtr.Zero, out IntPtr srvPtr);
                if (hr == 0 && srvPtr != IntPtr.Zero)
                {
                    _cachedShaderResourceView = srvPtr;
                    _cachedResourcePtr = resourcePtr;
                    return _cachedShaderResourceView;
                }
                else
                {
                    Log.Warning($"[MapTextureManager] CreateShaderResourceView failed with HRESULT 0x{hr:X8}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[MapTextureManager] Error creating ShaderResourceView: {ex.Message}");
        }

        return IntPtr.Zero;
    }

    private static IntPtr ResolveD3D11Device()
    {
        try
        {
            var diiAsm = typeof(global::DearImGuiInjectionPlus.DearImGuiInjectionPlus).Assembly;
            var dx11Type = diiAsm.GetType("DearImGuiInjectionPlus.Backends.ImGuiDX11");
            if (dx11Type != null)
            {
                var devField = dx11Type.GetField("_device", BindingFlags.NonPublic | BindingFlags.Static);
                var devObj = devField?.GetValue(null);
                if (devObj != null)
                {
                    var prop = devObj.GetType().GetProperty("NativePointer", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var ptr = (IntPtr)prop.GetValue(devObj);
                        if (ptr != IntPtr.Zero)
                        {
                            return ptr;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[MapTextureManager] Error resolving D3D11 Device: {ex.Message}");
        }
        return IntPtr.Zero;
    }

    private static void ReleaseCurrentSrv()
    {
        if (_cachedShaderResourceView != IntPtr.Zero)
        {
            try
            {
                Marshal.Release(_cachedShaderResourceView);
            }
            catch { }
            _cachedShaderResourceView = IntPtr.Zero;
            _cachedResourcePtr = IntPtr.Zero;
        }
    }

    public static void Teardown()
    {
        ReleaseCurrentSrv();
        if (_bakedRenderTexture != null)
        {
            try
            {
                _bakedRenderTexture.Release();
                UnityEngine.Object.Destroy(_bakedRenderTexture);
            }
            catch { }
            _bakedRenderTexture = null;
        }
        _discoveredTexture = null;
        _nativeTextureResourcePtr = IntPtr.Zero;
        _textureName = "None";
        _textureFormat = "N/A";
        _textureSourceInfo = "None";
        _candidates = Array.Empty<MapTextureCandidate>();
        _selectedCandidateIndex = -1;
    }
}
