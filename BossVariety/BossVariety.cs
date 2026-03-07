using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using MonoMod.Cil;
using RoR2;
using System;
using System.Collections.Generic;

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
        public const string PluginVersion = "1.0.0";

        public static ManualLogSource logger;

        private Queue<DirectorCard> _prevBosses = new Queue<DirectorCard>();
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
                        var oldBossIndices = new HashSet<int>();
                        var newBossAvailable = false;
                        //Log.LogDebug("Starting iteration over weightedSelection of size: " + weightedSelection.Count);
                        for (int i = 0; i < weightedSelection.Count; ++i)
                        {
                            WeightedSelection<DirectorCard>.ChoiceInfo choiceInfo = weightedSelection.GetChoice(i);
                            if (_prevBosses.Contains(choiceInfo.value))
                            {
                                oldBossIndices.Add(i);
                            }
                            else
                            {
                                newBossAvailable = true;
                            }
                        }
                        
                        // Loop through weightedSelection and remove _prevBosses cards
                        if (newBossAvailable)
                        {
                            Log.LogDebug("Starting iteration over oldBossIndices of size: " + oldBossIndices.Count);
                            foreach (int index in oldBossIndices)
                            {
                                Log.LogDebug("Removing choice: " + weightedSelection.GetChoice(index).value.spawnCard);
                                weightedSelection.RemoveChoice(index);
                            }
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
                        Log.LogDebug("Adding boss to ban pool: " + directorCard.spawnCard.name);
                        _prevBosses.Enqueue(directorCard);
                        while (_prevBosses.Count > MAX_QUEUE_COUNT)
                        {
                            Log.LogDebug("Removing boss from ban pool: " + _prevBosses.Dequeue().spawnCard.name);
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
