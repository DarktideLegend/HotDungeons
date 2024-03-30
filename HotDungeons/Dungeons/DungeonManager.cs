using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Dungeons
{
    internal class DungeonLandblock
    {
        public string Landblock { get; set; }
        public string Name { get; set; }
        public string Coords { get; set; }
        public int TotalXpEarned { get; set; } = 0;

        public double BonuxXp { get; set; } = 1.0f;

        public DungeonLandblock(string landblock, string name, string coords)
        {
            Landblock = landblock;
            Name = name;
            Coords = coords;
        }

        public Dictionary<uint, Player> Players { get; set; } = new Dictionary<uint, Player>();

        internal void AddTotalXp(int xpOverride)
        {
            TotalXpEarned += xpOverride;
        }
    }

    internal class DungeonManager
    {
        private static string CsvFile = "dungeon_ids.csv";

        private static readonly object lockObject = new object();

        private static Dictionary<string, DungeonLandblock> Landblocks = new Dictionary<string, DungeonLandblock>();

        private static Dictionary<string, DungeonLandblock> PotentialHotspotCandidate = new Dictionary<string, DungeonLandblock>();

        private static float MaxBonusXp { get; set; }

        public static DungeonLandblock? CurrentHotSpot { get; private set; }

        private static TimeSpan ElectorInterval { get; set; }

        private static DateTime LastElectorCheck = DateTime.MinValue;

        public static void Initialize(uint interval, float bonuxXpModifier)
        {
            if (Landblocks.Count == 0)
            {
                ImportDungeonsFromCsv();
                ElectorInterval = TimeSpan.FromMinutes(interval);
                MaxBonusXp = bonuxXpModifier;
            }
        }

        public static void Tick()
        {
            if (LastElectorCheck + ElectorInterval <= DateTime.UtcNow)
            {
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
            }
        }

        public static void ImportDungeonsFromCsv()
        {
            string csvFilePath = Path.Combine(Mod.ModPath, CsvFile);

            if (!File.Exists(csvFilePath))
            {
                throw new Exception("Failed to read dungeon_ids.csv");
            }

            using (StreamReader reader = new StreamReader(csvFilePath))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] parts = line.Split(';');

                    string landblock = parts[0];
                    string name = parts[1];
                    string coords = parts[2];

                    if (coords.Length == 2)
                        coords = "";
                    else if (coords.Length > 2 && coords.Substring(coords.Length - 2) == ",,")
                    {
                        coords = coords.Substring(0, coords.Length - 2);
                    }

                    DungeonLandblock dungeon = new DungeonLandblock(landblock, name, coords);

                    Landblocks[landblock] = dungeon;
                }
            }
        }
        public static void RemoveDungeonPlayer(string lb, Player player)
        {
            var guid = player.Guid.Full;

            if (Landblocks.TryGetValue(lb, out DungeonLandblock currentDungeon))
                if (currentDungeon.Players.ContainsKey(guid))
                {
                    currentDungeon.Players.Remove(guid);
                    ModManager.Log($"Removed {player.Name} from {currentDungeon.Name}");
                }
        }

        public static void AddDungeonPlayer(string nextLb, Player player)
        {
            var guid = player.Guid.Full;

            if (Landblocks.TryGetValue(nextLb, out DungeonLandblock nextDungeon))
                if (!nextDungeon.Players.ContainsKey(guid))
                {
                    nextDungeon.Players.TryAdd(guid, player);
                    ModManager.Log($"Added {player.Name} to {nextDungeon.Name}");
                }
        }

        public static bool HasDungeon(string lb)
        {
            return Landblocks.ContainsKey(lb);
        }



        internal static void ProcessCreaturesDeath(string currentLb, int xpOverride)
        {
            if (Landblocks.TryGetValue(currentLb, out DungeonLandblock currentDungeon))
            {
                if (CurrentHotSpot == currentDungeon)
                    return;

                lock (lockObject)
                {
                    currentDungeon.AddTotalXp(xpOverride);

                    if (!PotentialHotspotCandidate.ContainsKey(currentLb))
                    {
                        PotentialHotspotCandidate.TryAdd(currentLb, currentDungeon);
                    }
                }
            }
        }
    }

}
