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
    public int Index;
}

/// <summary>
/// Value-type state snapshot containing live spatial coordinates for the radar and full world map.
/// Transferred lock-free via <see cref="DearImGuiInjectionPlus.StructSnapshotPipeline{T}"/>.
/// Contains fixed-size unmanaged arrays for zero per-frame garbage collector allocations.
/// </summary>
public unsafe struct MinimapSnapshot
{
    public const int MaxPeers = 16;
    public const int MaxLandmarks = 32;
    public const int MaxMedals = 32;
    public const int MaxGourds = 64;

    public bool HasLocalPlayer;
    public Vector3 LocalPosition;
    public float LocalHeading;

    // Connected Network Peers
    public int PeerCount;
    public fixed float PeerPosX[MaxPeers];
    public fixed float PeerPosY[MaxPeers];
    public fixed float PeerPosZ[MaxPeers];
    public fixed float PeerHeading[MaxPeers];

    // Island Landmarks / Points of Interest
    public int LandmarkCount;
    public fixed float LandmarkPosX[MaxLandmarks];
    public fixed float LandmarkPosY[MaxLandmarks];
    public fixed float LandmarkPosZ[MaxLandmarks];

    // Quest / Medal Zones (MedalType)
    public int MedalCount;
    public fixed float MedalPosX[MaxMedals];
    public fixed float MedalPosY[MaxMedals];
    public fixed float MedalPosZ[MaxMedals];
    public fixed int MedalTypes[MaxMedals];

    // World Props / Gourds (SaveablePropName)
    public int GourdCount;
    public fixed float GourdPosX[MaxGourds];
    public fixed float GourdPosY[MaxGourds];
    public fixed float GourdPosZ[MaxGourds];
    public fixed int GourdPropIds[MaxGourds];

    public static MinimapSnapshot CreateDefault()
    {
        return new MinimapSnapshot
        {
            HasLocalPlayer = false,
            LocalPosition = Vector3.zero,
            LocalHeading = 0f,
            PeerCount = 0,
            LandmarkCount = 0,
            MedalCount = 0,
            GourdCount = 0
        };
    }

    public void SetPeer(int index, Vector3 position, float heading)
    {
        if (index < 0 || index >= MaxPeers) return;
        fixed (float* px = PeerPosX, py = PeerPosY, pz = PeerPosZ, ph = PeerHeading)
        {
            px[index] = position.x;
            py[index] = position.y;
            pz[index] = position.z;
            ph[index] = heading;
        }
    }

    public Vector3 GetPeerPosition(int index)
    {
        if (index < 0 || index >= MaxPeers) return Vector3.zero;
        fixed (float* px = PeerPosX, py = PeerPosY, pz = PeerPosZ)
        {
            return new Vector3(px[index], py[index], pz[index]);
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
        fixed (float* lx = LandmarkPosX, ly = LandmarkPosY, lz = LandmarkPosZ)
        {
            lx[index] = position.x;
            ly[index] = position.y;
            lz[index] = position.z;
        }
    }

    public Vector3 GetLandmarkPosition(int index)
    {
        if (index < 0 || index >= MaxLandmarks) return Vector3.zero;
        fixed (float* lx = LandmarkPosX, ly = LandmarkPosY, lz = LandmarkPosZ)
        {
            return new Vector3(lx[index], ly[index], lz[index]);
        }
    }

    public void SetMedal(int index, Vector3 position, int medalType)
    {
        if (index < 0 || index >= MaxMedals) return;
        fixed (float* mx = MedalPosX, my = MedalPosY, mz = MedalPosZ)
        fixed (int* mt = MedalTypes)
        {
            mx[index] = position.x;
            my[index] = position.y;
            mz[index] = position.z;
            mt[index] = medalType;
        }
    }

    public Vector3 GetMedalPosition(int index)
    {
        if (index < 0 || index >= MaxMedals) return Vector3.zero;
        fixed (float* mx = MedalPosX, my = MedalPosY, mz = MedalPosZ)
        {
            return new Vector3(mx[index], my[index], mz[index]);
        }
    }

    public int GetMedalType(int index)
    {
        if (index < 0 || index >= MaxMedals) return -1;
        fixed (int* mt = MedalTypes)
        {
            return mt[index];
        }
    }

    public void SetGourd(int index, Vector3 position, int propId)
    {
        if (index < 0 || index >= MaxGourds) return;
        fixed (float* gx = GourdPosX, gy = GourdPosY, gz = GourdPosZ)
        fixed (int* gp = GourdPropIds)
        {
            gx[index] = position.x;
            gy[index] = position.y;
            gz[index] = position.z;
            gp[index] = propId;
        }
    }

    public Vector3 GetGourdPosition(int index)
    {
        if (index < 0 || index >= MaxGourds) return Vector3.zero;
        fixed (float* gx = GourdPosX, gy = GourdPosY, gz = GourdPosZ)
        {
            return new Vector3(gx[index], gy[index], gz[index]);
        }
    }

    public int GetGourdPropId(int index)
    {
        if (index < 0 || index >= MaxGourds) return -1;
        fixed (int* gp = GourdPropIds)
        {
            return gp[index];
        }
    }
}
