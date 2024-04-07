﻿using ACE.Adapter.Enum;
using ACE.Database;
using ACE.Database.Models.Auth;
using ACE.Entity;
using ACE.Entity.Enum.Properties;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Factories;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.Physics;
using ACE.Server.Realms;
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

            for(var i = 0; i <= 6; i++)
            {
                DatabaseManager.World.GetDungeonCreatureWeenieIds((uint)i);
            }
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
        [HarmonyPatch(typeof(HouseManager), nameof(HouseManager.Tick))]
        public static bool PreTick()
        {
            if (HouseManager.updateHouseManagerRateLimiter.GetSecondsToWaitBeforeNextEvent() > 0)
                return false;

            HouseManager.updateHouseManagerRateLimiter.RegisterEvent();

            //log.Info($"HouseManager.Tick({RentQueue.Count})");

            var nextEntry = HouseManager.RentQueue.FirstOrDefault();

            if (nextEntry == null)
                return false;

            var currentTime = DateTime.UtcNow;

            while (currentTime > nextEntry.RentDue)
            {
                HouseManager.RentQueue.Remove(nextEntry);

                HouseManager.ProcessRent(nextEntry);

                nextEntry = HouseManager.RentQueue.FirstOrDefault();

                if (nextEntry == null)
                    return false;

                currentTime = DateTime.UtcNow;
            }

            DungeonManager.Tick();

            RiftManager.Tick();

            return false;
        }


        [HarmonyPrefix]
        [HarmonyPatch(typeof(Creature), nameof(Creature.OnDeath_GrantXP))]
        public static bool PreOnDeath_GrantXP(ref Creature __instance)
        {
            try
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

                    if (__instance.CurrentLandblock != null && __instance.XpOverride != null)
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
            } catch (Exception ex) 
            {
                ModManager.Log(ex.Message, ModManager.LogLevel.Error);
                return false;
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
                {
                    if (newRealm == RealmManager.ServerBaseRealm && prevrealm.GetDefaultInstanceID(__instance) == __instance.Account.AccountId)
                        __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"You have chosen {newRealm.Realm.Name} as your home realm.", ChatMessageType.System));
                    else
                        __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"You are temporarily leaving your home realm.", ChatMessageType.System));
                }
                else if (newRealm.Realm.Id == __instance.HomeRealm)
                    __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Returning to home realm.", ChatMessageType.System));
                else
                    __instance.Session.Network.EnqueueSend(new GameMessageSystemChat($"Switching from realm {prevrealm.Realm.Name} to {newRealm.Realm.Name}.", ChatMessageType.System));
            }
            __result = true;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WorldObject), "HandleCastSpell_PortalRecall", new Type[] { typeof(Spell), typeof(Creature) })]
        public static bool PreHandleCastSpell_PortalRecall(Spell spell, Creature targetCreature, ref WorldObject __instance)
        {
            var player = __instance as Player;

            if (player != null && player.IsOlthoiPlayer)
            {
                player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.OlthoiCanOnlyRecallToLifestone));
                return false;
            }

            if (targetCreature is Player)
            {
                var recallsDisabled = !targetCreature.RealmRuleset.GetProperty(RealmPropertyBool.HasRecalls);
                if (recallsDisabled)
                    return false;
            }

            var creature = __instance as Creature;

            var targetPlayer = targetCreature as Player;

            if (player != null && player.PKTimerActive)
            {
                player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouHaveBeenInPKBattleTooRecently));
                return false;
            }

            PositionType recall = PositionType.Undef;
            uint? recallDID = null;

            // verify pre-requirements for recalls

            switch ((SpellId)spell.Id)
            {
                case SpellId.PortalRecall:       // portal recall

                    if (targetPlayer.LastPortalDID == null)
                    {
                        // You must link to a portal to recall it!
                        targetPlayer.Session.Network.EnqueueSend(new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToPortalToRecall));
                    }
                    else
                    {
                        recall = PositionType.LastPortal;
                        recallDID = targetPlayer.LastPortalDID;
                    }
                    break;

                case SpellId.LifestoneRecall1:   // lifestone recall

                    if (targetPlayer.GetPosition(PositionType.LinkedLifestone) == null)
                    {
                        // You must link to a lifestone to recall it!
                        targetPlayer.Session.Network.EnqueueSend(new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToLifestoneToRecall));
                    }
                    else
                        recall = PositionType.LinkedLifestone;
                    break;

                case SpellId.LifestoneSending1:

                    if (player != null && player.GetPosition(PositionType.Sanctuary) != null)
                        recall = PositionType.Sanctuary;
                    else if (targetPlayer != null && targetPlayer.GetPosition(PositionType.Sanctuary) != null)
                        recall = PositionType.Sanctuary;

                    break;

                case SpellId.PortalTieRecall1:   // primary portal tie recall

                    if (targetPlayer.LinkedPortalOneDID == null)
                    {
                        // You must link to a portal to recall it!
                        targetPlayer.Session.Network.EnqueueSend(new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToPortalToRecall));
                    }
                    else
                    {
                        recall = PositionType.LinkedPortalOne;
                        recallDID = targetPlayer.LinkedPortalOneDID;
                    }
                    break;

                case SpellId.PortalTieRecall2:   // secondary portal tie recall

                    if (targetPlayer.LinkedPortalTwoDID == null)
                    {
                        // You must link to a portal to recall it!
                        targetPlayer.Session.Network.EnqueueSend(new GameEventWeenieError(targetPlayer.Session, WeenieError.YouMustLinkToPortalToRecall));
                    }
                    else
                    {
                        recall = PositionType.LinkedPortalTwo;
                        recallDID = targetPlayer.LinkedPortalTwoDID;
                    }
                    break;
            }

            if (recall != PositionType.Undef)
            {
                var playerLbRaw = targetPlayer.Location.LandblockId.Raw;
                var playerLb = $"{playerLbRaw:X8}".Substring(0, 4);

                RiftManager.TryGetActiveRift(playerLb, out Rift activeRift);
                var insideRift = activeRift != null;

                if (recallDID == null)
                {
                    // lifestone recall

                    if (insideRift)
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You cannot cast lifestone recall inside a rift.", ChatMessageType.System));
                        return false;
                    }

                    ActionChain lifestoneRecall = new ActionChain();
                    lifestoneRecall.AddAction(targetPlayer, () => targetPlayer.DoPreTeleportHide());
                    lifestoneRecall.AddDelaySeconds(2.0f);  // 2 second delay
                    lifestoneRecall.AddAction(targetPlayer, () => targetPlayer.TeleToPosition(recall));
                    lifestoneRecall.EnqueueChain();
                }
                else
                {
                    // portal recall

                    if (insideRift)
                    {
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat($"You cannot cast portal recall inside a rift.", ChatMessageType.System));
                        return false;
                    }

                    var portal = WorldObject.GetPortal(recallDID.Value);

                    if (portal.WeenieClassId == 600004)
                    {
                        // You cannot recall that portal!
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat($"Active Rifts cannot be recalled to.", ChatMessageType.System));
                        return false;
                    }

                    var newLbRaw = portal.Destination.LandblockId.Raw;
                    var nextLb = $"{newLbRaw:X8}".Substring(0, 4);

                    if (RiftManager.TryGetActiveRift(nextLb, out Rift _))
                    {
                        // You cannot recall that portal!
                        player.Session.Network.EnqueueSend(new GameMessageSystemChat($"Active Rifts cannot be recalled to.", ChatMessageType.System));
                        return false;
                    }

                    if (portal == null || portal.NoRecall)
                    {
                        // You cannot recall that portal!
                        player.Session.Network.EnqueueSend(new GameEventWeenieError(player.Session, WeenieError.YouCannotRecallPortal));
                        return false;
                    }

                    var result = portal.CheckUseRequirements(targetPlayer);
                    if (!result.Success)
                    {
                        if (result.Message != null)
                            targetPlayer.Session.Network.EnqueueSend(result.Message);

                        return false;
                    }

                    ActionChain portalRecall = new ActionChain();
                    portalRecall.AddAction(targetPlayer, () => targetPlayer.DoPreTeleportHide());
                    portalRecall.AddDelaySeconds(2.0f);  // 2 second delay
                    portalRecall.AddAction(targetPlayer, () =>
                    {
                        var teleportDest = new Position(portal.Destination);
                        WorldObject.AdjustDungeon(teleportDest, teleportDest.Instance);

                        targetPlayer.Teleport(teleportDest);
                    });
                    portalRecall.EnqueueChain();
                }
            }

            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Portal), nameof(Portal.CheckUseRequirements), new Type[] { typeof(WorldObject) })]
        public static bool PreCheckUseRequirements(WorldObject activator, ref Portal __instance, ref ActivationResult __result)
        {
            if (!(activator is Player player))
            {
                __result = new ActivationResult(false);
                return false;
            }

            if (__instance.Name == "Gateway")
            {

                var newLbRaw = __instance.Destination.LandblockId.Raw;
                var nextLb = $"{newLbRaw:X8}".Substring(0, 4);

                if (RiftManager.TryGetActiveRift(nextLb, out Rift _))
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat($"A summoned portal that leads to an Active Rift is not allowed.", ChatMessageType.System));
                    __result = new ActivationResult(false);
                    return false;
                }

                var currentLbRaw = __instance.Location.LandblockId.Raw;
                var currentLb = $"{newLbRaw:X8}".Substring(0, 4);

                if (RiftManager.TryGetActiveRift(nextLb, out Rift _))
                {
                    player.Session.Network.EnqueueSend(new GameMessageSystemChat($"A summoned portal inside an Active Rift is not allowed.", ChatMessageType.System));
                    __result = new ActivationResult(false);
                    return false;
                }

            }

            return true;
        }

        public static uint GetMonsterTierByLevel(uint level)
        {
            uint tier = 0;

            if (level <= 300)
                tier = 6;
            if (level <= 220)
                tier = 5;
            if (level <= 150)
                tier = 4;
            if (level <= 115)
                tier = 3;
            if (level <= 100)
                tier = 2;
            if (level <= 50)
                tier = 1;
            if (level <= 20)
                tier = 0;

            return tier;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MutationsManager), nameof(MutationsManager.ProcessWorldObject), new Type[] { typeof(WorldObject), typeof(AppliedRuleset), typeof(bool) })]
        public static bool PreProcessWorldObject(WorldObject wo, AppliedRuleset ruleset, bool replace, ref WorldObject __result)
        {
            if (ruleset.Realm.Id == 1016 && wo.WeenieType == WeenieType.Generic)
            {
                var creatureRespawnDuration = ruleset.GetProperty(RealmPropertyFloat.CreatureRespawnDuration);
                var creatureSpawnRateMultiplier = ruleset.GetProperty(RealmPropertyFloat.CreatureSpawnRateMultiplier);

                if (creatureRespawnDuration > 0)
                {
                    wo.RegenerationInterval = (int)((float)creatureRespawnDuration * creatureSpawnRateMultiplier);

                    wo.ReinitializeHeartbeats();

                    if (wo.Biota.PropertiesGenerator != null)
                    {
                        // While this may be ugly, it's done for performance reasons.
                        // Common weenie properties are not cloned into the bota on creation. Instead, the biota references simply point to the weenie collections.
                        // The problem here is that we want to update one of those common collection properties. If the biota is referencing the weenie collection,
                        // then we'll end up updating the global weenie (from the cache), instead of just this specific biota.
                        if (wo.Biota.PropertiesGenerator == wo.Weenie.PropertiesGenerator)
                        {
                            wo.Biota.PropertiesGenerator = new List<ACE.Entity.Models.PropertiesGenerator>(wo.Weenie.PropertiesGenerator.Count);

                            foreach (var record in wo.Weenie.PropertiesGenerator)
                                wo.Biota.PropertiesGenerator.Add(record.Clone());
                        }
                    }
                }

                __result = wo;
                return false;
            }

            if (ruleset.Realm.Id == 1016 && wo.WeenieType == ACE.Entity.Enum.WeenieType.Creature && wo.Attackable && !wo.IsGenerator)
            {
                var lbRaw = wo.Location.LandblockId.Raw;
                var lb = $"{lbRaw:X8}".Substring(0, 4);

                if (RiftManager.TryGetActiveRift(lb, out Rift activeRift)) 
                {
                    var creator = activeRift.Creator;
                    if (creator == null)
                        return true;

                    var randomMob = ThreadSafeRandom.Next(1, 100);

                    if (randomMob <= 25)
                    {
                        var tier = GetMonsterTierByLevel((uint)creator.Level);
                        var monsters = DatabaseManager.World.GetDungeonCreatureWeenieIds((uint)tier);
                        var random = ThreadSafeRandom.Next(0, monsters.Count - 1);
                        var wcid = monsters[random];
                        var creature = WorldObjectFactory.CreateNewWorldObject(wcid);
                        creature.Location = new Position(wo.Location);
                        creature.Name = $"Rift {creature.Name}";
                        wo.Destroy();
                        __result = creature;
                        return false;
                    }
                    else
                        return true;

                }

                return true;

            }

            return true;
        }

        [CommandHandler("active-rifts", AccessLevel.Player, CommandHandlerFlag.None, 0, "Get a list of available rifts.")]
        public static void HandleCheckRifts(Session session, params string[] paramters)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat($"\n<Active Rift List>", ChatMessageType.System));
            foreach (var rift in RiftManager.ActiveRifts.Values.ToList())
            {
                var at = rift.Coords.Length > 0 ? $"at {rift.Coords}" : "";
                var message = $"Rift {rift.Name} is active {at}";
                session.Network.EnqueueSend(new GameMessageSystemChat($"\n{message}", ChatMessageType.System));
            }
            session.Network.EnqueueSend(new GameMessageSystemChat($"\nTime Remaining: {RiftManager.FormatTimeRemaining()}", ChatMessageType.System));
        }

        [CommandHandler("hot-dungeons", AccessLevel.Player, CommandHandlerFlag.None, 0, "Get a list of available rifts.")]
        public static void HandleCheckDungeons(Session session, params string[] paramters)
        {
            session.Network.EnqueueSend(new GameMessageSystemChat($"\n<Active Dungeon List>", ChatMessageType.System));
            var dungeon = DungeonManager.CurrentHotSpot;
            if (dungeon != null)
            {
                var at = dungeon.Coords.Length > 0 ? $"at {dungeon.Coords}" : "";
                var message = $"Rift {dungeon.Name} is active {at}";
                session.Network.EnqueueSend(new GameMessageSystemChat($"\n{message}", ChatMessageType.System));
                session.Network.EnqueueSend(new GameMessageSystemChat($"\nTime Remaining: {DungeonManager.FormatTimeRemaining()}", ChatMessageType.System));

            } else
                session.Network.EnqueueSend(new GameMessageSystemChat($"\nNo Hotspots at this time", ChatMessageType.System));

        }


        #endregion
    }

}
