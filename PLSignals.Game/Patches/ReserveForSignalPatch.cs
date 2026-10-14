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

        /// <summary>
        /// Virtually reserved shunting signal → main signal whose reservation contains it.
        /// </summary>
        private static readonly Dictionary<SignalBase, SignalBase> s_virtualOwners = new Dictionary<SignalBase, SignalBase>();

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
        /// The virtual reservation ends together with <paramref name="owner"/>'s reservation.
        /// </summary>
        internal static void AddVirtualReservation(SignalBase signal, SignalBase owner)
        {
            if (s_signalsField != null)
            {
                var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;
                signals.Add(signal);
            }

            VirtualReservations.Add(signal);

            if (s_virtualOwners.TryGetValue(signal, out var previousOwner) && previousOwner != owner &&
                PropagationMap.TryGetValue(previousOwner, out var previousList))
            {
                previousList.Remove(signal);
            }

            s_virtualOwners[signal] = owner;

            if (!PropagationMap.TryGetValue(owner, out var list))
            {
                list = new List<SignalBase>();
                PropagationMap[owner] = list;
            }

            if (!list.Contains(signal))
            {
                list.Add(signal);
            }
        }

        internal static bool RemoveVirtualReservation(SignalBase signal)
        {
            if (!VirtualReservations.Remove(signal)) return false;

            if (s_virtualOwners.TryGetValue(signal, out var owner))
            {
                s_virtualOwners.Remove(signal);

                if (PropagationMap.TryGetValue(owner, out var list))
                {
                    list.Remove(signal);
                }
            }

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

            // RemoveVirtualReservation edits the list, so iterate a copy.
            foreach (var signal in virtuals.ToList())
            {
                RemoveVirtualReservation(signal);
                SignalsMod.Log($"Cleared virtual reservation for {signal.Name}");
            }

            PropagationMap.Remove(mainSignal);
        }

        /// <summary>
        /// Drops virtual reservations that <paramref name="mainSignal"/>'s renewed path no longer covers.
        /// </summary>
        internal static void RevalidateVirtualReservationsFor(SignalBase mainSignal)
        {
            if (!PropagationMap.TryGetValue(mainSignal, out var virtuals)) return;

            foreach (var signal in virtuals.ToList())
            {
                if (IsVirtualStillValid(signal)) continue;

                RemoveVirtualReservation(signal);
                SignalsMod.Log($"Cleared stale virtual reservation for {signal.Name}");
            }
        }

        /// <summary>
        /// The ordered tracks a signal actually holds: the truncated walk with switch alignment off, the full block otherwise.
        /// </summary>
        internal static List<TrackInfo>? GetReservedPath(SignalBase signal)
        {
            var block = signal.Block;
            if (block == null) return null;

            if (!TruncateReservationPatch.TruncatedBlocks.TryGetValue(signal, out var truncatedTracks))
            {
                return block.Tracks.ToList();
            }

            var path = new List<TrackInfo>();

            foreach (var trackInfo in block.Tracks)
            {
                if (!truncatedTracks.Contains(trackInfo.Track)) break;
                path.Add(trackInfo);
            }

            return path;
        }

        /// <summary>
        /// True while the shunting block lies entirely inside <paramref name="owner"/>'s reserved path with matching directions.
        /// </summary>
        private static bool IsContainedIn(SignalBase shuntingSignal, SignalBase owner)
        {
            var block = shuntingSignal.Block;
            if (block == null || !TrackReserver.HasReservation(owner)) return false;

            var ownerPath = GetReservedPath(owner);
            if (ownerPath == null) return false;

            var ownerTrackSet = new HashSet<TrackInfo>(ownerPath);

            foreach (var st in block.Tracks)
            {
                if (!TrackReserver.IsTrackReservedBy(st.Track, owner) || !ownerTrackSet.Contains(st))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when the virtual reservation's owner still holds a reservation that contains it.
        /// </summary>
        internal static bool IsVirtualStillValid(SignalBase signal)
        {
            return s_virtualOwners.TryGetValue(signal, out var owner) && IsContainedIn(signal, owner);
        }

        /// <summary>
        /// True when the shunting block lies entirely inside another controller's reservation with matching
        /// directions; the direction check keeps merging branches from counting as contained.
        /// </summary>
        internal static bool IsBlockContainedInReservation(SignalBase shuntingSignal, out SignalBase? owner)
        {
            owner = null;

            var block = shuntingSignal.Block;
            if (block == null || block.Tracks.Length == 0) return false;

            if (!TrackReserver.IsTrackReserved(block.Tracks[0].Track, out var by) || by.Controller == shuntingSignal.Controller)
            {
                return false;
            }

            if (!IsContainedIn(shuntingSignal, by)) return false;

            owner = by;
            return true;
        }

        /// <summary>
        /// Virtually reserves downstream shunting signals along the reserved path, stopping at the
        /// first occupied track. Only signals whose tracks match the path's direction qualify.
        /// </summary>
        internal static void PropagateToShuntingSignals(SignalBase reservedSignal)
        {
            // Switch alignment off: follow the truncated set instead of the full block.
            var path = GetReservedPath(reservedSignal);
            if (path == null) return;

            var reservedTrackSet = new HashSet<TrackInfo>(path);
            var trackOrder = new Dictionary<RailTrack, int>();

            for (int i = 0; i < path.Count; i++)
            {
                trackOrder[path[i].Track] = i;
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

            foreach (var (controller, shunting, _) in candidates)
            {
                if (shunting.Block!.IsOccupied(CrossingCheckMode.WholeTrack))
                {
                    SignalsMod.Log($"Propagation stopped at {controller.Name} ({shunting.Name}): tracks occupied");
                    break;
                }

                if (!TrackReserver.HasReservation(shunting))
                {
                    AddVirtualReservation(shunting, reservedSignal);
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
            s_virtualOwners.Clear();
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
                    // Renewals keep the old virtuals; drop those the new path no longer covers.
                    VirtualReservationHelper.RevalidateVirtualReservationsFor(signal);
                    VirtualReservationHelper.PropagateToShuntingSignals(signal);
                }
                return;
            }

            if (!signal.IsShunting) return;

            if (VirtualReservationHelper.IsBlockContainedInReservation(signal, out var owner))
            {
                VirtualReservationHelper.AddVirtualReservation(signal, owner!);
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