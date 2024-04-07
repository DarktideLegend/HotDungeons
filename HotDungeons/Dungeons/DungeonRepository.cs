using HotDungeons.Dungeons.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Dungeons
{
    internal static class DungeonRepository
    {
        public static Dictionary<string, DungeonLandblock> Landblocks = new Dictionary<string, DungeonLandblock>();

        private static string CsvFile = "dungeon_ids.csv";

        public static void Initialize()
        {
            if (Landblocks.Count == 0)
            {
                ImportDungeonsFromCsv();
            }
        }

        private static void ImportDungeonsFromCsv()
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

                    Entity.DungeonLandblock dungeon = new Entity.DungeonLandblock(landblock, name, coords);

                    Landblocks[landblock] = dungeon;
                }
            }
        }

    }
}
