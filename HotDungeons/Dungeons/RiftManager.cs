using ACE.Database;
using ACE.Entity;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Realms;
using ACE.Server.WorldObjects;
using HotDungeons.Dungeons.Entity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HotDungeons.Dungeons
{
    internal class Rift : DungeonBase
    {
        public Position? DropPosition = null;
        public double BonuxXp { get; private set; } = 1.0f;

        public Rift? Next = null;

        public Rift? Previous = null;

        public Landblock? LandblockInstance = null;

        public bool ReadyForLinks = false;

        public bool Linked = false;

        public uint Instance { get; set; } = 0;

        public Rift(string landblock, string name, string coords) : base(landblock, name, coords)
        {
            Landblock = landblock;
            Name = name;
            Coords = coords;
        }

        public void Close()
        {
            foreach(var player in Players.Values)
            {
                if (player != null)
                    player.ExitInstance();
            }

            Instance = 0;
            DropPosition = null;
            Next = null;
            Previous = null;
            Linked = false;
            ReadyForLinks = false;
            LandblockInstance = null;
            Players.Clear();
        }
    }

    internal static class RiftManager 
    {
        private static readonly string[] RiftIds =
        {
            "01AB", // forking trail
            "01CC", // halls of helm
            "01D4", // Bellig Tower
            "01C6", // Hunters Leap
            "01E3", // GlendonWood 
            "01E5", // Green Mire Grave
            "0283", // A Dark Lair
            "02C8", // Banderling Conques
        };

        private static readonly object lockObject = new object();

        private static bool IsOpen = false;

        private static Dictionary<string, Rift> Rifts = new Dictionary<string, Rift>();

        private static Dictionary<string, Rift> ActiveRifts = new Dictionary<string, Rift>();

        private static Dictionary<string, Rift> LastActive = new Dictionary<string, Rift>();

        private static List<Landblock> DestructionQueue = new List<Landblock>();

        private static float MaxBonusXp { get; set; }

        private static TimeSpan ResetInterval { get; set; }
        private static TimeSpan DestructionInterval { get; set; }


        private static DateTime LastResetCheck = DateTime.MinValue;

        private static DateTime LastDestructionCheck = DateTime.MinValue;

        public static void Initialize(uint interval, float bonuxXpModifier)
        {
            Rifts = DungeonRepository.Landblocks
                .Where(kvp => RiftIds.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => new Rift(kvp.Value.Landblock, kvp.Value.Name, kvp.Value.Coords));
            ResetInterval = TimeSpan.FromMinutes(10);
            DestructionInterval = TimeSpan.FromMinutes(7);
            MaxBonusXp = bonuxXpModifier;
        }

        public static void Tick()
        {
            if (LastDestructionCheck + DestructionInterval <= DateTime.UtcNow)
            {
                foreach (var lb in  DestructionQueue)
                {
                    ModManager.Log($"Added {lb.Instance} to destruction queue");
                    LandblockManager.AddToDestructionQueue(lb);
                }
                DestructionQueue.Clear();
                LastDestructionCheck = DateTime.UtcNow;
            }

            if (LastResetCheck + ResetInterval <= DateTime.UtcNow)
            {
                LastResetCheck = DateTime.UtcNow;

                IsOpen = false;

                lock (lockObject)
                {

                    var message = $"Rifts are currently resetting, new rifts will be available shortly";
                    ModManager.Log(message);
                    PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast));

                    foreach (var rift in ActiveRifts)
                    {
                        var lb = rift.Value.LandblockInstance;

                        if (lb != null) 
                            DestructionQueue.Add(lb);

                        rift.Value.Close();
                        LastActive.Add(rift.Key, rift.Value);
                    }

                    ActiveRifts.Clear();

                    SelectRandomRifts(2);

                    CreateActiveRifts();

                    IsOpen = true;
                }
            }
        }

        static void SelectRandomRifts(int count)
        {
            if (Rifts.Count < count)
            {
                throw new InvalidOperationException("Not enough items in Rifts dictionary.");
            }

            List<string> keys = Rifts.Keys.Where(k => !LastActive.ContainsKey(k)).ToList();
            for (int i = 0; i < count; i++)
            {
                int index = ThreadSafeRandom.Next(0, keys.Count - 1);
                string selectedKey = keys[index];
                var rift = Rifts[selectedKey];
                ActiveRifts[selectedKey] = rift;
                keys.RemoveAt(index);

                var at = rift.Coords.Length > 0 ? $"at {rift.Coords}" : "";
                var message = $"Dungeon {rift.Name} {at} is now an activated Rift";
                ModManager.Log(message);
                PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast));
            }

            LastActive.Clear();
        }

        private static void CreateActiveRifts()
        {
            var rifts = ActiveRifts.Values.ToList();
            var count = rifts.Count;
            for (var i = 0; i < count; i++)
            {
                var current = rifts[i];
                if (i == 0 && current != null)
                {
                    var next = rifts[i + 1];
                    current.Next = rifts[i + 1];
                    continue;
                }

                if (i == count - 1 && current != null)
                {
                    current.Previous = rifts[i - 1];
                    continue;
                }

                if (current != null)
                {
                    current.Previous = rifts[i - 1];
                    current.Next = rifts[i + 1];
                }
            }
        }

        public static void RemoveRiftPlayer(string lb, Player player)
        {
            var guid = player.Guid.Full;

            if (ActiveRifts.TryGetValue(lb, out Rift rift))
            {
                if (rift.Players.ContainsKey(guid))
                {
                    rift.Players.Remove(guid);
                    ModManager.Log($"Removed {player.Name} from {rift.Name}");
                }
            }
        }

        public static void AddRiftPlayer(string nextLb, Player player)
        {
            var guid = player.Guid.Full;

            if (ActiveRifts.TryGetValue(nextLb, out Rift rift))
            {
                if (!rift.Players.ContainsKey(guid))
                {
                    rift.Players.TryAdd(guid, player);
                    ModManager.Log($"Added {player.Name} to {rift.Name}");
                }
            }
        }

        public static bool HasRift(string lb)
        {
            return Rifts.ContainsKey(lb);
        }

        public static bool HasActiveRift(string lb)
        {
            lock (lockObject)
            {
                return ActiveRifts.ContainsKey(lb);
            }
        }

        public static bool TryGetActiveRift(string lb, out Rift activeRift)
        {
            lock (lockObject)
            {
                if (ActiveRifts.TryGetValue(lb, out activeRift))
                {
                    return true;
                }
                else
                {
                    activeRift = null; 
                    return false;
                }
            }
        }

        public static Rift CreateRiftInstance(Player creator, Position drop, Rift rift)
        {
            lock (lockObject)
            {
                if (rift.Instance != 0) return rift;

                var rules = new List<Realm>()
                {
                    RealmManager.ServerBaseRealm.Realm,
                    RealmManager.GetRealm(1016).Realm // rift ruleset
                };
                var ephemeralRealm =  RealmManager.GetNewEphemeralLandblock(drop.LandblockId, creator, rules, true);
                var instance = ephemeralRealm.Instance;

                var dropPosition = new Position(drop)
                {
                    Instance = instance
                };

                rift.DropPosition = dropPosition;
                rift.Instance = instance;
                rift.LandblockInstance = ephemeralRealm;
                rift.ReadyForLinks = true;

                ModManager.Log($"Creating Rift instance for {rift.Name} - {instance}");

                var shouldCreateLinks = true;
                var rifts = ActiveRifts.Values.Select(r => r.ReadyForLinks).ToList();

                foreach(var ready in rifts)
                {
                    if (!ready)
                        shouldCreateLinks = false;
                }

                if (shouldCreateLinks)
                    CreateLinks();

                return rift;
            }

        }

        private static List<WorldObject> FindRandomCreatures(Rift rift)
        {
            var lb = rift.LandblockInstance;

            var worldObjects = lb.GetAllWorldObjectsForDiagnostics();

            var portal = worldObjects.Where(wo => wo is Portal).FirstOrDefault();

            var filteredWorldObjects = worldObjects
                .Where(wo => wo is Creature && !(wo is Player) && !wo.IsGenerator)
                .OrderBy(creature => creature, new DistanceComparer(rift.DropPosition))
                .ToList(); // To prevent multiple enumeration

            return filteredWorldObjects;
        }

        private class DistanceComparer : IComparer<WorldObject>
        {
            private Position Location;
            public DistanceComparer(Position location)
            {
                Location = location;
            }
            public int Compare(WorldObject x, WorldObject y)
            {
                return (int)(x.Location.DistanceTo(Location) - y.Location.DistanceTo(Location));
            }
        }

        private static void CreateLinks()
        {
            var rifts = ActiveRifts.Values.ToList();
            var loaded = rifts.All(r => r.LandblockInstance != null && r.LandblockInstance.CreateWorldObjectsCompleted);

            if (!loaded)
            {
                WorldManager.EnqueueAction(new ActionEventDelegate(() =>
                {
                    CreateLinks();
                }));
                return;
            }

            ModManager.Log("Creating Links!");

            foreach (var rift in ActiveRifts.Values.ToList())
            {

                if (rift.Previous != null)
                {
                    var creatures = FindRandomCreatures(rift);

                    foreach (var wo in creatures)
                    {

                        var portal = WorldObjectFactory.CreateNewWorldObject(600004);
                        portal.Name = $"Previous Rift Portal {rift.Previous.Name}";
                        portal.Location = new Position(wo.Location);
                        portal.Destination = rift.Previous.DropPosition;
                        portal.Lifespan = int.MaxValue;

                        var name = "Portal to " +  rift.Previous.Name;
                        portal.SetProperty(ACE.Entity.Enum.Properties.PropertyString.AppraisalPortalDestination, name);
                        portal.ObjScale *= 0.25f;

                        wo.Destroy();
                        if (portal.EnterWorld())
                            break;
                    }
                }

                if (rift.Next != null)
                {
                    var creatures = FindRandomCreatures(rift);

                    foreach (var wo in creatures)
                    {
                        var portal = WorldObjectFactory.CreateNewWorldObject(600004);
                        portal.Name = $"Next Rift Portal {rift.Previous.Name}";
                        portal.Location = new Position(wo.Location);
                        portal.Destination = rift.Next.DropPosition;
                        portal.Lifespan = int.MaxValue;

                        var name = "Portal to " +  rift.Next.Name;
                        portal.SetProperty(ACE.Entity.Enum.Properties.PropertyString.AppraisalPortalDestination, name);
                        portal.ObjScale *= 0.25f;

                        wo.Destroy();
                        if (portal.EnterWorld())
                            break;
                    }
                }
            }
            ModManager.Log("Finished Creating Links!");
        }
    }

}
