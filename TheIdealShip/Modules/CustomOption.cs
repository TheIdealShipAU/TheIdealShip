using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TheIdealShip.Utilities;
using UnityEngine;
using Hazel;
using HarmonyLib;
using System.Text;
using System.Reflection;
using System.Linq;
using static TheIdealShip.Languages.Language;

namespace TheIdealShip.Modules
{
    public class CustomOption
    {
        // 定义Type
        public enum CustomOptionType
        {
            General,
            Impostor,
            Neutral,
            Crewmate,
            Modifier,
        }

        public static List<CustomOption> options = new List<CustomOption>();
        public static int preset = 0;

        public int id;
        public string name;
        public Color color;
        public System.Object[] selections;
        public int defaultSelection;
        public ConfigEntry<int> entry;
        public int selection;
        public OptionBehaviour optionBehaviour;
        public CustomOption parent;
        public bool isHeader;
        public CustomOptionType type;

        // 创建Option
        public CustomOption
        (
            int id,
            CustomOptionType type,
            string name,
            System.Object[] selections,
            System.Object defaultValue,
            CustomOption parent,
            bool isHeader
        )
        {
            this.id = id;
            this.name = parent == null ? name : "- " + name;
            this.selections = selections;
            int index = Array.IndexOf(selections,defaultValue);
            this.defaultSelection = index >= 0 ? index : 0;
            this.parent = parent;
            this.isHeader = isHeader;
            this.type = type;
            selection = 0;
            if (id !=0)
            {
                entry = TheIdealShipPlugin.Instance.Config.Bind($"Preset{preset}", id.ToString(), defaultSelection);
                selection = Mathf.Clamp(entry.Value, 0, selections.Length - 1);
            }
            options.Add(this);
        }

        public static CustomOption Create
        (
            int id,
            CustomOptionType type,
            string name,
            string[] selections,
            CustomOption parent = null,
            bool isHeader =false
        )
        {
            return new CustomOption(id, type, name, selections, "", parent, isHeader);
        }

        public static CustomOption Create
        (
            int id,
            CustomOptionType type,
            string name,
            float defaultValue,
            float min,
            float max,
            float step,
            CustomOption parent = null,
            bool isHeader = false
        )
        {
            List<object> selections = new();
            for (float s =min; s <= max; s += step)
                selections.Add(s);

            return new CustomOption
            (
                id,
                type,
                name,
                selections.ToArray(),
                defaultValue,
                parent,
                isHeader
            );
        }

        public static CustomOption Create
        (
            int id,
            CustomOptionType type,
            string name,
            bool defaultValue,
            CustomOption parent = null,
            bool isHeader = false
        )
        {
            return new CustomOption
            (
                id,
                type,
                name,
                new string[]{"Off","On"},
                defaultValue ? "On" : "Off",
                parent,
                isHeader
            );
        }

        public static void switchPreset(int newPreset)
        {
            CustomOption.preset = newPreset;
            foreach (CustomOption option in CustomOption.options)
            {
                if (option.id == 0) continue;

                option.entry = TheIdealShipPlugin.Instance.Config.Bind($"Preset{preset}",option.id.ToString(),option.defaultSelection);
                option.selection = Mathf.Clamp(option.entry.Value, 0, option.selections.Length - 1);
                if (option.optionBehaviour != null && option.optionBehaviour is StringOption stringOption)
                {
                    stringOption.oldValue = stringOption.Value = option.selection;
                    stringOption.ValueText.text = GetString(option.selections[option.selection].ToString());
                }
            }
        }

