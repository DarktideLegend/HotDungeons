using System;
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

        public DungeonLandblock(string landblock, string name, string coords)
        {
            Landblock = landblock;
            Name = name;
            Coords = coords;
        }

        public Dictionary<uint, Player> Players { get; set; } = new Dictionary<uint, Player>();
    }

    internal class DungeonManager
    {
        private static string CsvFile = "dungeon_ids.csv";

        public static Dictionary<string, DungeonLandblock> Landblocks = new Dictionary<string, DungeonLandblock>();

        public static void Initialize() 
        {
            if (Landblocks.Count == 0) 
            {
                ImportDungeonsFromCsv();
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

                    ModManager.Log($"landblock: {landblock} - name: {name} - coords{coords} ");

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
    }
}
