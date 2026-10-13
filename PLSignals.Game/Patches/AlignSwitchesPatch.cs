using HarmonyLib;
using Signals.Game;
using Signals.Game.Railway;
using System.Collections.Generic;
using System.Reflection;
using SignalBase = Signals.Game.Signal;

namespace PLSignals.Patches
{
    /// <summary>
    /// With "Align Switches on Reserve" off, truncates the reservation at the first misaligned switch instead of flipping it.
    /// </summary>
    [HarmonyPatch(typeof(TrackReserver), nameof(TrackReserver.ReserveForSignal), new[] { typeof(SignalBase) })]
    internal static class TruncateReservationPatch
    {
        private static readonly FieldInfo? s_reservationsField =
            typeof(TrackReserver).GetField("s_reservations", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo? s_signalsField =
            typeof(TrackReserver).GetField("s_signals", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// Truncated track set per reserved signal, used by propagation instead of the full block.
        /// </summary>
        internal static readonly Dictionary<SignalBase, HashSet<RailTrack>> TruncatedBlocks = new Dictionary<SignalBase, HashSet<RailTrack>>();

        private static bool Prefix(SignalBase signal, ref bool __result)
        {
            if (Main.Settings.AlignSwitchesOnReserve) return true;
            if (!SignalManager.Running || SignalManager.CurrentPack.ModId != "PLSignals") return true;

            signal.Controller.UpdateBlocks();

            var block = signal.Block;
            if (block == null)
            {
                __result = false;
                return false;
            }

            if (s_reservationsField == null || s_signalsField == null)
            {
                // Reflection failed, fall back to original.
                return true;
            }

            var reservations = (Dictionary<RailTrack, SignalBase>)s_reservationsField.GetValue(null)!;
            var signals = (HashSet<SignalBase>)s_signalsField.GetValue(null)!;

            // Check if another signal already reserved our tracks.
            foreach (var track in block.AllTracks)
            {
                if (reservations.TryGetValue(track, out var by) && by.Controller != signal.Controller)
                {
                    __result = false;
                    return false;
                }
            }

            if (signals.Contains(signal))
            {
                TrackReserver.ClearFromSignal(signal);
            }

            var hasTracks = false;
            var truncatedSet = new HashSet<RailTrack>();

            foreach (var trackInfo in block.Tracks)
            {
                if (trackInfo.IsJunctionTrack)
                {
                    var junction = trackInfo.Track.inJunction;

                    if (trackInfo.Direction == TrackDirection.In &&
                        junction.outBranches[junction.selectedBranch].track != trackInfo.Track)
                    {
                        // Stop before the misaligned switch track.
                        break;
                    }
                }

                truncatedSet.Add(trackInfo.Track);
            }

            // Add only extra tracks that belong to junctions already in our truncated set,
            // and only from the SAME branch direction. We build a set of junctions
            // we've encountered to filter properly.
            var includedJunctions = new HashSet<Junction>();
            foreach (var trackInfo in block.Tracks)
            {
                if (truncatedSet.Contains(trackInfo.Track) && trackInfo.IsJunctionTrack)
                {
                    includedJunctions.Add(trackInfo.Track.inJunction);
                }
            }

            foreach (var track in block.ExtraTracks)
            {
                // Only add extra tracks from junctions we've included,
                // and only if the track is also in our truncated set.
                if (truncatedSet.Contains(track) && !track.isJunctionTrack)
                {
                    truncatedSet.Add(track);
                }
            }

            foreach (var track in truncatedSet)
            {
                if (!reservations.ContainsKey(track))
                {
                    reservations.Add(track, signal);
                    hasTracks = true;
                }
            }

            if (!hasTracks)
            {
                __result = false;
                return false;
            }

            signals.Add(signal);
            TrackReserver.ReservationMade?.Invoke(signal);

            // Store the truncated track set for propagation to use.
            TruncatedBlocks[signal] = truncatedSet;

            __result = true;
            return false;
        }
    }

    /// <summary>
    /// Blocks switch alignment when the setting is off. AlignAllSwitches runs outside
    /// ReserveForSignal (CommsRadioSignalReserver calls it separately), so it needs its own guard.
    /// </summary>
    [HarmonyPatch(typeof(SignalBase), "AlignAllSwitches")]
    internal static class AlignAllSwitchesPatch
    {
        private static bool Prefix()
        {
            if (Main.Settings.AlignSwitchesOnReserve) return true;
            if (!SignalManager.Running || SignalManager.CurrentPack.ModId != "PLSignals") return true;

            return false;
        }
    }
}