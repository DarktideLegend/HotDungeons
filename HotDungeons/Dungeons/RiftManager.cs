using ACE.Database;
using ACE.Entity;
using ACE.Entity.Models;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Realms;
using ACE.Server.WorldObjects;
using DiscordTurbineChat;
using HotDungeons.Dungeons.Entity;
using Microsoft.Cci.Pdb;
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

        public List<WorldObject> LinkedPortals = new List<WorldObject>();

        public Landblock? LandblockInstance = null;

        public Player Creator;

        public uint Instance { get; set; } = 0;

        public Rift(string landblock, string name, string coords, Position dropPosition, uint instance, Landblock ephemeralRealm, Player creator) : base(landblock, name, coords)
        {
            Landblock = landblock;
            Name = name;
            Coords = coords;
            DropPosition = dropPosition;
            Instance = instance;
            LandblockInstance = ephemeralRealm;
            Creator = creator;
        }

        public void Close()
        {
            foreach (var player in Players.Values)
            {
                if (player != null)
                {
                    player.ExitInstance();
                }
            }

            LandblockInstance.DestroyAllNonPlayerObjects();
            Instance = 0;
            DropPosition = null;
            Next = null;
            Previous = null;
            LandblockInstance.Permaload = false;
            LandblockInstance = null;
            LinkedPortals.Clear();
            Players.Clear();
        }
    }

    internal static class RiftManager
    {
        private static readonly object lockObject = new object();

        private static Dictionary<string, Rift> Rifts = new Dictionary<string, Rift>();

        public static Dictionary<string, Rift> ActiveRifts = new Dictionary<string, Rift>();

        private static float MaxBonusXp { get; set; }

        private static uint MaxActiveRifts { get; set; }

        private static TimeSpan ResetInterval { get; set; }

        private static DateTime LastResetCheck = DateTime.MinValue;

        private static string WebhookGeneral { get; set; }

        private static TimeSpan TimeRemaining => (LastResetCheck + ResetInterval) - DateTime.UtcNow;

        public static void Initialize(uint interval, float bonuxXpModifier, uint maxActiveRifts, string generalWebhook)
        {

            ResetInterval = TimeSpan.FromHours(interval);
            MaxBonusXp = bonuxXpModifier;
            WebhookGeneral = generalWebhook;
            MaxActiveRifts = maxActiveRifts;
        }

        public static void Tick()
        {
            if (LastResetCheck + ResetInterval <= DateTime.UtcNow)
            {
                LastResetCheck = DateTime.UtcNow;

                lock (lockObject)
                {

                    var message = $"Rifts are currently resetting!";
                    ModManager.Log(message);
                    PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast));

                    foreach (var rift in ActiveRifts)
                        rift.Value.Close();

                    ActiveRifts.Clear();
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
            return ActiveRifts.ContainsKey(lb);
        }

        public static bool TryGetActiveRift(string lb, out Rift activeRift)
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

        public static Rift CreateRiftInstance(Player creator, DungeonLandblock dungeon)
        {
            var rules = new List<Realm>()
            {
                RealmManager.ServerBaseRealm.Realm,
                RealmManager.GetRealm(1016).Realm // rift ruleset
            };
            var ephemeralRealm = RealmManager.GetNewEphemeralLandblock(creator.Location.LandblockId, creator, rules, true);
            var instance = ephemeralRealm.Instance;

            var dropPosition = new Position(creator.Location)
            {
                Instance = instance
            };

            var rift = new Rift(dungeon.Landblock, dungeon.Name, dungeon.Coords, dropPosition, instance, ephemeralRealm, creator);

            ModManager.Log($"Creating Rift instance for {rift.Name} - {instance}");

            return rift;
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

        public static string FormatTimeRemaining()
        {
            TimeSpan timeRemaining = TimeRemaining;

            if (timeRemaining.TotalSeconds < 1)
            {
                return "less than a second";
            }
            else if (timeRemaining.TotalMinutes < 1)
            {
                return $"{timeRemaining.Seconds} second{(timeRemaining.Seconds != 1 ? "s" : "")}";
            }
            else if (timeRemaining.TotalHours < 1)
            {
                return $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} and {timeRemaining.Seconds} second{(timeRemaining.Seconds != 1 ? "s" : "")}";
            }
            else
            {
                return $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")}, {timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")}, and {timeRemaining.Seconds} second{(timeRemaining.Seconds != 1 ? "s" : "")}";
            }
        }

        internal static bool TryAddRift(string currentLb, Player killer, DungeonLandblock dungeon, out Rift addedRift)
        {
            addedRift = null;


            if (ActiveRifts.ContainsKey(currentLb))
                return false;

            if (ActiveRifts.Count >= MaxActiveRifts) return false;


            var rift = CreateRiftInstance(killer, dungeon);
            var rifts = ActiveRifts.Values.ToList();

            var last = rifts.LastOrDefault();

            if (last != null)
            {
                rift.Previous = last;
                last.Next = rift;

                _ = SpawnNextAsync(last);
                _ = SpawnPreviousAsync(rift);
            }

            ActiveRifts[currentLb] = rift;

            addedRift = rift;

            var at = rift.Coords.Length > 0 ? $"at {rift.Coords}" : "";
            var message = $"Dungeon {rift.Name} {at} is now an activated Rift";
            ModManager.Log(message);
            PlayerManager.BroadcastToAll(new GameMessageSystemChat(message, ChatMessageType.WorldBroadcast));


            try
            {
                var channel = ChatType.General;
                Player sender = null;
                var formattedMessage = $"[CHAT][{channel.ToString().ToUpper()}] {(sender != null ? sender.Name : "[SYSTEM]")} says on the {channel} channel, \"{message}\"";
                _ = WebhookRepository.SendWebhookChat(DiscordChatChannel.General, formattedMessage, WebhookGeneral);
            } catch (Exception ex)
            {
                ModManager.Log(ex.StackTrace, ModManager.LogLevel.Error);
            }




            return true;
        }


        internal static async Task SpawnPreviousAsync(Rift rift)
        {
            while (!rift.LandblockInstance.CreateWorldObjectsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            List<WorldObject> creatures;


            creatures = FindRandomCreatures(rift);

            if (rift.Previous != null)
            {
                ModManager.Log($"Creatures Count {creatures.Count} in {rift.Name}");

                foreach (var wo in creatures)
                {
                    var portal = WorldObjectFactory.CreateNewWorldObject(600004);
                    portal.Name = $"Rift Portal {rift.Previous.Name}";
                    portal.Location = new Position(wo.Location);
                    portal.Destination = rift.Previous.DropPosition;
                    portal.Lifespan = int.MaxValue;

                    var name = "Portal to " + rift.Previous.Name;
                    portal.SetProperty(ACE.Entity.Enum.Properties.PropertyString.AppraisalPortalDestination, name);
                    portal.ObjScale *= 0.25f;

                    ModManager.Log($"Attempting to link Portal for previous in {rift.Name}");
                    if (portal.EnterWorld())
                    {
                        ModManager.Log($"Added Linked Portal for previous in {rift.Name}");
                        rift.LinkedPortals.Add(portal);
                        return;
                    }
                }
            }
        }

        internal static async Task SpawnNextAsync(Rift rift)
        {
            while (!rift.LandblockInstance.CreateWorldObjectsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            List<WorldObject> creatures;


            creatures = FindRandomCreatures(rift);

            if (rift.Next != null)
            {
                ModManager.Log($"Creatures Count {creatures.Count} in {rift.Name}");
                foreach (var wo in creatures)
                {
                    var portal = WorldObjectFactory.CreateNewWorldObject(600004);
                    portal.Name = $"Rift Portal {rift.Next.Name}";
                    portal.Location = new Position(wo.Location);
                    portal.Destination = rift.Next.DropPosition;
                    portal.Lifespan = int.MaxValue;

                    var name = "Portal to " + rift.Next.Name;
                    portal.SetProperty(ACE.Entity.Enum.Properties.PropertyString.AppraisalPortalDestination, name);
                    portal.ObjScale *= 0.25f;

                    ModManager.Log($"Attempting to link Portal for next in {rift.Name}");
                    if (portal.EnterWorld())
                    {
                        ModManager.Log($"Added Linked Portal for next in {rift.Name}");
                        rift.LinkedPortals.Add(portal);
                        return;
                    }
                }
            }
        }
    }
}
