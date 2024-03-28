using ACE.Entity;
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
        [HarmonyPatch(typeof(WorldManager), nameof(WorldManager.Open), new Type[] { typeof(Player) })]
        public static bool PreOpen(Player player)
        {
            DungeonManager.Initialize();
            //Return false to override
            //return false;

            //Return true to execute original
            return true;
        }

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
        #endregion
    }

}
