using System;
using UnityEngine;

namespace BigWalk.AIO.State;

/// <summary>
/// Fixed-size unmanaged peer marker for zero-allocation minimap telemetry.
/// </summary>
public struct MinimapPeerMarker
{
    public Vector3 Position;
    public float Heading;
    public string Name;
}

/// <summary>
/// Landmark marker for gourds and points of interest.
/// </summary>
public struct MinimapLandmarkMarker
{
    public Vector3 Position;
    public string Name;
}

/// <summary>
/// Value-type state snapshot containing live spatial coordinates for the radar overlay.
/// Transferred lock-free via <see cref="DearImGuiInjectionPlus.StructSnapshotPipeline{T}"/>.
/// </summary>
public unsafe struct MinimapSnapshot
{
    public const int MaxPeers = 16;
    public const int MaxLandmarks = 32;

    public bool HasLocalPlayer;
    public Vector3 LocalPosition;
    public float LocalHeading;

    public int PeerCount;
    public fixed float PeerPosX[MaxPeers];
    public fixed float PeerPosZ[MaxPeers];
    public fixed float PeerHeading[MaxPeers];

    public int LandmarkCount;
    public fixed float LandmarkPosX[MaxLandmarks];
    public fixed float LandmarkPosZ[MaxLandmarks];

    public static MinimapSnapshot CreateDefault()
    {
        return new MinimapSnapshot
        {
            HasLocalPlayer = false,
            LocalPosition = Vector3.zero,
            LocalHeading = 0f,
            PeerCount = 0,
            LandmarkCount = 0
        };
    }

    public void SetPeer(int index, Vector3 position, float heading)
    {
        if (index < 0 || index >= MaxPeers) return;
        fixed (float* px = PeerPosX, pz = PeerPosZ, ph = PeerHeading)
        {
            px[index] = position.x;
            pz[index] = position.z;
            ph[index] = heading;
        }
    }

    public Vector3 GetPeerPosition(int index)
    {
        if (index < 0 || index >= MaxPeers) return Vector3.zero;
        fixed (float* px = PeerPosX, pz = PeerPosZ)
        {
            return new Vector3(px[index], 0f, pz[index]);
        }
    }

    public float GetPeerHeading(int index)
    {
        if (index < 0 || index >= MaxPeers) return 0f;
        fixed (float* ph = PeerHeading)
        {
            return ph[index];
        }
    }

    public void SetLandmark(int index, Vector3 position)
    {
        if (index < 0 || index >= MaxLandmarks) return;
        fixed (float* lx = LandmarkPosX, lz = LandmarkPosZ)
        {
            lx[index] = position.x;
            lz[index] = position.z;
        }
    }

    public Vector3 GetLandmarkPosition(int index)
    {
        if (index < 0 || index >= MaxLandmarks) return Vector3.zero;
        fixed (float* lx = LandmarkPosX, lz = LandmarkPosZ)
        {
            return new Vector3(lx[index], 0f, lz[index]);
        }
    }
}
