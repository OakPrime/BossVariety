using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossVariety
{
    //This is an example plugin that can be put in BepInEx/plugins/ExamplePlugin/ExamplePlugin.dll to test out.
    //It's a small plugin that adds a relatively simple item to the game, and gives you that item whenever you press F2.

    //This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    //This is the main declaration of our plugin class. BepInEx searches for all classes inheriting from BaseUnityPlugin to initialize on startup.
    //BaseUnityPlugin itself inherits from MonoBehaviour, so you can use this as a reference for what you can declare and use in your plugin class: https://docs.unity3d.com/ScriptReference/MonoBehaviour.html
    public class RespawnForBoss : BaseUnityPlugin
    {
        //The Plugin GUID should be a unique ID for this plugin, which is human readable (as it is used in places like the config).
        //If we see this PluginGUID as it is on thunderstore, we will deprecate this mod. Change the PluginAuthor and the PluginName !
        public const string PluginGUID = PluginAuthor + "." + "BossVariety";
        public const string PluginAuthor = "OakPrime";
        public const string PluginName = "BossVariety";
        public const string PluginVersion = "1.0.2";

        public static ManualLogSource logger;

        private Queue<GameObject> _prevBosses = new Queue<GameObject>();
        private int MAX_QUEUE_COUNT = 2;

        //The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            try
            {

                //RFBConfig.InitializeConfig();
                logger = Logger;
                Log.Init(logger);

                var configFile = new ConfigFile(Paths.ConfigPath + "\\" + PluginGUID + ".cfg", true);
                var maxQueueConfig = configFile.Bind("Main", "Boss Ban Pool Size", 2, "Amount of previous bosses that will be excluded from current stage's boss pool. 1 = No repeats " +
                    "across 2 stages, 2 = No repeats across 3 stages, etc. At values above 2, you risk banning the stage's entire boss pool, in which case the mod will allow repeats" +
                    " that stage");
                maxQueueConfig.Value = Math.Clamp(maxQueueConfig.Value, 0, 5);
                MAX_QUEUE_COUNT = maxQueueConfig.Value;

                IL.RoR2.CombatDirector.SetNextSpawnAsBoss += (il) =>
                {
                    ILCursor c = new ILCursor(il);
                    if (!c.TryGotoNext(
                        x => x.MatchLdcI4(0),
                        x => x.MatchBle(out _),
                        x => x.MatchLdloc(out _),
                        x => x.MatchLdarg(out _)
                    ))
                    {
                        Log.LogInstrNotFound(il);
                        return;
                    }
                    c.Index += 3;
                    c.EmitDelegate<Func<WeightedSelection<DirectorCard>, WeightedSelection<DirectorCard>>>(weightedSelection =>
                    {
                        // skip logic if _prevBosses is empty (first stage)
                        if (_prevBosses.Count <= 0)
                        {
                            return weightedSelection;
                        }

                        // ensure weightedSelection has a value outside of _prevBosses
                        var oldBossIndices = new List<int>();
                        var newBossAvailable = false;
                        //Log.LogDebug("Looping over banned bosses...");
                        foreach (GameObject prefab in _prevBosses)
                        {
                            Log.LogDebug("Repeat boss is banned this stage: " + prefab.name);
                        }
                        //Log.LogDebug("Starting iteration over weightedSelection of size: " + weightedSelection.Count);
                        for (int i = 0; i < weightedSelection.Count; ++i)
                        {
                            WeightedSelection<DirectorCard>.ChoiceInfo choiceInfo = weightedSelection.GetChoice(i);
                            //Log.LogDebug("Stage potential boss spawn: " + choiceInfo.value.spawnCard.prefab.name);
                            if (_prevBosses.Contains(choiceInfo.value.spawnCard.prefab))
                            {
                                //Log.LogDebug("Added potential boss spawn to toBan indices");
                                oldBossIndices.Add(i);
                            }
                            else
                            {
                                //Log.LogDebug("Potential boss spawn will remain");
                                newBossAvailable = true;
                            }
                        }
                        
                        // Loop through weightedSelection and remove _prevBosses cards
                        if (newBossAvailable)
                        {
                            //Log.LogDebug("Starting iteration over oldBossIndices of size: " + oldBossIndices.Count);
                            for (int i = oldBossIndices.Count - 1; i >= 0; --i)
                            {
                                // Spawncard name was working well as a log before
                                var oldBossIndex = oldBossIndices[i];
                                Log.LogDebug("Stopping repeat boss from spawning: " + weightedSelection.GetChoice(oldBossIndex).value.spawnCard.prefab.name);
                                weightedSelection.RemoveChoice(oldBossIndex);
                            }
                        }
                        else
                        {
                            Log.LogDebug("Only repeat bosses are available this stage.");
                        }

                            return weightedSelection;
                    });

                    if (!c.TryGotoNext(
                        x => x.MatchCallOrCallvirt(out _),
                        x => x.MatchCallOrCallvirt(out _),
                        x => x.MatchStloc(out _)
                    ))
                    {
                        Log.LogInstrNotFound(il);
                        return;
                    }
                    c.Index += 2;
                    c.EmitDelegate<Func<DirectorCard, DirectorCard>>(directorCard =>
                    {
                        Log.LogDebug("Adding boss to ban pool: " + directorCard.spawnCard.prefab.name);
                        _prevBosses.Enqueue(directorCard.spawnCard.prefab);
                        while (_prevBosses.Count > MAX_QUEUE_COUNT)
                        {
                            Log.LogDebug("Removing boss from ban pool: " + _prevBosses.Dequeue().name);
                        }
                        return directorCard;
                    });
                };

            }
            catch (Exception e)
            {
                Logger.LogError(e.Message + " - " + e.StackTrace);
            }
        }

        
    }
}
