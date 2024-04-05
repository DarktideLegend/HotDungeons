using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using HotDungeons.Dungeons.Entity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Dungeons
{
    internal class Dungeon: DungeonBase
    {

        public int TotalXpEarned { get; set; } = 0;

        public double BonuxXp { get; set; } = 1.0f;

        public Dungeon(string landblock, string name, string coords) : base(landblock, name, coords) 
        {
            Landblock = landblock;
            Name = name;
            Coords = coords;
        }

        internal void AddTotalXp(int xpOverride)
        {
            TotalXpEarned += xpOverride;
        }
    }

    internal class DungeonManager
    {

        private static readonly object lockObject = new object();

        private static Dictionary<string, Dungeon> Dungeons = new Dictionary<string, Dungeon>();

        private static Dictionary<string, Dungeon> PotentialHotspotCandidate = new Dictionary<string, Dungeon>();

        private static float MaxBonusXp { get; set; }

        public static Dungeon? CurrentHotSpot { get; private set; }

        private static TimeSpan ElectorInterval { get; set; }

        private static DateTime LastElectorCheck = DateTime.MinValue;

        private static TimeSpan TimeRemaining => (LastElectorCheck + ElectorInterval) - DateTime.UtcNow;

        private static bool ProcessingTick = false;

        public static void Initialize(uint interval, float bonuxXpModifier)
        {
            ElectorInterval = TimeSpan.FromMinutes(interval);
            MaxBonusXp = bonuxXpModifier;
        }


        public static void Tick()
        {
            if (LastElectorCheck + ElectorInterval <= DateTime.UtcNow)
            {
                ProcessingTick = true;
                LastElectorCheck = DateTime.UtcNow;

                lock (lockObject)
                {

                    if (CurrentHotSpot != null)
                    {
                        var message = $"{CurrentHotSpot.Name} is no longer boosted xp!";
                        ModManager.Log(message);
                        PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast));
                    }

                    var candidates = PotentialHotspotCandidate.Values.OrderByDescending(d => d.TotalXpEarned).ToList();

                    if (candidates.Count > 0)
                    {
                        var elected = candidates.First();
                        elected.BonuxXp = ThreadSafeRandom.Next(1.5f, MaxBonusXp);
                        CurrentHotSpot = elected;

                        foreach (var candidate in candidates)
                        {
                            candidate.TotalXpEarned = 0;
                        }

                        PotentialHotspotCandidate.Clear();
                        var at = elected.Coords.Length > 0 ? $"at {elected.Coords}" : "";
                        var message = $"{elected.Name} {at} has been very active, this dungeon has been boosted with {elected.BonuxXp.ToString("0.00")}x xp for the next hour!";
                        ModManager.Log(message);
                        PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast));
                    }
                    else
                    {
                        CurrentHotSpot = null;
                    }
                }

                ProcessingTick = false;
            }
        }

        public static void RemoveDungeonPlayer(string lb, Player player)
        {
            var guid = player.Guid.Full;

            if (DungeonRepository.Landblocks.TryGetValue(lb, out DungeonLandblock currentDungeon) && Dungeons.TryGetValue(lb, out Dungeon dungeon))
            {
                if (dungeon.Players.ContainsKey(guid))
                {
                    dungeon.Players.Remove(guid);
                    ModManager.Log($"Removed {player.Name} from {dungeon.Name}");
                }
            } 
        }

        public static void AddDungeonPlayer(string nextLb, Player player)
        {
            var guid = player.Guid.Full;

            if (DungeonRepository.Landblocks.TryGetValue(nextLb, out DungeonLandblock nextDungeon) && Dungeons.TryGetValue(nextLb, out Dungeon dungeon))
            {
                if (!dungeon.Players.ContainsKey(guid))
                {
                    dungeon.Players.TryAdd(guid, player);
                    ModManager.Log($"Added {player.Name} to {dungeon.Name}");
                }
            }
        }

        public static bool HasDungeon(string lb)
        {
            return DungeonRepository.Landblocks.ContainsKey(lb);
        }

        internal static void ProcessCreaturesDeath(string currentLb, int xpOverride)
        {
            if (ProcessingTick)
                return;

            if (RiftManager.HasRift(currentLb))
                return;

            if (DungeonRepository.Landblocks.TryGetValue(currentLb, out DungeonLandblock currentDungeon))
            {
                if (CurrentHotSpot == null)
                {
                    var newDungeon = new Dungeon(currentDungeon.Landblock, currentDungeon.Name, currentDungeon.Coords);
                    CurrentHotSpot = newDungeon;
                    CurrentHotSpot.BonuxXp = ThreadSafeRandom.Next(1.5f, MaxBonusXp);
                    PotentialHotspotCandidate.Clear();
                    var at = newDungeon.Coords.Length > 0 ? $"at {newDungeon.Coords}" : "";
                    var message = $"{newDungeon.Name} {at} has been very active, this dungeon has been boosted with {newDungeon.BonuxXp.ToString("0.00")}x xp for the next hour!";
                    ModManager.Log(message);
                    PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast));
                    return;
                }

                if (CurrentHotSpot.Name == currentDungeon.Name)
                    return;

                if (!PotentialHotspotCandidate.TryGetValue(currentLb, out Dungeon dungeon))
                {
                    var newDungeon = new Dungeon(currentDungeon.Landblock, currentDungeon.Name, currentDungeon.Coords);
                    newDungeon.AddTotalXp(xpOverride);
                    PotentialHotspotCandidate.TryAdd(currentLb, newDungeon);
                } else 
                    dungeon.AddTotalXp(xpOverride);    
            }
        }

        public static string FormatTimeRemaining()
        {
            if (TimeRemaining.TotalSeconds < 1)
            {
                return "less than a second";
            }
            else if (TimeRemaining.TotalMinutes < 1)
            {
                return $"{TimeRemaining.Seconds} seconds";
            }
            else
            {
                return $"{TimeRemaining.Minutes} minutes and {TimeRemaining.Seconds} seconds";
            }
        }
    }

}
