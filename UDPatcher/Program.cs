using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Environments;
using CommandLine;
using Noggog;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using DynamicData.Kernel;

namespace UDPatcher
{
    public class Program
    {
        public static Lazy<UDPatchSettings> _settings = null!;
        public static UDPatchSettings Settings => _settings.Value;

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "UD_Patch.esp")
                .SetAutogeneratedSettings(
                            nickname: "Settings",
                            path: "settings.json",
                            out _settings,
                            true)
                .Run(args);
        }

        private static string? GetUdScriptNameFromZad(string zadName)
        {
            foreach (var zadGroup in Settings.ScriptMatches)
            {
                if (zadGroup.Value.Contains(zadName))
                {
                    return zadGroup.Key;
                }
            }
            return null;
        }

        private static List<UDOtherSettings> GetOtherRulesFromUd(string udName)
        {
            return Settings.OtherMatches.FindAll(rule => rule.InputScripts.Contains(udName));
        }

        private static string? GetUdScriptNameFromKws(List<UDKwSettings> kwRules, IEnumerable<string> inputScripts, 
            IEnumerable<IFormLinkGetter<IKeywordGetter>>? armorKeywords)
        {
            string? newName = null;
            foreach (var kwRule in kwRules) { 
                try
                {
                    newName = GetUdScriptNameFromKw(kwRule, armorKeywords);
                } catch (Exception ex)
                {
                    throw new Exception($"Failed on KW rule with output {kwRule.OutputScript}", ex);
                }
                if (newName != null && !inputScripts.Contains(newName))
                {
                    return newName;
                }
            }
            return newName;
        }

        private static string? GetUdScriptNameFromKw(UDKwSettings kwRule, IEnumerable<IFormLinkGetter<IKeywordGetter>>? armorKeywords)
        {
            if (kwRule.OutputScript == null)
            {
                throw new Exception("Output Script of Keyword Match not defined");
            } else if (armorKeywords == null) {
                return null;
            }
            else if (kwRule.Keywords.Intersect(armorKeywords).Any())
            {
                return kwRule.OutputScript;
            } else
            {
                return null;
            }
        }

        private static string? GetUdScriptNameFromSearchRule(UDNameSearchSettings nameRule, string armorName)
        {
            if (armorName.Contains(nameRule.SearchText))
            {
                return nameRule.OutputScript;
            }
            else
            {
                return null;
            }
        }

        private static string? GetUdScriptNameFromSearchRules(IEnumerable<UDNameSearchSettings> nameRules, IEnumerable<string> inputScripts, string armorName)
        {
            string? newUdName = null;
            foreach (var rule in nameRules)
            {
                try
                {
                    newUdName = GetUdScriptNameFromSearchRule(rule, armorName);
                } catch (Exception ex)
                {
                    throw new Exception($"Failed on word '{rule.SearchText}'", ex);
                }
                if (newUdName != null && !inputScripts.Contains(newUdName))
                {
                    return newUdName;
                }
            }
            return newUdName;
        }

        private static string? GetUdScriptNameFromOtherRule(UDOtherSettings otherRule, IArmorGetter armor)
        {
            var inputScripts = otherRule.InputScripts;
            if (inputScripts == null || !inputScripts.Any())
            {
                throw new Exception("No Input Scripts found");
            }
            string? newUdName;
            try
            {
                newUdName = GetUdScriptNameFromKws(otherRule.KeywordMatch, inputScripts, armor.Keywords);
            }
            catch (Exception e)
            {
                throw new Exception("Failed to match Keywords", e);
            }
            if (armor.Name == null || armor.Name.String == null || (newUdName != null && !inputScripts.Contains(newUdName)))
            {
                return newUdName;
            }
            try
            {
                return GetUdScriptNameFromSearchRules(otherRule.NameMatch, inputScripts, armor.Name.String);
            } catch (Exception e)
            {
                throw new Exception("Failed to match Search rule", e);
            }
        }

        private static string GetUdScriptNameFromOtherRules(string udName, IArmorGetter armor)
        {
            List<UDOtherSettings> otherRules = GetOtherRulesFromUd(udName);
            string? newUdName;
            foreach (var otherRule in otherRules) {
                try
                {
                    newUdName = GetUdScriptNameFromOtherRule(otherRule, armor);
                } catch(Exception e)
                {
                    throw new Exception("Failed to apply Other Rules", e);
                }
                if (newUdName != null && newUdName != udName)
                {
                    return newUdName;
                }
            }
            return udName;
        }

        public static string? GetUdScriptNameFromArmor(IArmorGetter armor, string zadName)
        {
            var udName = GetUdScriptNameFromZad(zadName);
            if (udName == null)
            {
                Console.WriteLine($"Could not find direct UD match for script {zadName} of " +
                    $"Armor {armor}");
                return null;
            }
            var loopedNames = new HashSet<string>() { udName };
            string newUdName = udName;
            string prevNewUdName = string.Empty;
            while (prevNewUdName != newUdName)
            {
                prevNewUdName = newUdName;
                newUdName = GetUdScriptNameFromOtherRules(udName, armor);
                if (prevNewUdName != newUdName && loopedNames.Contains(newUdName))
                {
                    throw new Exception($"Found looping rule for Armor {armor} (from Script " +
                        $"{prevNewUdName} to Script {newUdName}");
                }
                loopedNames.Add(newUdName);
            }
            return newUdName;
        }

       /* public static IScriptEntryGetter? FindArmorScript(IEnumerable<IScriptEntryGetter> armorScripts, 
            IDictionary<string, IScriptEntryGetter> searchScripts)
        {
            
            foreach (var armorScript in armorScripts) {
                if (searchScripts.TryGetValue(armorScript.Name, out var outScript))
                {
                    return outScript;
                }
            }
            return null;
        }*/

        public static IScriptEntryGetter? FindArmorScript(IEnumerable<IScriptEntryGetter> armorScripts,
            IEnumerable<string> searchScripts)
        {
            foreach (var script in armorScripts)
            {
                if (searchScripts.Contains(script.Name)) return script;
            }
            return null;
        }

        public static HashSet<string> GetAllUdScriptNamesFromSettings()
        {
            var allNames = new HashSet<string>(Settings.ScriptMatches.Keys);
            foreach(var otherRule in Settings.OtherMatches)
            {
                allNames.UnionWith(otherRule.InputScripts);
            }
            return allNames;
        }

        public static HashSet<string> GetZadNamesFromRules(IEnumerable<UDOtherSetting> otherSettings)
        {
            return otherSettings.Select(setting => setting.OutputScript).ToHashSet();
        }

        //public static List<UDOtherSetting> ConvertSettingList(List<U>)

        public static HashSet<string> GetAllZadScriptNamesFromSettings()
        {
            var allNames = new HashSet<string>();
            foreach(var zadMatches in Settings.ScriptMatches.Values)
            {
                allNames.UnionWith(zadMatches);
            }
            foreach(var otherRule in Settings.OtherMatches)
            {
                /*var gameg = new List<IEnumerable<UDOtherSetting>>();
                gameg.Add(otherRule.KeywordMatch.Select(match => match.Cast<UDOtherSetting>()));
                foreach (var matcher in new List<List<UDOtherSetting>>() { otherRule.KeywordMatch, otherRule.NameMatch } )
                {

                }
                //allNames.Add(otherRule.KeywordMatch.OutputScript);
                allNames.UnionWith(otherRule.NameMatch.Select(rule => rule.OutputScript));*/
                //List<List<UDOtherSetting>> stuff = new(){ otherRule.KeywordMatch, otherRule.NameMatch };
                var kwMatches = otherRule.KeywordMatch.Select(match => (UDOtherSetting)match);
                var nameMatches = otherRule.NameMatch.Select(match => (UDOtherSetting)match);
                allNames.UnionWith(GetZadNamesFromRules(kwMatches));
                allNames.UnionWith(GetZadNamesFromRules(nameMatches));
            }
            return allNames;
        }

        public static ScriptEntry CopyInvScriptToRender(IScriptEntryGetter original)
        {
            var VALID_PROP_NAMES = new HashSet<string>() { "deviceInventory", "libs", "zad_DeviousDevice" };
            var REPLACEMENT_PROP_NAMES = new Dictionary<string, string>() {
            { "zad_DeviousDevice", "UD_DeviceKeyword"}};
            var newScript = original.DeepCopy();
            newScript.Properties.RemoveWhere(prop => !VALID_PROP_NAMES.Contains(prop.Name));
            foreach (var prop in newScript.Properties)
            {
                if (REPLACEMENT_PROP_NAMES.TryGetValue(prop.Name, out var newName))
                {
                    prop.Name = newName;
                }
            }
            /*var toRemove = new List<int>();
            for(int i = 0; i < props.Count; i++)
            {
                if (!VALID_PROP_NAMES.Contains(props[i].Name))
                {
                    toRemove.Add(i);
                }
            }*/
            return newScript;
            //props = props.IntersectBy(VALID_PROP_NAMES, (prop => prop.Name)).ToExtendedList();
            /*foreach (var index in toRemove)
            {
                props.
            }*/
        }

        public static T DumbRecordGetter<T>(ILinkCache linkCache, ModKey mod, uint formId)
        {
            return linkCache.Resolve(new FormKey(mod, formId), typeof(T)).Cast<T>();
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            /*var UDScripts = new Dictionary<string, IScriptEntryGetter>();
            var zadScripts = new Dictionary<string, IScriptEntryGetter>();*/
            var UDScripts = GetAllUdScriptNamesFromSettings();
            var zadScripts = GetAllZadScriptNamesFromSettings();

            //const bool USE_MODES = Settings.Value.UseModes;

            const string DDI_NAME = "Devious Devices - Integration.esm";
            ModKey ddiMod = ModKey.FromFileName(DDI_NAME);

            const int ZADINVKW_ID = 0x02b5f0;

            const string UD_NAME = "UnforgivingDevices.esp";
            ModKey udMod = ModKey.FromFileName(UD_NAME);
            //ISkyrimModGetter udModGetter = Mod

            const int UDINVKW_ID = 0x1553dd;
            const int UDPATCHKW_ID = 0x13A977;
            const int UDKW_ID = 0x11a352;
            const int UDPATCHNOMODEKW_ID = 0x1579be;

            const int UDCDMAINQST_ID = 0x15e73c;

            //IEnumerable<IModListing<ISkyrimModGetter>> masters = new List<IModListing<ISkyrimModGetter>>() {new ModListing<ISkyrimModGetter>(ddiMod, true, true)};
            var shortenedLoadOrder = state.LoadOrder.PriorityOrder.Where(
                mod =>
                Settings.ModsToPatch.Contains(mod.ModKey)
                //Settings.Value.ModToPatch == mod.ModKey
                );
            var shortenedLoadOrderFuller = state.LoadOrder.PriorityOrder.Where(mod =>
                Settings.ModsToPatch.Contains(mod.ModKey) || mod.ModKey == ddiMod || mod.ModKey == udMod
                );
            var idLinkCache = shortenedLoadOrderFuller.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
            //var zadInvKeyword = GetZadInventoryKeyword(idLinkCache);
            IKeywordGetter zadInvKeyword = DumbRecordGetter<IKeywordGetter>(idLinkCache, ddiMod, ZADINVKW_ID);
                //idLinkCache.Resolve(new FormKey(ModKey.FromFileName(DDI_NAME), ZADINV_ID), typeof(IKeywordGetter)).Cast<IKeywordGetter>();
            IKeywordGetter udInvKeyword = DumbRecordGetter<IKeywordGetter>(idLinkCache, udMod, UDINVKW_ID);
            IKeywordGetter udPatchKw = DumbRecordGetter<IKeywordGetter>(idLinkCache, udMod, UDPATCHKW_ID);
            IKeywordGetter udKw = DumbRecordGetter<IKeywordGetter>(idLinkCache, udMod, UDKW_ID);
            IKeywordGetter udPatchNoModeKw = DumbRecordGetter<IKeywordGetter>(idLinkCache, udMod, UDPATCHNOMODEKW_ID);

            IQuestGetter udMainQst = DumbRecordGetter<IQuestGetter>(idLinkCache, udMod, UDCDMAINQST_ID);

            void addKeywords(Armor armor)
            {
                var keywords = new ExtendedList<IKeywordGetter>() { udKw, udPatchKw };
                if (Settings.UseModes)
                {
                    keywords.Add(udPatchNoModeKw);
                }
                if (armor.Keywords == null)
                {
                    armor.Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
                }
                foreach (var keyword in keywords)
                {
                    var kwLink = keyword.ToLinkGetter();
                    if (!armor.Keywords.Contains(kwLink))
                    {
                        armor.Keywords.Add(kwLink);
                    }
                }
            }
                //idLinkCache.Resolve(new FormKey(ModKey.FromFileName(UD_NAME), UDCDMAIN_ID), typeof(IQuestGetter)).Cast<IQuestGetter>();
            int totalPatched = 0;
            int newDevices = 0;
            foreach (var invArmorGetter in shortenedLoadOrder.Armor().WinningOverrides())
            {
                Console.WriteLine($"Doing Armor {invArmorGetter}");
                if (invArmorGetter.Keywords == null)
                {
                    Console.WriteLine($"{invArmorGetter} has no keywords");
                    continue;
                } else if (invArmorGetter.VirtualMachineAdapter == null || invArmorGetter.VirtualMachineAdapter.Scripts == null)
                {
                    Console.WriteLine($"{invArmorGetter}) has no scripts");
                    continue;
                }
                if (invArmorGetter.Keywords.Contains(zadInvKeyword))
                {
                    Console.WriteLine("Found zadInvKeyword");
                    // find the script the armour's using
                    var invCurrentScripts = invArmorGetter.VirtualMachineAdapter.Scripts;//.Select(script => script.Name);
                    var invUDScript = FindArmorScript(invCurrentScripts, UDScripts);
                    var invZadScript = FindArmorScript(invCurrentScripts, zadScripts);
                    /*if (invZadScript == null && invUDScript == null)
                    {
                        Console.WriteLine("penigs");
                        continue;
                    }*/
                    var invFinalScript = invZadScript != null ? invZadScript : invUDScript;
                    if (invFinalScript == null)
                    {
                        Console.WriteLine("penigs");
                        continue;
                    }
                    var renderDevice = invFinalScript
                        .Properties
                        .Where(prop => prop.Name == "deviceRendered")
                        .FirstOrDefault()!
                        .Cast<IScriptObjectPropertyGetter>()
                        .Object;
                    IArmorGetter renderArmor;
                    if (renderDevice.TryResolve<IArmorGetter>(idLinkCache, out var foundArmor))
                    {
                        renderArmor = foundArmor;
                    } else
                    {
                        //throw new Exception($"Invalid render target {renderDevice.FormKey} for inventory item {invArmorGetter.EditorID} ({invArmorGetter.FormKey})");
                        Console.WriteLine($"Invalid render target {renderDevice.FormKey} for inventory item {invArmorGetter.EditorID} ({invArmorGetter.FormKey})");
                        continue;
                    }
                    IScriptEntryGetter? renderUDScript = null;
                    if (renderArmor.VirtualMachineAdapter != null)
                    {
                        renderUDScript = FindArmorScript(renderArmor.VirtualMachineAdapter!.Scripts, UDScripts);
                    }
                    
                    /*if (renderUDScript == null && invUDScript == null)
                    {
                        Console.WriteLine("pegnis");
                        continue;
                    }*/
                    var renderArmorOverride = state.PatchMod.Armors.GetOrAddAsOverride(renderArmor);
                    if (renderArmorOverride == null)
                    {
                        Console.WriteLine("video peningns");
                        continue;
                    }
                    if (renderArmorOverride.Keywords == null)
                    {
                        renderArmorOverride.Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>();
                    }
                    if (renderArmorOverride.VirtualMachineAdapter == null)
                    {
                        renderArmorOverride.VirtualMachineAdapter = new VirtualMachineAdapter();
                    }
                    //renderArmorOverride.Keywords.Add(udInvKeyword);

                    //var newRenderScriptName = UDScripts[invZadScript!.Name].Name;
                    

                    if (invUDScript == null)
                    {
                        var invArmorOverride = state.PatchMod.Armors.GetOrAddAsOverride(invArmorGetter);
                        if (invArmorOverride.VirtualMachineAdapter == null)
                        {
                            throw new Exception("wtf???");
                        }
                        if (invArmorOverride.Keywords == null)
                        {
                            invArmorOverride.Keywords = new();
                        }
                        invArmorOverride.Keywords.Add(udInvKeyword);
                        var invScript = invArmorOverride.VirtualMachineAdapter.Scripts.Where(script => script.Name == invFinalScript.Name).Single();
                        //invScript.Name = "UD_CustomDevice_EquipScript";
                        
                        var UDCDProp = new ScriptObjectProperty();
                        UDCDProp.Name = "UDCDmain";
                        UDCDProp.Flags = ScriptProperty.Flag.Edited;
                        UDCDProp.Object = udMainQst.ToLink();
                        //invScript.Properties = new ExtendedList<ScriptProperty>(UDCDProp);
                        
                        invScript.Name = "UD_CustomDevice_EquipScript";
                        invScript.Properties.Add(UDCDProp);

                        var newRenderScriptName = GetUdScriptNameFromArmor(renderArmorOverride, invFinalScript.Name);
                        if (newRenderScriptName == null)
                        {
                            Console.WriteLine($"Unable to find corresponding renderScript for {invFinalScript.Name} ({renderArmor})");
                            continue;
                        }
                        var newRenderScript = CopyInvScriptToRender(invFinalScript);
                        newRenderScript.Name = newRenderScriptName;

                        Console.WriteLine($"Created new script: {newRenderScriptName}");

                        if (renderUDScript == null)
                        {
                            //var newRenderScript = invScript.DeepCopy();

                            /*if (renderArmorOverride.VirtualMachineAdapter == null)
                            {
                                renderArmorOverride.VirtualMachineAdapter = new VirtualMachineAdapter();
                            }*/
                            
                            renderArmorOverride.VirtualMachineAdapter.Scripts.Add(newRenderScript);
                            addKeywords(renderArmorOverride);
                            Console.WriteLine($"Device {renderArmorOverride} patched!");
                            totalPatched++;
                            // add keywords
                            // 1. add epatchkw
                            // 2. add eudkw
                            // 3. add patchnomode if not using modes
                        } else
                        {
                            Console.WriteLine($"WARNING: Render device {renderArmor} already has UD script! Creating new render device!");
                            newDevices++;
                            var newRenderArmor = state.PatchMod.Armors.DuplicateInAsNewRecord(renderArmor);
                            newRenderArmor.EditorID = newRenderArmor.EditorID + "_AddedRenderDevice";
                            var newRenderArmorScripts = newRenderArmor.VirtualMachineAdapter!.Scripts;
                            
                            newRenderArmorScripts[newRenderArmorScripts.FindIndex(script => script == renderUDScript)] = newRenderScript;
                            invScript.Properties[invScript.Properties.FindIndex(prop => prop.Name == "devideRendered")].Cast<ScriptObjectProperty>().Object = newRenderArmor.ToLink();
                            Console.WriteLine($"---NEW DEVICE {newRenderArmor} CREATED!---");
                            //newRenderArmor.VirtualMachineAdapter!.Scripts.FindIndex(x => x == renderUDScript) = newRenderScript;
                        }
                        //newInvScript.Properties.
                        //newInvScript.Name = "UD_CustomDevice_EquipScript";
                        //newInvScript.Properties = UDCDProp;
                        // Up to element edit values
                        //UDCDProp.
                    } else if (renderUDScript == null)
                    {
                        Console.WriteLine($"Device with patched INV but not patched REND detected. Patching renderDevice {renderArmor}.");
                        var newRenderScriptName = GetUdScriptNameFromArmor(renderArmorOverride, "zadequipscript");
                        if (newRenderScriptName == null)
                        {
                            continue;
                        }
                        var newRenderScript = CopyInvScriptToRender(invFinalScript);
                        newRenderScript.Name = newRenderScriptName;
                        renderArmorOverride.VirtualMachineAdapter.Scripts.Add(newRenderScript);
                        addKeywords(renderArmorOverride);
                        Console.WriteLine($"Repatched RenderDevice {renderArmor} of InventoryDevice {invArmorGetter}");
                    }
                }
            }
        }
    }
}