        public static void ShareOptionSelections()
        {
            if (CachedPlayer.AllPlayers.Count <= 1 || AmongUsClient.Instance!.AmHost == false && CachedPlayer.LocalPlayer.PlayerControl == null) return;

            var optionsList = new List<CustomOption>(CustomOption.options);
            while (optionsList.Any())
            {
                byte amount = (byte) Math.Min(optionsList.Count,20);
                var writer = AmongUsClient.Instance!.StartRpcImmediately(CachedPlayer.LocalPlayer.PlayerControl.NetId, (byte)CustomRPC.ShareOptions,SendOption.Reliable,-1);
                writer.Write(amount);
                for (int i = 0; i < amount; i++)
                {
                    var option =optionsList [0];
                    optionsList.RemoveAt(0);
                    writer.WritePacked((uint) option.id);
                    writer.WritePacked(Convert.ToUInt32(option.selection));
                }
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }

        // Getter

        public int getSelection()
        {
            return selection;
        }

        public bool getBool()
        {
            return selection > 0;
        }

        public float getFloat()
        {
            return (float)selections[selection];
        }

        public int getQuantity()
        {
            return selection + 1;
        }

        // Option changes
        public void updateSelection(int newSelection)
        {
            selection = Mathf.Clamp((newSelection + selections.Length) % selections.Length, 0, selections.Length - 1);
            if (optionBehaviour != null && optionBehaviour is StringOption stringOption)
            {
                stringOption.oldValue = stringOption.Value = selection;
                stringOption.ValueText.text = GetString(selections[selection].ToString());

                if (AmongUsClient.Instance?.AmHost == true && CachedPlayer.LocalPlayer.PlayerControl)
                {
                    if (id == 0) switchPreset(selection);
                    else if (entry != null) entry.Value =selection;

                    ShareOptionSelections();
                }
            }
        }

        [HarmonyPatch(typeof(GameOptionsMenu),nameof(GameOptionsMenu.Start))]
        class GameOptionsMenuPatch
        {
            public static void Postfix(GameOptionsMenu __instance)
            {
                createTabs(__instance);
            }

            private static void createTabs(GameOptionsMenu __instance)
            {
                bool isReturn = setNames
                (
                    new Dictionary<string, string>()
                    {
                        ["TISSettings"] = "The Ideal Ship Settings",
                        ["ImpostorSettings"] = "Impostor Roles Settings",
                        ["NeutralSettings"] = "Neutral Roles Settings",
                        ["CrewmateSettings"] = "Crewmate Roles Settings",
                        ["ModifierSettings"] = "Modifier Settings"
                    }
                );
                if (isReturn) return;

                var template = UnityEngine.Object.FindObjectsOfType<StringOption>().FirstOrDefault();
                if (template == null) return;
                var gameSettings = GameObject.Find("Game Settings");
                var gameSettingMenu = UnityEngine.Object.FindObjectsOfType<GameSettingMenu>().FirstOrDefault();

                var tisSettings = UnityEngine.Object.Instantiate(gameSettings,gameSettings.transform.parent);
                var tisMenu = getMenu(tisSettings,"TISSettings");

                var impostorSettings = UnityEngine.Object.Instantiate(gameSettings, gameSettings.transform.parent);
                var impostorMenu = getMenu(impostorSettings, "ImpostorSettings");

                var neutralSettings = UnityEngine.Object.Instantiate(gameSettings, gameSettings.transform.parent);
                var neutralMenu = getMenu(neutralSettings, "NeutralSettings");

                var crewmateSettings = UnityEngine.Object.Instantiate(gameSettings, gameSettings.transform.parent);
                var crewmateMenu = getMenu(crewmateSettings, "CrewmateSettings");

                var modifierSettings = UnityEngine.Object.Instantiate(gameSettings, gameSettings.transform.parent);
                var modifierMenu = getMenu(modifierSettings, "ModifierSettings");

                var roleTab = GameObject.Find("RoleTab");
                var gameTab = GameObject.Find("GameTab");

                var tisTab = UnityEngine.Object.Instantiate(roleTab,roleTab.transform.parent);
                var tisTabHighlight = getTabHighlight(tisTab,"TheIdealShipTab","TheIdealShip.Resources.Tab.png");

                var impostorTab = UnityEngine.Object.Instantiate(roleTab, tisTab.transform);
                var impostorTabHighlight = getTabHighlight(impostorTab, "ImpostorTab", "TheIdealShip.Resources.TabI.png");

                var neutralTab = UnityEngine.Object.Instantiate(roleTab, impostorTab.transform);
                var neutralTabHighlight = getTabHighlight(neutralTab, "NeutralTab", "TheIdealShip.Resources.TabN.png");

                var crewmateTab = UnityEngine.Object.Instantiate(roleTab, neutralTab.transform);
                var crewmateTabHighlight = getTabHighlight(crewmateTab, "CrewmateTab", "TheIdealShip.Resources.TabC.png");

                var modifierTab = UnityEngine.Object.Instantiate(roleTab, crewmateTab.transform);
                var modifierTabHighlight = getTabHighlight(modifierTab, "ModifierTab", "TheIdealShip.Resources.TabM.png");

                gameTab.transform.position += Vector3.left * 3f;
                roleTab.transform.position += Vector3.left * 3f;
                tisTab.transform.position += Vector3.left * 2f;
                impostorTab.transform.localPosition = Vector3.right * 1f;
                neutralTab.transform.localPosition = Vector3.right * 1f;
                crewmateTab.transform.localPosition = Vector3.right * 1f;
                modifierTab.transform.localPosition = Vector3.right * 1f;

                var tabs = new GameObject[]{gameTab,roleTab,tisTab,impostorTab,neutralTab,crewmateTab,modifierTab};
                var settingsHighlightMap = new Dictionary<GameObject,SpriteRenderer>
                {
                    [gameSettingMenu.RegularGameSettings] = gameSettingMenu.GameSettingsHightlight,
                    [gameSettingMenu.RolesSettings.gameObject] = gameSettingMenu.RolesSettingsHightlight,
                    [tisSettings.gameObject] = tisTabHighlight,
                    [impostorSettings.gameObject] = impostorTabHighlight,
                    [neutralSettings.gameObject] = neutralTabHighlight,
                    [crewmateSettings.gameObject] = crewmateTabHighlight,
                    [modifierSettings.gameObject] = modifierTabHighlight
                };
                for (int i = 0; i < tabs.Length; i++)
                {
                    var button = tabs[i].GetComponentInChildren<PassiveButton>();
                    if (button == null) continue;
                    int copiedIndex = i;
                    button.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                    button.OnClick.AddListener((Action)(() => {setListener(settingsHighlightMap, copiedIndex);}));
                }

                destroyOptions(new List<List<OptionBehaviour>>
                {
                    tisMenu.GetComponentsInChildren<OptionBehaviour>().ToList(),
                    impostorMenu.GetComponentsInChildren<OptionBehaviour>().ToList(),
                    neutralMenu.GetComponentsInChildren<OptionBehaviour>().ToList(),
                    crewmateMenu.GetComponentsInChildren<OptionBehaviour>().ToList(),
                    modifierMenu.GetComponentsInChildren<OptionBehaviour>().ToList()
                }
                );

                List<OptionBehaviour> tisOptions = new List<OptionBehaviour>();
                List<OptionBehaviour> impostorOptions = new List<OptionBehaviour>();
                List<OptionBehaviour> neutralOptions = new List<OptionBehaviour>();
                List<OptionBehaviour> crewmateOptions = new List<OptionBehaviour>();
                List<OptionBehaviour> modifierOptions = new List<OptionBehaviour>();

                List<Transform> menus = new List<Transform>() { tisMenu.transform, impostorMenu.transform, neutralMenu.transform, crewmateMenu.transform, modifierMenu.transform };
                List<List<OptionBehaviour>> optionBehaviours = new List<List<OptionBehaviour>>() { tisOptions, impostorOptions, neutralOptions, crewmateOptions, modifierOptions };

                for (int i = 0; i < CustomOption.options.Count; i++)
                {
                    CustomOption option = CustomOption.options[i];
                    if ((int)option.type > 4) continue;
                    if (option.optionBehaviour == null)
                    {
                        StringOption stringOption = UnityEngine.Object.Instantiate(template,menus[(int)option.type]);
                        stringOption.OnValueChanged = new Action<OptionBehaviour>((o) => {});
                        stringOption.TitleText.text = GetString(option.name);
                        if (option.name != option.name.Replace("-", ""))
                        {
                            stringOption.TitleText.text = "- " + GetString(option.name.Replace("- ", ""));
                        }
                        if (option.name != option.name.Replace("</color>", ""))
                        {
                            var name = option.name.Replace("</color>", "");
                            var found = name.IndexOf(">");
                            name = option.name.Replace(name.Substring(found + 1),GetString(name.Substring(found + 1)));
                            stringOption.TitleText.text = name;
                        }
                        stringOption.Value = stringOption.oldValue = option.selection;
                        stringOption.ValueText.text = GetString(option.selections[option.selection].ToString());

                        option.optionBehaviour = stringOption;
                    }
                    option.optionBehaviour.gameObject.SetActive(true);
                }

                setOptions
                (
                    new List<GameOptionsMenu> { tisMenu, impostorMenu, neutralMenu, crewmateMenu, modifierMenu },
                    new List<List<OptionBehaviour>> { tisOptions, impostorOptions, neutralOptions, crewmateOptions, modifierOptions },
                    new List<GameObject> { tisSettings, impostorSettings, neutralSettings, crewmateSettings, modifierSettings }
                );

                adaptTaskCount(__instance);
            }

            private static bool setNames (Dictionary<string,string> gameObjectNameDisplayNameMap)
            {
                foreach (KeyValuePair <string,string> entry in gameObjectNameDisplayNameMap)
                {
                    if (GameObject.Find(entry.Key) != null)
                    {
                        GameObject.Find(entry.Key).transform.FindChild("GameGroup").FindChild("Text").GetComponent<TMPro.TextMeshPro>().SetText(entry.Value);
                        return true;
                    }
                }
                return false;
            }

            private static GameOptionsMenu getMenu(GameObject setting, string settingName)
            {
                var menu = setting.transform.FindChild("GameGroup").FindChild("SliderInner").GetComponent<GameOptionsMenu>();
                setting.name = settingName;
                return menu;
            }

            private static SpriteRenderer getTabHighlight (GameObject tab,string tabname,string tabSpritePath)
            {
                var tabHighlight = tab.transform.FindChild("Hat Button").FindChild("Tab Background").GetComponent<SpriteRenderer>();
                tab.transform.FindChild("Hat Button").FindChild("Icon").GetComponent<SpriteRenderer>().sprite = Helpers.LoadSpriteFromResources(tabSpritePath,100f);
                tab.name = tabname;
                return tabHighlight;
            }

            private static void setListener(Dictionary<GameObject,SpriteRenderer> settingsHighlightMap,int index)
            {
                foreach (KeyValuePair<GameObject,SpriteRenderer> entry in settingsHighlightMap)
                {
                    entry.Key.SetActive(false);
                    entry.Value.enabled = false;
                }
                settingsHighlightMap.ElementAt(index).Key.SetActive(true);
                settingsHighlightMap.ElementAt(index).Value.enabled = true;
            }

            private static void destroyOptions (List<List<OptionBehaviour>> optionBehavioursList)
            {
                foreach (List<OptionBehaviour> optionBehaviours in optionBehavioursList)
                {
                    foreach (OptionBehaviour option in optionBehaviours)
                    {
                        UnityEngine.Object.Destroy(option.gameObject);
                    }
                }
            }

            private static void setOptions (List<GameOptionsMenu> menus,List<List<OptionBehaviour>> options,List<GameObject> settings)
            {
                if (!(menus.Count == options.Count && options.Count == settings.Count))
                {
                    TheIdealShipPlugin.Logger.LogError("List counts are not equal");
                    return;
                }
                for (int i = 0; i < menus.Count; i++)
                {
                    menus[i].Children = options[i].ToArray();
                    settings[i].gameObject.SetActive(false);
                }
            }

            private static void adaptTaskCount(GameOptionsMenu __instance)
            {
                var commonTaskOption = __instance.Children.FirstOrDefault(x => x.name == "NumCommonTasks").TryCast<NumberOption>();
                if (commonTaskOption != null) commonTaskOption.ValidRange = new FloatRange(0f,4f);

                var shortTasksOption = __instance.Children.FirstOrDefault(x => x.name == "NumShortTasks").TryCast<NumberOption>();
                if (shortTasksOption != null) shortTasksOption.ValidRange = new FloatRange(0f, 23f);

                var longTasksOption = __instance.Children.FirstOrDefault(x => x.name == "NumLongTasks").TryCast<NumberOption>();
                if (longTasksOption != null) longTasksOption.ValidRange = new FloatRange(0f, 15f);
            }
        }

        [HarmonyPatch(typeof(StringOption), nameof(StringOption.OnEnable))]
        public class StringOptionEnablePatch
        {
            public static bool Prefix(StringOption __instance)
            {
                CustomOption option = CustomOption.options.FirstOrDefault(option => option.optionBehaviour == __instance);
                if (option == null) return true;

                __instance.OnValueChanged = new Action<OptionBehaviour>((o) => { });
                __instance.TitleText.text = GetString(option.name);
                if (option.name != option.name.Replace("-", ""))
                {
                    __instance.TitleText.text = "- " + GetString(option.name.Replace("- ", ""));
                }
                if (option.name != option.name.Replace("</color>", ""))
                {
                    var name = option.name.Replace("</color>", "");
                    var found = name.IndexOf(">");
                    name = option.name.Replace(name.Substring(found + 1), GetString(name.Substring(found + 1)));
                    __instance.TitleText.text = name;
                }
                __instance.Value = __instance.oldValue = option.selection;
                __instance.ValueText.text = GetString(option.selections[option.selection].ToString());

                return false;
            }
        }

        [HarmonyPatch(typeof(StringOption), nameof(StringOption.Increase))]
        public class StringOptionIncreasePatch
        {
            public static bool Prefix(StringOption __instance)
            {
                CustomOption option = CustomOption.options.FirstOrDefault(option => option.optionBehaviour == __instance);
                if (option == null) return true;
                option.updateSelection(option.selection + 1);
                return false;
            }
        }

        [HarmonyPatch(typeof(StringOption), nameof(StringOption.Decrease))]
        public class StringOptionDecreasePatch
        {
            public static bool Prefix(StringOption __instance)
            {
                CustomOption option = CustomOption.options.FirstOrDefault(option => option.optionBehaviour == __instance);
                if (option == null) return true;
                option.updateSelection(option.selection - 1);
                return false;
            }
        }

        [HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.Update))]
        class GameOptionsMenuUpdatePatch
        {
            private static float timer = 1f;
            public static void Postfix(GameOptionsMenu __instance)
            {
                // Return Menu Update if in normal among us settings 
                var gameSettingMenu = UnityEngine.Object.FindObjectsOfType<GameSettingMenu>().FirstOrDefault();
                if (gameSettingMenu.RegularGameSettings.active || gameSettingMenu.RolesSettings.gameObject.active) return;

                __instance.GetComponentInParent<Scroller>().ContentYBounds.max = -0.5F + __instance.Children.Length * 0.55F;
                timer += Time.deltaTime;
                if (timer < 0.1f) return;
                timer = 0f;

                float offset = 2.75f;
                foreach (CustomOption option in CustomOption.options)
                {
                    if (GameObject.Find("TISSettings") && option.type != CustomOption.CustomOptionType.General)
                        continue;
                    if (GameObject.Find("ImpostorSettings") && option.type != CustomOption.CustomOptionType.Impostor)
                        continue;
                    if (GameObject.Find("NeutralSettings") && option.type != CustomOption.CustomOptionType.Neutral)
                        continue;
                    if (GameObject.Find("CrewmateSettings") && option.type != CustomOption.CustomOptionType.Crewmate)
                        continue;
                    if (GameObject.Find("ModifierSettings") && option.type != CustomOption.CustomOptionType.Modifier)
                        continue;
                    if (option?.optionBehaviour != null && option.optionBehaviour.gameObject != null)
                    {
                        bool enabled = true;
                        var parent = option.parent;
                        while (parent != null && enabled)
                        {
                            enabled = parent.selection != 0;
                            parent = parent.parent;
                        }
                        option.optionBehaviour.gameObject.SetActive(enabled);
                        if (enabled)
                        {
                            offset -= option.isHeader ? 0.75f : 0.5f;
                            option.optionBehaviour.transform.localPosition = new Vector3(option.optionBehaviour.transform.localPosition.x, offset, option.optionBehaviour.transform.localPosition.z);
                        }
                    }
                }
            }
        }
    }
}