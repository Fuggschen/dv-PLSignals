using HarmonyLib;
using Signals.Common;
using Signals.Game;
using Signals.Game.Controllers;
using Signals.Game.Railway;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    internal static class VirtualReservationHelper
    {
        internal const string ModId = "PLSignals";

        /// <summary>
        /// Main signal → shunting signals whose virtual reservations it spawned.
        /// </summary>
        internal static readonly Dictionary<SignalBase, List<SignalBase>> PropagationMap = new Dictionary<SignalBase, List<SignalBase>>();

        internal static readonly HashSet<SignalBase> VirtualReservations = new HashSet<SignalBase>();

        internal static readonly FieldInfo? s_signalsField =
            typeof(TrackReserver).GetField("s_signals", BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly FieldInfo? s_clearRoutinesField =
            typeof(TrackReserver).GetField("s_clearRoutines", BindingFlags.NonPublic | BindingFlags.Static);

        internal static readonly FieldInfo? s_timesField =
            typeof(TrackReserver).GetField("s_times", BindingFlags.NonPublic | BindingFlags.Static);

        internal static bool IsOurPackActive()
        {
            return SignalManager.Running && SignalManager.CurrentPack.ModId == ModId;
        }

        /// <summary>
        /// Makes the signal look reserved (HasReservation) without owning any tracks in s_reservations.
        /// </summary>
        internal static void AddVirtualReservation(SignalBase signal)
        {
            if (s_signalsField != null)
            {
                var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;
                signals.Add(signal);
            }

            VirtualReservations.Add(signal);
        }

        internal static bool RemoveVirtualReservation(SignalBase signal)
        {
            if (!VirtualReservations.Remove(signal)) return false;

            if (s_signalsField != null)
            {
                var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;
                signals.Remove(signal);
            }

            if (s_clearRoutinesField != null)
            {
                var routines = (Dictionary<SignalBase, Coroutine>)s_clearRoutinesField.GetValue(null)!;
                if (routines.TryGetValue(signal, out var coroutine))
                {
                    CoroutineManager.Instance.Stop(coroutine);
                    routines.Remove(signal);
                }
            }

            if (s_timesField != null)
            {
                var times = (Dictionary<SignalBase, float>)s_timesField.GetValue(null)!;
                times.Remove(signal);
            }

            return true;
        }

        /// <summary>
        /// Called when a main signal's reservation ends; drops the virtual reservations it spawned.
        /// </summary>
        internal static void ClearVirtualReservationsFor(SignalBase mainSignal)
        {
            if (!PropagationMap.TryGetValue(mainSignal, out var virtuals)) return;

            foreach (var signal in virtuals)
            {
                RemoveVirtualReservation(signal);
                SignalsMod.Log($"Cleared virtual reservation for {signal.Name}");
            }

            PropagationMap.Remove(mainSignal);
        }

        /// <summary>
        /// True when the shunting block lies entirely inside another signal's reservation with matching
        /// directions; the direction check keeps merging branches from counting as contained.
        /// </summary>
        internal static bool IsBlockContainedInReservation(SignalBase shuntingSignal)
        {
            var block = shuntingSignal.Block;
            if (block == null) return false;

            var shuntingTracks = block.Tracks;

            foreach (var trackInfo in shuntingTracks)
            {
                if (!TrackReserver.IsTrackReserved(trackInfo.Track, out var by) || by.Controller == shuntingSignal.Controller)
                {
                    return false;
                }

                var mainBlock = by.Block;
                if (mainBlock == null) return false;

                var mainTrackSet = new HashSet<TrackInfo>(mainBlock.Tracks);

                foreach (var st in shuntingTracks)
                {
                    if (!TrackReserver.IsTrackReserved(st.Track, out var by2) || by2.Controller == shuntingSignal.Controller)
                    {
                        return false;
                    }

                    if (!mainTrackSet.Contains(st))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Virtually reserves downstream shunting signals along the reserved path, stopping at the
        /// first occupied track. Only signals whose tracks match the path's direction qualify.
        /// </summary>
        internal static void PropagateToShuntingSignals(SignalBase reservedSignal)
        {
            var block = reservedSignal.Block;
            if (block == null) return;

            // Switch alignment off: follow the truncated set instead of the full block.
            bool hasTruncated = TruncateReservationPatch.TruncatedBlocks.TryGetValue(reservedSignal, out var truncatedTracks);

            var reservedTrackSet = new HashSet<TrackInfo>();

            var trackOrder = new Dictionary<RailTrack, int>();

            if (hasTruncated)
            {
                int order = 0;
                foreach (var trackInfo in block.Tracks)
                {
                    if (!truncatedTracks!.Contains(trackInfo.Track)) break;
                    reservedTrackSet.Add(trackInfo);
                    trackOrder[trackInfo.Track] = order++;
                }
            }
            else
            {
                reservedTrackSet = new HashSet<TrackInfo>(block.Tracks);
                for (int i = 0; i < block.Tracks.Length; i++)
                {
                    trackOrder[block.Tracks[i].Track] = i;
                }
            }

            var candidates = new List<(BasicSignalController Controller, SignalBase Shunting, int Order)>();

            foreach (var controller in SignalManager.Instance.AllControllers)
            {
                if (controller.ShuntingSignals.Length == 0) continue;

                foreach (var shunting in controller.ShuntingSignals)
                {
                    if (shunting.Block == null) continue;

                    var shuntingTracks = shunting.Block.Tracks;

                    // Every track must match the reserved set and direction, so wrong-way signals stay out.
                    bool allContained = true;
                    int earliestOrder = int.MaxValue;

                    foreach (var st in shuntingTracks)
                    {
                        if (!reservedTrackSet.Contains(st))
                        {
                            allContained = false;
                            break;
                        }

                        if (trackOrder.TryGetValue(st.Track, out var order) && order < earliestOrder)
                        {
                            earliestOrder = order;
                        }
                    }

                    if (!allContained) continue;

                    candidates.Add((controller, shunting, earliestOrder));
                }
            }

            candidates.Sort((a, b) => a.Order.CompareTo(b.Order));

            if (!PropagationMap.ContainsKey(reservedSignal))
            {
                PropagationMap[reservedSignal] = new List<SignalBase>();
            }

            foreach (var (controller, shunting, _) in candidates)
            {
                if (shunting.Block!.IsOccupied(CrossingCheckMode.WholeTrack))
                {
                    SignalsMod.Log($"Propagation stopped at {controller.Name} ({shunting.Name}): tracks occupied");
                    break;
                }

                if (!TrackReserver.HasReservation(shunting))
                {
                    AddVirtualReservation(shunting);
                    PropagationMap[reservedSignal].Add(shunting);
                    SignalsMod.Log($"Virtually reserved shunting signal {shunting.Name} on {controller.Name}");
                }
            }
        }

        /// <summary>
        /// Clears all virtual reservations. Called when the signal system resets.
        /// </summary>
        internal static void ClearAll()
        {
            foreach (var signal in VirtualReservations.ToList())
            {
                RemoveVirtualReservation(signal);
            }

            PropagationMap.Clear();
        }
    }

    /// <summary>
    /// Lets shunting signals reserve when their block sits inside a main signal's reservation
    /// (matching directions), and propagates reservations to downstream shunting signals.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ReserveForSignal), new[] { typeof(SignalBase) })]
    internal static class ReserveForSignalPatch
    {
        private static void Postfix(ref bool __result, SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            if (__result)
            {
                if (!signal.IsShunting)
                {
                    VirtualReservationHelper.PropagateToShuntingSignals(signal);
                }
                return;
            }

            if (!signal.IsShunting) return;

            if (VirtualReservationHelper.IsBlockContainedInReservation(signal))
            {
                VirtualReservationHelper.AddVirtualReservation(signal);
                __result = true;
            }
        }
    }

    /// <summary>
    /// Cleans up truncated-block tracking and virtual reservations when a reservation is cleared,
    /// including propagated virtuals when a main signal's reservation expires.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ClearFromSignal))]
    internal static class ClearFromSignalPatch
    {
        private static void Prefix(SignalBase signal)
        {
            if (!VirtualReservationHelper.IsOurPackActive()) return;

            TruncateReservationPatch.TruncatedBlocks.Remove(signal);
            VirtualReservationHelper.RemoveVirtualReservation(signal);
            VirtualReservationHelper.ClearVirtualReservationsFor(signal);
        }
    }

    /// <summary>
    /// Patches <see cref="TrackReserver.ClearAll"/> to also clear virtual reservations.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ClearAll))]
    internal static class ClearAllPatch
    {
        private static void Postfix()
        {
            VirtualReservationHelper.ClearAll();
        }
    }
}