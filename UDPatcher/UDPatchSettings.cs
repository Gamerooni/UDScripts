using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using Mutagen.Bethesda.Plugins.Cache;
using System.Reflection;
using CommandLine;
using Mutagen.Bethesda;

namespace UDPatcher
{
    public class UDPatchSettings
    {
        [MaintainOrder]
        [Tooltip("If patched devices can have modifiers added by UD (on runtime)")]
        public bool UseModifiers = false;
        [MaintainOrder]
        [Tooltip("Leave this blank to patch the entire load order")]
        public HashSet<ModKey> ModsToPatch = new();
        [MaintainOrder]
        [Tooltip("Mods which will be ignored when patching")]
        public HashSet<ModKey> ModsToNotPatch = new();
        [MaintainOrder]
        public UDRenderSettings RenderScriptSettings = new();
        [MaintainOrder]
        public UDInventorySettings InventoryScriptSettings = new();
        [MaintainOrder]
        [Tooltip("DO NOT EDIT THESE UNLESS YOU KNOW DAMN WELL WHAT YOU'RE DOING")]
        public UDImportantConstants IMPORTANTCONSTANTS = new();
    }

    public class UDRenderSettings
    {
        [Tooltip("The DD Inventory scripts which match to a single UD render script")]
        [MaintainOrder]
        public Dictionary<string, HashSet<string>> ScriptMatches = new();
        [MaintainOrder]
        public List<UDOtherSettings> OtherMatches = new();
        [Tooltip("Inventory Script values to transfer to render script, and their modified name (leave blank to keep as-is)")]
        [MaintainOrder]
        public Dictionary<string, string?> ScriptValues = new();
    }

    public class UDInventorySettings
    {
        [Tooltip("The DD inventory scripts which match to a single UD inventory script")]
        public Dictionary<string, HashSet<string>> ScriptMatches = new();
    }

    public class UDOtherSettings
    {
        [Tooltip("This rule will only apply when the matched UD script is one of the following")]
        [MaintainOrder]
        public HashSet<string> InputScripts = new HashSet<string>();
        public List<UDKwSettings> KeywordMatch = new();
        public List<UDNameSearchSettings> NameMatch = new();
    }

    public class UDKwSettings : UDOtherSetting
    {
        [Tooltip("Will change UD script only if Armor contains one of the chosen Keywords")]
        public HashSet<IFormLinkGetter<IKeywordGetter>> Keywords = new();
    }

    public class UDNameSearchSettings : UDOtherSetting
    {
        [Tooltip("Will change UD script only if the Armor's name contains the chosen text")]
        public string SearchText = string.Empty;
    }

    public abstract class UDOtherSetting
    {
        public string OutputScript = string.Empty;
        [Tooltip("Priority of the rule being applied (if several rules apply, the highest priority overrules)")]
        public int Priority = 0;
    }

    public class UDImportantConstants
    {
        public IFormLinkGetter<IKeywordGetter> zadInvKeywordGetter = FormLinkGetter<IKeywordGetter>.Null;
        public IFormLinkGetter<IKeywordGetter> udInvKeywordGetter = FormLinkGetter<IKeywordGetter>.Null;
        public IFormLinkGetter<IKeywordGetter> udPatchKwGetter = FormLinkGetter<IKeywordGetter>.Null;
        public IFormLinkGetter<IKeywordGetter> udKwGetter = FormLinkGetter<IKeywordGetter>.Null;
        public IFormLinkGetter<IKeywordGetter> udPatchNoModeKwGetter = FormLinkGetter<IKeywordGetter>.Null;
        public IFormLinkGetter<IQuestGetter> udMainQstGetter = FormLinkGetter<IQuestGetter>.Null;
    }
    
    public class UDImportantConstantsFound : UDImportantConstants 
    { 
        public IKeywordGetter? zadInvKeyword { get; set; }
        public IKeywordGetter? udInvKeyword { get; set; }
        public IKeywordGetter? udPatchKw { get; set; }
        public IKeywordGetter? udKw { get; private set; }
        public IKeywordGetter? udPatchNoModeKw { get; set; }
        public IQuestGetter? udMainQst { get; set; }

        public ILinkCache? LinkCache { get; set; }

        public UDImportantConstantsFound(UDImportantConstants parent, ILinkCache linkCache)
        {
            LinkCache = linkCache;
            foreach (FieldInfo property in typeof(UDImportantConstants).GetFields())
            {
                if (typeof(IFormLinkGetter<IKeywordGetter>).IsAssignableFrom(property.FieldType))
                {
                    FunnierFunction<IKeywordGetter>(property, parent);
                } else if (typeof(IFormLinkGetter<IQuestGetter>).IsAssignableFrom(property.FieldType))
                {
                    FunnierFunction<IQuestGetter>(property, parent);
                }
                
            }
            
        }

        private T FunnyFunction<T> (FieldInfo property, UDImportantConstants original) where T : class, ISkyrimMajorRecordGetter
        {
            return property.GetValue(original).Cast<IFormLinkGetter<T>>().Resolve(LinkCache!);
        }

        private void FunnierFunction<T> (FieldInfo property, UDImportantConstants original) where T : class, ISkyrimMajorRecordGetter
        {
            GetType()!
                .GetProperty(property.Name
                .Replace("Getter", ""))!
                .SetValue(this, FunnyFunction<T>(property, original));
        }
    }
}
