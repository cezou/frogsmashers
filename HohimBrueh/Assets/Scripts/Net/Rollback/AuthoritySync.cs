using System.Collections.Generic;
using FrogSmashers.Net.Sim;
using FrogSmashers.Net.Transport;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FrogSmashers.Net.Rollback
{
    /// <summary>
    /// Authoritative drift correction. The host broadcasts the state
    /// hash of its newest fully-confirmed tick every interval; clients
    /// compare against their own post-rollback hash for the same tick
    /// and, on mismatch, request a full snapshot which they restore
    /// and resimulate from. This bounds float drift without requiring
    /// strict cross-machine determinism.
    /// </summary>
    public class AuthoritySync : ISimTickable
    {
        const uint hashInterval = 30;
        const int hashRing = 512;

        /// <summary>Sync driving the current online match.</summary>
        public static AuthoritySync Active { get; private set; }

        /// <summary>Authoritative hash checkpoints compared (client).</summary>
        public int ComparedCount { get; private set; }

        /// <summary>Desyncs detected since match start (client).</summary>
        public int DesyncCount { get; private set; }

        /// <summary>Snapshot corrections applied (client).</summary>
        public int CorrectionCount { get; private set; }

        readonly uint[] hashes = new uint[hashRing];
        readonly uint[] hashTicks = new uint[hashRing];
        readonly SortedDictionary<uint, uint> pendingHostHashes =
            new SortedDictionary<uint, uint>();
        readonly MatchSnapshot wireSnapshot = new MatchSnapshot();
        readonly RollbackManager rollback;
        readonly bool isHost;

        uint lastBroadcastTick;
        bool snapshotRequested;

        AuthoritySync(RollbackManager rollback, bool isHost)
        {
            this.rollback = rollback;
            this.isHost = isHost;
        }

        /// <summary>Hashes after the whole tick has simulated.</summary>
        public int SimOrder
        {
            get { return 9000; }
        }

        /// <summary>Starts drift correction for the current match.</summary>
        public static AuthoritySync Begin(
            RollbackManager rollback, bool isHost)
        {
            Stop();
            Active = new AuthoritySync(rollback, isHost);
            SimulationDriver.Register(Active);
            if (isHost)
                NetMessages.SnapshotRequested += Active.OnSnapshotRequest;
            else
                NetMessages.HostHashReceived += Active.OnHostHash;
            if (!isHost)
                NetMessages.SnapshotReceived += Active.OnSnapshotReceived;
            return Active;
        }

        /// <summary>Stops drift correction.</summary>
        public static void Stop()
        {
            if (Active == null)
                return;
            SimulationDriver.Unregister(Active);
            NetMessages.SnapshotRequested -= Active.OnSnapshotRequest;
            NetMessages.HostHashReceived -= Active.OnHostHash;
            NetMessages.SnapshotReceived -= Active.OnSnapshotReceived;
            Active = null;
        }

        public void SimTick(float dt)
        {
            uint tick = SimClock.CurrentTick;
            hashes[tick % hashRing] = MatchHasher.Compute();
            hashTicks[tick % hashRing] = tick;
            if (SimulationDriver.IsResimulating)
                return;
            if (isHost)
                BroadcastConfirmedHash();
            else
                CompareReadyHashes();
        }

        uint SafeTick()
        {
            return OnlineMatch.SafeTick();
        }

        void BroadcastConfirmedHash()
        {
            uint safe = SafeTick();
            if (safe == uint.MaxValue || safe == 0)
                return;
            uint target = safe - (safe % hashInterval);
            if (target <= lastBroadcastTick
                || hashTicks[target % hashRing] != target)
            {
                return;
            }
            lastBroadcastTick = target;
            var manager = NetworkManager.Singleton;
            uint hash = hashes[target % hashRing];
            foreach (var clientId in manager.ConnectedClientsIds)
            {
                if (clientId != manager.LocalClientId)
                    NetMessages.SendHostHash(clientId, target, hash);
            }
        }

        void OnHostHash(uint tick, uint hash)
        {
            pendingHostHashes[tick] = hash;
        }

        void CompareReadyHashes()
        {
            uint safe = SafeTick();
            var done = new List<uint>();
            foreach (var entry in pendingHostHashes)
            {
                if (entry.Key > safe)
                    break;
                done.Add(entry.Key);
                if (hashTicks[entry.Key % hashRing] != entry.Key)
                    continue;
                ComparedCount++;
                if (hashes[entry.Key % hashRing] != entry.Value)
                {
                    DesyncCount++;
                    Debug.LogWarning("[AuthoritySync] Desync at tick"
                        + $" {entry.Key}, requesting snapshot");
                    RequestSnapshotOnce();
                }
            }
            foreach (var tick in done)
                pendingHostHashes.Remove(tick);
        }

        void RequestSnapshotOnce()
        {
            if (snapshotRequested)
                return;
            snapshotRequested = true;
            NetMessages.SendSnapshotRequest();
        }

        void OnSnapshotRequest(ulong clientId)
        {
            uint safe = SafeTick();
            uint present = SimClock.CurrentTick;
            uint tick = System.Math.Min(safe, present);
            var snap = rollback.Snapshots.TryGet(tick);
            if (snap == null)
            {
                Debug.LogError("[AuthoritySync] No snapshot for tick"
                    + $" {tick} to answer correction request");
                return;
            }
            var writer = new FastBufferWriter(
                SnapshotWire.MaxBytes, Allocator.Temp);
            using (writer)
            {
                writer.WriteValueSafe(NetMessages.CurrentEpoch);
                SnapshotWire.Write(ref writer, snap);
                NetMessages.SendSnapshot(clientId, writer);
            }
            Debug.Log("[AuthoritySync] Snapshot of tick"
                + $" {tick} sent to client {clientId}");
        }

        void OnSnapshotReceived(FastBufferReader reader)
        {
            SnapshotWire.Read(ref reader, wireSnapshot);
            uint present = SimClock.CurrentTick;
            if (!GameController.RestoreFrom(wireSnapshot))
            {
                Debug.LogError("[AuthoritySync] Authoritative snapshot"
                    + " restore failed");
                snapshotRequested = false;
                return;
            }
            rollback.Inputs.AcknowledgeMispredict();
            SimulationDriver.IsResimulating = true;
            while (SimClock.CurrentTick < present)
                SimulationDriver.StepNow();
            SimulationDriver.IsResimulating = false;
            CorrectionCount++;
            snapshotRequested = false;
            pendingHostHashes.Clear();
            Debug.Log("[AuthoritySync] Corrected from snapshot of tick"
                + $" {wireSnapshot.Tick}, resimulated to {present}");
        }
    }
}
