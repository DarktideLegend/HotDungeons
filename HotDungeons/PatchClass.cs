using ACE.Entity;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Physics;
using ACE.Server.WorldObjects;
using HotDungeons.Dungeons;
using Iced.Intel.EncoderInternal;
using System;
using static Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId;

namespace HotDungeons
{
    [HarmonyPatch]
    public class PatchClass
    {
        #region Settings
        const int RETRIES = 10;

        public static Settings Settings = new();
        static string settingsPath => Path.Combine(Mod.ModPath, "Settings.json");
        private FileInfo settingsInfo = new(settingsPath);

        private JsonSerializerOptions _serializeOptions = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private void SaveSettings()
        {
            string jsonString = JsonSerializer.Serialize(Settings, _serializeOptions);

            if (!settingsInfo.RetryWrite(jsonString, RETRIES))
            {
                ModManager.Log($"Failed to save settings to {settingsPath}...", ModManager.LogLevel.Warn);
                Mod.State = ModState.Error;
            }
        }

        private void LoadSettings()
        {
            if (!settingsInfo.Exists)
            {
                ModManager.Log($"Creating {settingsInfo}...");
                SaveSettings();
            }
            else
                ModManager.Log($"Loading settings from {settingsPath}...");

            if (!settingsInfo.RetryRead(out string jsonString, RETRIES))
            {
                Mod.State = ModState.Error;
                return;
            }

            try
            {
                Settings = JsonSerializer.Deserialize<Settings>(jsonString, _serializeOptions);
            }
            catch (Exception)
            {
                ModManager.Log($"Failed to deserialize Settings: {settingsPath}", ModManager.LogLevel.Warn);
                Mod.State = ModState.Error;
                return;
            }
        }
        #endregion

        #region Start/Shutdown
        public void Start()
        {
            //Need to decide on async use
            Mod.State = ModState.Loading;
            LoadSettings();

            if (Mod.State == ModState.Error)
            {
                ModManager.DisableModByPath(Mod.ModPath);
                return;
            }

            Mod.State = ModState.Running;

            DungeonRepository.Initialize();
            DungeonManager.Initialize(Settings.DungeonCheckInterval, Settings.MaxBonusXp);
            RiftManager.Initialize(Settings.RiftCheckInterval, Settings.RiftMaxBonusXp);
        }

        public void Shutdown()
        {
            //if (Mod.State == ModState.Running)
            // Shut down enabled mod...

            //If the mod is making changes that need to be saved use this and only manually edit settings when the patch is not active.
            //SaveSettings();

            if (Mod.State == ModState.Error)
                ModManager.Log($"Improper shutdown: {Mod.ModPath}", ModManager.LogLevel.Error);
        }
        #endregion

