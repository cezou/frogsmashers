using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FrogSmashers.Net.Transport
{
    /// <summary>
    /// Network-conditions simulator for tuning the rollback under
    /// realistic latency. Enabled by -simLatency MS (full RTT; each
    /// peer delays its receives by half), plus -simJitter MS,
    /// -simLoss PCT (unreliable messages only) and -simSeed N. It
    /// defers the registered receive handlers on the wall clock with
    /// its own System.Random, so the deterministic simulation never
    /// observes it. Reliable messages are delayed but never dropped
    /// nor reordered (their delivery times are kept monotonic per
    /// sender, matching the sequenced channel guarantee).
    /// </summary>
    public static class NetSimulator
    {
        struct Pending
        {
            public double Due;
            public long Seq;
            public ulong Sender;
            public byte[] Bytes;
            public int Length;
            public CustomMessagingManager
                .HandleNamedMessageDelegate Handler;
        }

        static readonly List<Pending> queue = new List<Pending>();
        static readonly List<Pending> ready = new List<Pending>();
        static readonly Dictionary<ulong, double> reliableDue =
            new Dictionary<ulong, double>();

        static bool parsed;
        static double oneWaySeconds;
        static double jitterSeconds;
        static double lossFraction;
        static System.Random rng;
        static long nextSeq;
        static NetSimulatorPump pump;

        /// <summary>True when any sim flag was passed on the CLI.</summary>
        public static bool Enabled
        {
            get
            {
                EnsureParsed();
                return oneWaySeconds > 0 || jitterSeconds > 0
                    || lossFraction > 0;
            }
        }

        /// <summary>
        /// Defers a received named message: rolls loss (droppable
        /// messages only), copies the payload (the original reader
        /// dies with this callback) and schedules the real handler.
        /// </summary>
        public static void Enqueue(
            ulong senderClientId, FastBufferReader reader,
            CustomMessagingManager.HandleNamedMessageDelegate handler,
            bool droppable)
        {
            if (droppable && rng.NextDouble() < lossFraction)
                return;
            int length = reader.Length - reader.Position;
            var bytes = new byte[length];
            reader.ReadBytesSafe(ref bytes, length);
            double due = Time.unscaledTimeAsDouble + RollDelay();
            if (!droppable)
                due = ClampReliable(senderClientId, due);
            queue.Add(new Pending
            {
                Due = due,
                Seq = nextSeq++,
                Sender = senderClientId,
                Bytes = bytes,
                Length = length,
                Handler = handler,
            });
            EnsurePump();
        }

        /// <summary>Delivers every message whose delay has elapsed.</summary>
        public static void Pump()
        {
            double now = Time.unscaledTimeAsDouble;
            ready.Clear();
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                if (queue[i].Due <= now)
                {
                    ready.Add(queue[i]);
                    queue.RemoveAt(i);
                }
            }
            ready.Sort(CompareDelivery);
            for (int i = 0; i < ready.Count; i++)
                Deliver(ready[i]);
        }

        /// <summary>Drops every in-flight message (session teardown).</summary>
        public static void Reset()
        {
            queue.Clear();
            reliableDue.Clear();
        }

        static int CompareDelivery(Pending a, Pending b)
        {
            int byDue = a.Due.CompareTo(b.Due);
            return byDue != 0 ? byDue : a.Seq.CompareTo(b.Seq);
        }

        static void Deliver(Pending entry)
        {
            var reader = new FastBufferReader(
                entry.Bytes, Allocator.Temp, entry.Length);
            using (reader)
                entry.Handler(entry.Sender, reader);
        }

        static double RollDelay()
        {
            double jitter =
                ((rng.NextDouble() * 2.0) - 1.0) * jitterSeconds;
            return Math.Max(0.0, oneWaySeconds + jitter);
        }

        static double ClampReliable(ulong sender, double due)
        {
            if (reliableDue.TryGetValue(sender, out double last)
                && due < last)
            {
                due = last;
            }
            reliableDue[sender] = due;
            return due;
        }

        static void EnsurePump()
        {
            if (pump != null)
                return;
            var host = new GameObject("NetSimulatorPump");
            UnityEngine.Object.DontDestroyOnLoad(host);
            pump = host.AddComponent<NetSimulatorPump>();
        }

        static void EnsureParsed()
        {
            if (parsed)
                return;
            parsed = true;
            double latencyMs = ReadArg("-simLatency");
            double jitterMs = ReadArg("-simJitter");
            double lossPct = ReadArg("-simLoss");
            int seed = (int)ReadArg("-simSeed");
            oneWaySeconds = latencyMs / 2000.0;
            jitterSeconds = jitterMs / 1000.0;
            lossFraction = lossPct / 100.0;
            rng = new System.Random(
                seed != 0 ? seed : Environment.TickCount);
            if (oneWaySeconds > 0 || jitterSeconds > 0
                || lossFraction > 0)
            {
                Debug.Log("[NetSimulator] Enabled: one-way "
                    + $"{latencyMs / 2:0}ms, jitter ±{jitterMs:0}ms, "
                    + $"loss {lossPct:0.#}% (unreliable only)");
            }
        }

        static double ReadArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name && double.TryParse(args[i + 1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double value))
                {
                    return value;
                }
            }
            return 0;
        }
    }
}