        #region Patches

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Landblock), "ProcessPendingWorldObjectAdditionsAndRemovals")]
        public static bool PreProcessPendingWorldObjectAdditionsAndRemovals(ref Landblock __instance)
        {
            if (__instance.pendingAdditions.Count > 0)
            {
                foreach (var kvp in __instance.pendingAdditions)
                {
                    __instance.worldObjects[kvp.Key] = kvp.Value;

                    if (kvp.Value is Player player)
                    {
                        __instance.players.Add(player);
                        var currentLb = $"{__instance.Id.Raw:X8}".Substring(0, 4);

                        if (RiftManager.HasActiveRift(currentLb))
                            RiftManager.AddRiftPlayer(currentLb, player);

                        if (DungeonManager.HasDungeon(currentLb))
                            DungeonManager.AddDungeonPlayer(currentLb, player);


                    }
                    else if (kvp.Value is Creature creature)
                        __instance.sortedCreaturesByNextTick.AddLast(creature);

                    __instance.InsertWorldObjectIntoSortedHeartbeatList(kvp.Value);
                    __instance.InsertWorldObjectIntoSortedGeneratorUpdateList(kvp.Value);
                    __instance.InsertWorldObjectIntoSortedGeneratorRegenerationList(kvp.Value);

                    if (kvp.Value.WeenieClassId == 80007) // Landblock KeepAlive weenie (ACE custom)
                        __instance.HasNoKeepAliveObjects = false;
                }

                __instance.pendingAdditions.Clear();
            }

            if (__instance.pendingRemovals.Count > 0)
            {
                foreach (var objectGuid in __instance.pendingRemovals)
                {
                    if (__instance.worldObjects.Remove(objectGuid, out var wo))
                    {
                        if (wo is Player player)
                        {
                            __instance.players.Remove(player);
                            var currentLb = $"{__instance.Id.Raw:X8}".Substring(0, 4);

                            if (RiftManager.HasActiveRift(currentLb))
                                RiftManager.RemoveRiftPlayer(currentLb, player);

                            if (DungeonManager.HasDungeon(currentLb))
                                DungeonManager.RemoveDungeonPlayer(currentLb, player);

                        }
                        else if (wo is Creature creature)
                            __instance.sortedCreaturesByNextTick.Remove(creature);

                        __instance.sortedWorldObjectsByNextHeartbeat.Remove(wo);
                        __instance.sortedGeneratorsByNextGeneratorUpdate.Remove(wo);
                        __instance.sortedGeneratorsByNextRegeneration.Remove(wo);

                        if (wo.WeenieClassId == 80007) // Landblock KeepAlive weenie (ACE custom)
                        {
                            var keepAliveObject = __instance.worldObjects.Values.FirstOrDefault(w => w.WeenieClassId == 80007);

                            if (keepAliveObject == null)
                                __instance.HasNoKeepAliveObjects = true;
                        }
                    }
                }

                __instance.pendingRemovals.Clear();
            }

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(LandblockManager), "TickMultiThreadedWork")]
        public static bool PreTickMultiThreadedWork()
        {
            DungeonManager.Tick();
            RiftManager.Tick();
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Creature), nameof(Creature.OnDeath_GrantXP))]
        public static bool PreOnDeath_GrantXP(ref Creature __instance)
        {
            if (__instance is Player && __instance.PlayerKillerStatus == PlayerKillerStatus.PKLite)
                return false;

            var totalHealth = __instance.DamageHistory.TotalHealth;

            if (totalHealth == 0)
                return false;

            foreach (var kvp in __instance.DamageHistory.TotalDamage)
            {
                var damager = kvp.Value.TryGetAttacker();

                var playerDamager = damager as Player;

                if (playerDamager == null && kvp.Value.PetOwner != null)
                    playerDamager = kvp.Value.TryGetPetOwner();

                if (playerDamager == null)
                    continue;

                var totalDamage = kvp.Value.TotalDamage;

                var damagePercent = totalDamage / totalHealth;

                var currentLb = $"{__instance.CurrentLandblock.Id.Raw:X8}".Substring(0, 4);

                if (__instance.CurrentLandblock != null)
                    DungeonManager.ProcessCreaturesDeath(currentLb, (int)__instance.XpOverride);

                var xp = (double)(__instance.XpOverride ?? 0);

                if (DungeonManager.CurrentHotSpot?.Landblock == currentLb)
                    xp *= DungeonManager.CurrentHotSpot.BonuxXp;

                var totalXP = (xp) * damagePercent;

                playerDamager.EarnXP((long)Math.Round(totalXP), XpType.Kill);

                // handle luminance
                if (__instance.LuminanceAward != null)
                {
                    var totalLuminance = (long)Math.Round(__instance.LuminanceAward.Value * damagePercent);
                    playerDamager.EarnLuminance(totalLuminance, XpType.Kill);
                }
            }

            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Creature), nameof(Creature.OnDeath_GrantXP))]
        public static void PostOnDeath_GrantXP(ref Creature __instance)
        {
            if (__instance.CurrentLandblock != null)
            {
                var currentLb = $"{__instance.CurrentLandblock.Id.Raw:X8}".Substring(0, 4);

                DungeonManager.ProcessCreaturesDeath(currentLb, (int)__instance.XpOverride);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), nameof(Player.Teleport), new Type[] { typeof(Position), typeof(bool), typeof(bool) })]
        public static bool PreTeleport(Position _newPosition, bool teleportingFromInstance, bool fromPortal, ref Player __instance)
        {
            var newLbRaw = _newPosition.LandblockId.Raw;
            var nextLb = $"{newLbRaw:X8}".Substring(0, 4);

            if (!RiftManager.TryGetActiveRift(nextLb, out Rift activeRift))
                return true;

            if (activeRift.Instance == 0)
                RiftManager.CreateRiftInstance(__instance, _newPosition, activeRift);

            var currentLbRaw = __instance.Location.LandblockId.Raw;
            var currentLb = $"{currentLbRaw:X8}".Substring(0, 4);

            RiftManager.TryGetActiveRift(currentLb, out Rift currentRift);

            var pos = new Position(_newPosition);
            pos.Instance = activeRift.Instance;

            _newPosition.Instance = pos.Instance;

            Position.ParseInstanceID(__instance.Location.Instance, out var isTemporaryRuleset, out ushort _a, out ushort _b);
            if (isTemporaryRuleset)
            {
                if (!teleportingFromInstance && __instance.ExitInstance())
                    return false;
            }


            if (!__instance.ValidatePlayerRealmPosition(_newPosition))
            {
                if (__instance.IsAdmin)
                {
                    __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Admin bypassing realm restriction.", ChatMessageType.System));
                }
                else
                {
                    __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Unable to teleport to that realm.", ChatMessageType.System));
                    return false;
                }
            }

            var newPosition = new Position(_newPosition);
            //newPosition.PositionZ += 0.005f;
            newPosition.PositionZ += 0.005f * (__instance.ObjScale ?? 1.0f);

            if (_newPosition.Instance != __instance.Location.Instance)
            {
                if (!__instance.OnTransitionToNewRealm(__instance.Location.RealmID, _newPosition.RealmID, newPosition))
                    return false;
            }

            //Console.WriteLine($"{Name}.Teleport() - Sending to {newPosition.ToLOCString()}");

            // Check currentFogColor set for player. If LandblockManager.GlobalFogColor is set, don't bother checking, dungeons didn't clear like this on retail worlds.
            // if not clear, reset to clear before portaling in case portaling to dungeon (no current way to fast check unloaded landblock for IsDungeon or current FogColor)
            // client doesn't respond to any change inside dungeons, and only queues for change if in dungeon, executing change upon next teleport
            // so if we delay teleport long enough to ensure clear arrives before teleport, we don't get fog carrying over into dungeon.

            var player = __instance;

            if (__instance.currentFogColor.HasValue && __instance.currentFogColor != EnvironChangeType.Clear && !LandblockManager.GlobalFogColor.HasValue)
            {
                var delayTelport = new ActionChain();
                delayTelport.AddAction(player, () => player.ClearFogColor());
                delayTelport.AddDelaySeconds(1);
                delayTelport.AddAction(player, () => WorldManager.ThreadSafeTeleport(player, _newPosition, teleportingFromInstance));

                delayTelport.EnqueueChain();

                return false;
            }

            __instance.Teleporting = true;
            __instance.LastTeleportTime = DateTime.UtcNow;
            __instance.LastTeleportStartTimestamp = Time.GetUnixTime();

            if (fromPortal)
                __instance.LastPortalTeleportTimestamp = __instance.LastTeleportStartTimestamp;

            __instance.Session.Network.EnqueueSend(new GameMessagePlayerTeleport(__instance));

            // load quickly, but player can load into landblock before server is finished loading

            // send a "fake" update position to get the client to start loading asap,
            // also might fix some decal bugs
            var prevLoc = __instance.Location;
            __instance.Location = newPosition;
            __instance.SendUpdatePosition();
            __instance.Location = prevLoc;

            __instance.DoTeleportPhysicsStateChanges();

            // force out of hotspots
            __instance.PhysicsObj.report_collision_end(true);

            if (__instance.UnderLifestoneProtection)
                __instance.LifestoneProtectionDispel();

            __instance.HandlePreTeleportVisibility(newPosition);

            __instance.UpdatePlayerPosition(new Position(newPosition), true);

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), "OnTransitionToNewRealm", new Type[] { typeof(ushort), typeof(ushort), typeof(Position) })]
        public static bool PreOnTransitionToNewRealm(ushort prevRealmId, ushort newRealmId, Position newLocation, ref Player __instance, ref bool __result)
        {
            var prevrealm = RealmManager.GetRealm(prevRealmId);
            var newRealm = RealmManager.GetRealm(newRealmId);

            if (newLocation.IsEphemeralRealm && !__instance.Location.IsEphemeralRealm)
            {
                __instance.SetPosition(PositionType.EphemeralRealmExitTo, new Position(__instance.Location));
                __instance.SetPosition(PositionType.EphemeralRealmLastEnteredDrop, new Position(newLocation));
            }
            else if (!newLocation.IsEphemeralRealm)
            {
                __instance.SetPosition(PositionType.EphemeralRealmExitTo, null);
                __instance.SetPosition(PositionType.EphemeralRealmLastEnteredDrop, null);
            }

            var pk = false;
            if (newLocation.IsEphemeralRealm)
            {
                var lb = LandblockManager.GetLandblockUnsafe(newLocation.LandblockId, newLocation.Instance);
                if (lb.RealmHelpers.IsDuel || lb.RealmHelpers.IsPkOnly)
                    pk = true;
            }

            if (newRealm.StandardRules.GetProperty(RealmPropertyBool.IsPKOnly))
                pk = true;

            __instance.PlayerKillerStatus = pk ? PlayerKillerStatus.PK : PlayerKillerStatus.NPK;
            __instance.EnqueueBroadcast(new GameMessagePublicUpdatePropertyInt(__instance, PropertyInt.PlayerKillerStatus, (int)__instance.PlayerKillerStatus));

            if (newLocation.IsEphemeralRealm)
                __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Entering ephemeral instance. Type /realm-info to view realm properties.", ChatMessageType.System));
            else if (__instance.Location.IsEphemeralRealm && !newLocation.IsEphemeralRealm)
                __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Leaving instance and returning to realm {newRealm.Realm.Name}.", ChatMessageType.System));
            else
            {
                if (prevrealm.Realm.Id != __instance.HomeRealm)
                    __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"You are temporarily leaving your home realm. Some actions may be restricted and your corpse will appear at your hideout if you die.", ChatMessageType.System));
                else if (newRealm.Realm.Id == __instance.HomeRealm)
                    __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Returning to home realm.", ChatMessageType.System));
                else
                    __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Switching from realm {prevrealm.Realm.Name} to {newRealm.Realm.Name}.", ChatMessageType.System));
            }
            __result = true;
            return false;
        }



        #endregion
    }

}
