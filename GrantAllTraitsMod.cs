using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine.UI;
using GameData.Domains.Character;

namespace GrantAllTraitsMod
{
    [PluginConfig("我全都要", "1.0.0.0", "jiliangsui")]
    public class GrantAllTraitsPlugin : TaiwuRemakePlugin
    {
        private static bool _enabled = true;
        private static bool _bookWeightEnabled = true;
        private static bool _foodWeightEnabled = true;
        private static bool _medicineWeightEnabled = true;
        private static bool _equipmentWeightEnabled = true;
        private static bool _toolWeightEnabled = true;
        private static bool _materialWeightEnabled = true;
        private static bool _miscWeightEnabled = true;
        private static bool _cricketWeightEnabled = true;
        private static bool _lockFiveElementsToMix = true;
        private static bool _breakthroughAlwaysSuccess = true;
        private static int _customPointMainAttribute = 300;
        private static int _customPointLifeSkill = 800;
        private static int _customPointCombatSkill = 700;
        private static int _customPointFeature = 7;
        private static string GetModRootDirectory()
        {
            // 从 DLL 自身位置出发：Mod/{ModName}/Plugins/GrantAllTraitsMod.dll
            try
            {
                var dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                Log($"GetModRootDirectory: Location='{dllPath}'");
                if (!string.IsNullOrEmpty(dllPath) && System.IO.Path.IsPathRooted(dllPath))
                {
                    var pluginsDir = System.IO.Path.GetDirectoryName(dllPath);
                    if (pluginsDir != null && pluginsDir.EndsWith("Plugins"))
                    {
                        var root = System.IO.Path.GetDirectoryName(pluginsDir);
                        Log($"GetModRootDirectory: 从 DLL 位置推算 -> {root}");
                        return root;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"GetModRootDirectory: Location 方式异常: {ex.Message}");
            }

            // 回退：扫描已知路径找 Settings.Lua
            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
            var dir = baseDir;
            for (int i = 0; i < 3; i++)
            {
                var settingsPath = System.IO.Path.Combine(dir, "Mod", "我全都要", "Settings.Lua");
                if (System.IO.File.Exists(settingsPath))
                {
                    Log($"GetModRootDirectory: 从 {dir} 找到 Settings.Lua");
                    return System.IO.Path.Combine(dir, "Mod", "我全都要");
                }
                var parent = System.IO.Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
                if (parent == null || parent == dir) break;
                dir = parent;
            }

            // 额外：从游戏根目录的 Mod/ 下所有子目录找我们的 Settings.Lua
            try
            {
                var gameRoot = dir;
                var modDir = System.IO.Path.Combine(gameRoot, "Mod");
                if (System.IO.Directory.Exists(modDir))
                {
                    foreach (var subDir in System.IO.Directory.GetDirectories(modDir))
                    {
                        var settingsFile = System.IO.Path.Combine(subDir, "Settings.Lua");
                        var ourDll = System.IO.Path.Combine(subDir, "Plugins", "GrantAllTraitsMod.dll");
                        if (System.IO.File.Exists(settingsFile) && System.IO.File.Exists(ourDll))
                        {
                            Log($"GetModRootDirectory: 在 Mod 目录找到 -> {subDir}");
                            return subDir;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"GetModRootDirectory: 扫描 Mod 目录异常: {ex.Message}");
            }

            Log($"GetModRootDirectory: 未找到 Settings.Lua，返回 BaseDirectory={baseDir}");
            return baseDir;
        }
        private static string LogPath;
        private static string SettingsPath;
        private static string _modRoot;
        private static System.DateTime _lastSettingsReadTime = System.DateTime.MinValue;

        private static void ReadSettings()
        {
            try
            {
                if (SettingsPath == null)
                {
                    if (_modRoot == null) _modRoot = GetModRootDirectory();
                    SettingsPath = Path.Combine(_modRoot, "Settings.Lua");
                }
                if (File.Exists(SettingsPath))
                {
                    string content = File.ReadAllText(SettingsPath);
                    string[] lines = content.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim().TrimEnd(',');
                        if (trimmed == "Enabled = false") _enabled = false;
                        else if (trimmed == "Enabled = true") _enabled = true;
                        else if (trimmed == "BookWeightEnabled = false") _bookWeightEnabled = false;
                        else if (trimmed == "BookWeightEnabled = true") _bookWeightEnabled = true;
                        else if (trimmed == "FoodWeightEnabled = false") _foodWeightEnabled = false;
                        else if (trimmed == "FoodWeightEnabled = true") _foodWeightEnabled = true;
                        else if (trimmed == "MedicineWeightEnabled = false") _medicineWeightEnabled = false;
                        else if (trimmed == "MedicineWeightEnabled = true") _medicineWeightEnabled = true;
                        else if (trimmed == "EquipmentWeightEnabled = false") _equipmentWeightEnabled = false;
                        else if (trimmed == "EquipmentWeightEnabled = true") _equipmentWeightEnabled = true;
                        else if (trimmed == "ToolWeightEnabled = false") _toolWeightEnabled = false;
                        else if (trimmed == "ToolWeightEnabled = true") _toolWeightEnabled = true;
                        else if (trimmed == "MaterialWeightEnabled = false") _materialWeightEnabled = false;
                        else if (trimmed == "MaterialWeightEnabled = true") _materialWeightEnabled = true;
                        else if (trimmed == "MiscWeightEnabled = false") _miscWeightEnabled = false;
                        else if (trimmed == "MiscWeightEnabled = true") _miscWeightEnabled = true;
                        else if (trimmed == "CricketWeightEnabled = false") _cricketWeightEnabled = false;
                        else if (trimmed == "CricketWeightEnabled = true") _cricketWeightEnabled = true;
                        else if (trimmed == "LockFiveElementsToMix = false") _lockFiveElementsToMix = false;
                        else if (trimmed == "LockFiveElementsToMix = true") _lockFiveElementsToMix = true;
                        else if (trimmed == "BreakthroughAlwaysSuccess = false") _breakthroughAlwaysSuccess = false;
                        else if (trimmed == "BreakthroughAlwaysSuccess = true") _breakthroughAlwaysSuccess = true;
                        else if (trimmed.StartsWith("CustomPointMainAttribute = "))
                        {
                            var raw = trimmed.Substring("CustomPointMainAttribute = ".Length).Trim().Trim('"');
                            // Slider 可能写入整数或浮点数
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointMainAttribute = System.Math.Max((int)f, 300);
                        }
                        else if (trimmed.StartsWith("CustomPointLifeSkill = "))
                        {
                            var raw = trimmed.Substring("CustomPointLifeSkill = ".Length).Trim().Trim('"');
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointLifeSkill = System.Math.Max((int)f, 800);
                        }
                        else if (trimmed.StartsWith("CustomPointCombatSkill = "))
                        {
                            var raw = trimmed.Substring("CustomPointCombatSkill = ".Length).Trim().Trim('"');
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointCombatSkill = System.Math.Max((int)f, 700);
                        }
                        else if (trimmed.StartsWith("CustomPointFeature = "))
                        {
                            var raw = trimmed.Substring("CustomPointFeature = ".Length).Trim().Trim('"');
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointFeature = System.Math.Max((int)f, 7);
                        }
                    }
                    _lastSettingsReadTime = System.IO.File.GetLastWriteTimeUtc(SettingsPath);
                }
                else
                {
                    _enabled = true;
                    _bookWeightEnabled = true;
                    _foodWeightEnabled = true;
                    _medicineWeightEnabled = true;
                    _equipmentWeightEnabled = true;
                    _toolWeightEnabled = true;
                    _materialWeightEnabled = true;
                    _miscWeightEnabled = true;
                    _cricketWeightEnabled = true;
                    _lockFiveElementsToMix = true;
                }
            }
            catch
            {
                _enabled = true;
                _bookWeightEnabled = true;
            }
        }

        // 单独重读点数设置（用于 OnModSettingUpdate 中热刷新）
        private void ReadCustomPointSettings()
        {
            try
            {
                if (SettingsPath == null)
                {
                    if (_modRoot == null) _modRoot = GetModRootDirectory();
                    SettingsPath = Path.Combine(_modRoot, "Settings.Lua");
                }
                if (File.Exists(SettingsPath))
                {
                    string content = File.ReadAllText(SettingsPath);
                    string[] lines = content.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim().TrimEnd(',');
                        if (trimmed.StartsWith("CustomPointMainAttribute = "))
                        {
                            var raw = trimmed.Substring("CustomPointMainAttribute = ".Length).Trim().Trim('"');
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointMainAttribute = System.Math.Max((int)f, 300);
                        }
                        else if (trimmed.StartsWith("CustomPointLifeSkill = "))
                        {
                            var raw = trimmed.Substring("CustomPointLifeSkill = ".Length).Trim().Trim('"');
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointLifeSkill = System.Math.Max((int)f, 800);
                        }
                        else if (trimmed.StartsWith("CustomPointCombatSkill = "))
                        {
                            var raw = trimmed.Substring("CustomPointCombatSkill = ".Length).Trim().Trim('"');
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointCombatSkill = System.Math.Max((int)f, 700);
                        }
                        else if (trimmed.StartsWith("CustomPointFeature = "))
                        {
                            var raw = trimmed.Substring("CustomPointFeature = ".Length).Trim().Trim('"');
                            if (float.TryParse(raw, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out float f))
                                _customPointFeature = System.Math.Max((int)f, 7);
                        }
                    }
                }
            }
            catch { }
        }

        public override void Dispose() { }

        public override void OnModSettingUpdate()
        {
            ReadHotReloadSettings();

            // 主页修改点数后也立即生效
            ReadCustomPointSettings();
            ApplyCustomPoints();
        }

        // 只热刷新重量和五行开关（特质/开局点数不支持热变配）
        private static void ReadHotReloadSettings()
        {
            try
            {
                if (SettingsPath == null)
                {
                    if (_modRoot == null) _modRoot = GetModRootDirectory();
                    SettingsPath = Path.Combine(_modRoot, "Settings.Lua");
                }
                if (File.Exists(SettingsPath))
                {
                    string content = File.ReadAllText(SettingsPath);
                    string[] lines = content.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim().TrimEnd(',');
                        if (trimmed == "BookWeightEnabled = false") _bookWeightEnabled = false;
                        else if (trimmed == "BookWeightEnabled = true") _bookWeightEnabled = true;
                        else if (trimmed == "FoodWeightEnabled = false") _foodWeightEnabled = false;
                        else if (trimmed == "FoodWeightEnabled = true") _foodWeightEnabled = true;
                        else if (trimmed == "MedicineWeightEnabled = false") _medicineWeightEnabled = false;
                        else if (trimmed == "MedicineWeightEnabled = true") _medicineWeightEnabled = true;
                        else if (trimmed == "EquipmentWeightEnabled = false") _equipmentWeightEnabled = false;
                        else if (trimmed == "EquipmentWeightEnabled = true") _equipmentWeightEnabled = true;
                        else if (trimmed == "ToolWeightEnabled = false") _toolWeightEnabled = false;
                        else if (trimmed == "ToolWeightEnabled = true") _toolWeightEnabled = true;
                        else if (trimmed == "MaterialWeightEnabled = false") _materialWeightEnabled = false;
                        else if (trimmed == "MaterialWeightEnabled = true") _materialWeightEnabled = true;
                        else if (trimmed == "MiscWeightEnabled = false") _miscWeightEnabled = false;
                        else if (trimmed == "MiscWeightEnabled = true") _miscWeightEnabled = true;
                        else if (trimmed == "CricketWeightEnabled = false") _cricketWeightEnabled = false;
                        else if (trimmed == "CricketWeightEnabled = true") _cricketWeightEnabled = true;
                        else if (trimmed == "LockFiveElementsToMix = false") _lockFiveElementsToMix = false;
                        else if (trimmed == "LockFiveElementsToMix = true") _lockFiveElementsToMix = true;
                        else if (trimmed == "BreakthroughAlwaysSuccess = false") _breakthroughAlwaysSuccess = false;
                        else if (trimmed == "BreakthroughAlwaysSuccess = true") _breakthroughAlwaysSuccess = true;
                    }
                    _lastSettingsReadTime = System.IO.File.GetLastWriteTimeUtc(SettingsPath);
                }
            }
            catch
            {
                _bookWeightEnabled = true;
            }
        }

        // 检查 Settings.Lua 是否有更新（解决后端进程收不到 OnModSettingUpdate 的问题）
        private static void RefreshSettingsIfChanged()
        {
            try
            {
                if (SettingsPath != null && System.IO.File.Exists(SettingsPath))
                {
                    var lastWrite = System.IO.File.GetLastWriteTimeUtc(SettingsPath);
                    if (lastWrite > _lastSettingsReadTime)
                    {
                        Log($"热刷新: 文件修改时间 {lastWrite:T} > 上次读取 {_lastSettingsReadTime:T}");
                        _lastSettingsReadTime = lastWrite;
                        ReadHotReloadSettings();
                        Log($"设置已热刷新");
                    }
                }
                else
                {
                    Log($"热刷新跳过: SettingsPath={SettingsPath ?? "(null)"}");
                }
            }
            catch (System.Exception ex)
            {
                Log($"热刷新异常: {ex.Message}");
            }
        }

        public override void Initialize()
        {
            try
            {
                // 从 DLL 位置推算 Mod 根目录（用于读 Settings.Lua）
                _modRoot = GetModRootDirectory();

                // LogPath = 游戏根目录/Logs/GrantAllTraits.log
                // _modRoot 形如: 游戏根目录/Mod/我全都要
                // 往上两级 = 游戏根目录
                var gameRoot = Path.GetDirectoryName(Path.GetDirectoryName(_modRoot.TrimEnd('\\', '/')));
                LogPath = Path.Combine(gameRoot ?? ".", "Logs", "GrantAllTraits.log");

                Log("=== 我全都要 初始化 ===");
                Log($"ModRoot={_modRoot}, LogPath={LogPath}");
                ReadSettings();
                Log($"设置: trait={_enabled}");
                Log($"重量: 书籍={_bookWeightEnabled} 食物={_foodWeightEnabled} 药物茶酒={_medicineWeightEnabled} 装备={_equipmentWeightEnabled} 工具={_toolWeightEnabled} 材料={_materialWeightEnabled} 杂项={_miscWeightEnabled} 促织={_cricketWeightEnabled}");
                Log($"五行锁定混元={_lockFiveElementsToMix} 突破百分百={_breakthroughAlwaysSuccess}");
                Log($"自定义点数: 主属性={_customPointMainAttribute} 技艺={_customPointLifeSkill} 功法={_customPointCombatSkill} 特质={_customPointFeature}");

                // 检测是否在前端（Unity）环境
                bool isFrontend = false;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "Assembly-CSharp")
                    {
                        isFrontend = true;
                        break;
                    }
                }

                if (isFrontend)
                    PatchFrontend();
                else
                    PatchBackend();

                // 自定义开局点数（GameData.Shared.dll 在前后端都加载，所以在这统一 Patch）
                ApplyCustomPoints();

                // 记录 Settings.Lua 的初始修改时间，后续热刷新用
                RefreshSettingsIfChanged();

                Log("初始化完成");
            }
            catch (Exception ex)
            {
                Log($"初始化异常: {ex.Message}");
            }
        }

        // ===== 前端 Patch：特质选择界面 =====
        private void PatchFrontend()
        {
            Log("运行在前端模式");

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "Assembly-CSharp") continue;

                if (!_enabled)
                {
                    Log("特质功能已禁用");
                }
                else
                {
                    // Init Postfix
                    var itemType = asm.GetType("Game.Views.NewGame.NewGameFeatureItem");
                    if (itemType == null) { Log("NewGameFeatureItem 为空"); return; }

                    foreach (var m in itemType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name == "Init" && m.GetParameters().Length == 8)
                        {
                            var h = new Harmony("com.grantalltraits.mod");
                            h.Patch(m, postfix: new HarmonyMethod(
                                typeof(GrantAllTraitsPlugin).GetMethod("InitPostfix",
                                    BindingFlags.Public | BindingFlags.Static)));
                            Log("Init 修补成功");
                            break;
                        }
                    }

                    var updateMethod = AccessTools.Method(itemType, "UpdateDisabledStyle");
                    if (updateMethod != null)
                    {
                        var h2 = new Harmony("com.grantalltraits.style");
                        h2.Patch(updateMethod, postfix: new HarmonyMethod(
                            typeof(GrantAllTraitsPlugin).GetMethod("StylePostfix",
                                BindingFlags.Public | BindingFlags.Static)));
                        Log("UpdateDisabledStyle 修补成功");
                    }

                    var pageType = asm.GetType("Game.Views.NewGame.NewGameSubPageFeature");
                    if (pageType != null)
                    {
                        var uiMethod = pageType.GetMethod("UpdateUI",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (uiMethod != null)
                        {
                            var h3 = new Harmony("com.grantalltraits.ui");
                            h3.Patch(uiMethod, postfix: new HarmonyMethod(
                                typeof(GrantAllTraitsPlugin).GetMethod("UpdateUIPostfix",
                                    BindingFlags.Public | BindingFlags.Static)));
                            Log("UpdateUI 修补成功");
                        }
                    }

                    // 书籍重量：Patch 前端所有 get_Weight 方法
                    if (_bookWeightEnabled)
                    {
                        int patched = 0;
                        Type[] allTypes;
                        try { allTypes = asm.GetTypes(); } catch { allTypes = null; }
                        if (allTypes != null) foreach (var t in allTypes)
                        {
                            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                            {
                                if (m.Name.Contains("get_Weight") && (m.ReturnType == typeof(int) || m.ReturnType == typeof(float)))
                                {
                                    var h = new Harmony("com.grantalltraits.fw." + t.Name);
                                    h.Patch(m, postfix: new HarmonyMethod(
                                        typeof(GrantAllTraitsPlugin).GetMethod("WeightPostfixFloat",
                                            BindingFlags.Public | BindingFlags.Static)));
                                    patched++;
                                }
                            }
                        }
                        Log($"前端 get_Weight 修补: {patched} 个");
                    }

                    // Patch 前端新游戏面板的剩余点数显示（确保自定义点数生效）
                    // 无条件 Patch，即使当前是默认值也 Patch（用户可能在游戏内修改后生效）
                    var attrPanelType = asm.GetType("Game.Views.NewGame.NewGameSubPageCustomPresetAttribute");
                        if (attrPanelType != null)
                        {
                            // Patch 属性面板的刷新方法，让 GlobalConfig 的新值立即显示
                            var refreshMethods = new[] { "AddMainAttribute", "ResetMainAttributes", "RandomMainAttributes", "RefreshUI", "EnsureUiInitialized" };
                            foreach (var mn in refreshMethods)
                            {
                                var m = attrPanelType.GetMethod(mn,
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (m != null)
                                {
                                    var h = new Harmony("com.grantalltraits.points.refresh");
                                    h.Patch(m, postfix: new HarmonyMethod(
                                        typeof(GrantAllTraitsPlugin).GetMethod("RefreshPointDisplayPostfix",
                                            BindingFlags.Public | BindingFlags.Static)));
                                }
                            }
                            Log("属性面板点数刷新 Patch 成功");
                        }
                    }
                break;
            }
        }

        private void PatchBackend()
        {
            int patchedCount = 0;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "GameData") continue;

                // 分类定义：(类型名列表, 是否启用)
                string[][] categoryTypes = new[] {
                    new[] { "SkillBook" },
                    new[] { "Food" },
                    new[] { "Medicine", "TeaWine" },
                    new[] { "Weapon", "Armor", "Accessory", "Clothing", "Carrier" },
                    new[] { "CraftTool" },
                    new[] { "Material" },
                    new[] { "Misc" },
                    new[] { "Cricket" },
                };
                bool[] categoryEnabled = new[] {
                    _bookWeightEnabled,
                    _foodWeightEnabled,
                    _medicineWeightEnabled,
                    _equipmentWeightEnabled,
                    _toolWeightEnabled,
                    _materialWeightEnabled,
                    _miscWeightEnabled,
                    _cricketWeightEnabled,
                };
                string[] categoryNames = new[] {
                    "书籍", "食物", "药物茶酒", "装备", "工具", "材料", "杂项", "促织"
                };

                for (int i = 0; i < categoryTypes.Length; i++)
                {
                    if (!categoryEnabled[i]) continue;
                    foreach (var typeName in categoryTypes[i])
                    {
                        var t = asm.GetType("GameData.Domains.Item." + typeName);
                        if (t == null) { Log($"类型 {typeName} 未找到"); continue; }

                        var getBaseWeight = t.GetMethod("GetBaseWeight",
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        if (getBaseWeight != null && getBaseWeight.ReturnType == typeof(int))
                        {
                            var h = new Harmony("com.grantalltraits.bw." + typeName);
                            h.Patch(getBaseWeight, postfix: new HarmonyMethod(
                                typeof(GrantAllTraitsPlugin).GetMethod("WeightPostfix",
                                    BindingFlags.Public | BindingFlags.Static)));
                            patchedCount++;
                        }
                        else
                        {
                            Log($"类型 {typeName} 的 GetBaseWeight 未找到或返回类型不匹配");
                        }
                    }
                }

                break;
            }

            Log($"后端重量 Patch: {patchedCount} 个方法");

            // 五行真正锁定混元（Patch 后端实际比例计算）
            if (_lockFiveElementsToMix)
            {
                foreach (var asm2 in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm2.GetName().Name != "GameData") continue;
                    var charType = asm2.GetType("GameData.Domains.Character.Character");
                    if (charType == null) { Log("Character 类型未找到"); break; }
                    // Patch 私有方法 CalcNeiliProportionOfFiveElements
                    // 用 Prefix 跳过原方法，只对太吾本人返回全 20
                    var calcMethod = charType.GetMethod("CalcNeiliProportionOfFiveElements",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (calcMethod != null)
                    {
                        var h = new Harmony("com.grantalltraits.fiveelements");
                        h.Patch(calcMethod, postfix: new HarmonyMethod(
                            typeof(GrantAllTraitsPlugin).GetMethod("CalcNeiliProportionPostfix",
                                BindingFlags.Public | BindingFlags.Static)));
                        Log("五行锁定混元 Patch 成功");
            // 突破百分百成功
            if (_breakthroughAlwaysSuccess)
            {
                foreach (var asm3 in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm3.GetName().Name != "GameData.Shared") continue;
                    var plateType = asm3.GetType("GameData.Domains.Taiwu.SkillBreakPlate");
                    if (plateType == null) break;
                    var calcRate = plateType.GetMethod("CalcSuccessRate",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (calcRate != null)
                    {
                        var h4 = new Harmony("com.grantalltraits.breakthrough");
                        h4.Patch(calcRate, postfix: new HarmonyMethod(
                            typeof(GrantAllTraitsPlugin).GetMethod("CalcSuccessRatePostfix",
                                BindingFlags.Public | BindingFlags.Static)));
                        Log("突破百分百成功 Patch 成功");
                    }
                    break;
                }
            }
                    }
                    break;
                }
            }
        }

        // 自定义开局点数（GameData.Shared.dll 在前后端都加载，统一在此 Apply）
        private void ApplyCustomPoints()
        {
            Log($"ApplyCustomPoints: 尝试修改点数 主属性={_customPointMainAttribute} 技艺={_customPointLifeSkill} 功法={_customPointCombatSkill} 特质={_customPointFeature}");
            
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "GameData.Shared") continue;

                var configType = asm.GetType("GlobalConfig");
                if (configType == null) { Log("GlobalConfig 类型未找到"); break; }

                var instField = configType.GetField("Instance",
                    BindingFlags.Public | BindingFlags.Static);
                if (instField == null) { Log("Instance 字段未找到"); break; }
                var instance = instField.GetValue(null);
                if (instance == null) { Log("Instance 值为 null"); break; }

                // 动态读取单项上限，用于计算总上限
                short ReadField(string name)
                {
                    var f = configType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                    return (short)(f?.GetValue(instance) ?? 0);
                }

                short maxPerAttr = ReadField("CustomProtagonistMainAttributeMaxPoint");
                short maxPerSkill = ReadField("CustomProtagonistLifeSkillQualificationMaxPoint");
                short maxPerCombat = ReadField("CustomProtagonistCombatSkillQualificationMaxPoint");

                // 从 struct 大小动态计算属性数量
                int attrCount = 6, lifeCount = 16, combatCount = 14; // 默认值
                try
                {
                    var mainAttrType = asm.GetType("GameData.Domains.Character.MainAttributes");
                    if (mainAttrType != null)
                        attrCount = System.Runtime.InteropServices.Marshal.SizeOf(mainAttrType) / sizeof(short);
                    
                    var lifeType = asm.GetType("GameData.Domains.Character.LifeSkillShorts");
                    if (lifeType != null)
                        lifeCount = System.Runtime.InteropServices.Marshal.SizeOf(lifeType) / sizeof(short);
                    
                    var combatType = asm.GetType("GameData.Domains.Character.CombatSkillShorts");
                    if (combatType != null)
                        combatCount = System.Runtime.InteropServices.Marshal.SizeOf(combatType) / sizeof(short);
                }
                catch { }

                // Slider 已经在 Config.lua 中限制了范围，直接使用用户设置值
                Log($"ApplyCustomPoints: 动态计算 attrCount={attrCount} lifeCount={lifeCount} combatCount={combatCount}");

                void SetField(string name, int value)
                {
                    var f = configType.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                    if (f == null) { Log($"字段 {name} 未找到"); return; }
                    var oldVal = f.GetValue(instance);
                    f.SetValue(instance, (short)value);
                    Log($"字段 {name}: {oldVal} -> {(short)value}");
                }

                SetField("CustomProtagonistMainAttributeTotalPoint", _customPointMainAttribute);
                SetField("CustomProtagonistLifeSkillQualificationTotalPoint", _customPointLifeSkill);
                SetField("CustomProtagonistCombatSkillQualificationTotalPoint", _customPointCombatSkill);
                SetField("CustomProtagonistCharacterFeatureTotalPoint", _customPointFeature);

                break;
            }
        }

        // ===== Patch 方法 =====
        public static void InitPostfix(object __instance)
        {
            if (!_enabled) return;
            try
            {
                var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var t = __instance.GetType();
                t.GetField("_canSelect", f)?.SetValue(__instance, true);
                t.GetField("_maxPoints", f)?.SetValue(__instance, 10000);
                var fd = t.GetField("_featureData", f)?.GetValue(__instance);
                if (fd != null)
                    fd.GetType().GetField("PrerequisiteCost", f)?.SetValue(fd, (sbyte)0);
            }
            catch { }
        }

        public static void StylePostfix(object __instance)
        {
            try
            {
                var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var t = __instance.GetType();
                if (_enabled)
                {
                    t.GetField("_canSelect", f)?.SetValue(__instance, true);
                    t.GetField("_maxPoints", f)?.SetValue(__instance, 10000);
                }
                else
                {
                    t.GetField("_maxPoints", f)?.SetValue(__instance, 15);
                }
            }
            catch { }
        }

        public static void WeightPostfix(ref int __result, object __instance)
        {
            RefreshSettingsIfChanged();
            var typeName = __instance.GetType().Name;
            bool shouldZero;
            if (typeName == "SkillBook") shouldZero = _bookWeightEnabled;
            else if (typeName == "Food") shouldZero = _foodWeightEnabled;
            else if (typeName == "Medicine" || typeName == "TeaWine") shouldZero = _medicineWeightEnabled;
            else if (typeName == "Weapon" || typeName == "Armor" || typeName == "Accessory"
                  || typeName == "Clothing" || typeName == "Carrier") shouldZero = _equipmentWeightEnabled;
            else if (typeName == "CraftTool") shouldZero = _toolWeightEnabled;
            else if (typeName == "Material") shouldZero = _materialWeightEnabled;
            else if (typeName == "Misc") shouldZero = _miscWeightEnabled;
            else if (typeName == "Cricket") shouldZero = _cricketWeightEnabled;
            else shouldZero = false;

            if (shouldZero) __result = 0;
        }

        public static void CalcNeiliProportionPostfix(ref NeiliProportionOfFiveElements __result, object __instance)
        {
            if (!_lockFiveElementsToMix) return;
            
            try
            {
                var isTaiwu = (bool)__instance.GetType().GetMethod("IsTaiwu",
                    BindingFlags.Public | BindingFlags.Instance)?.Invoke(__instance, null);
                if (isTaiwu != true) return;
            }
            catch { return; }
            
            for (int i = 0; i < 5; i++)
                __result[i] = 20;
        }

        // 前端属性面板点数刷新 Postfix（修改属性后刷新显示文字）
        public static void RefreshPointDisplayPostfix(object __instance)
        {
            try
            {
                var t = __instance.GetType();
                var instance = typeof(GlobalConfig).GetField("Instance",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as GlobalConfig;
                if (instance == null) return;

                int total = instance.CustomProtagonistMainAttributeTotalPoint;
                var spentField = t.GetField("_spentPoints",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                int spent = (int)(spentField?.GetValue(__instance) ?? 0);
                int remain = total - spent;

                Log($"点数刷新: total={total} spent={spent} remain={remain}");

                var textField = t.GetField("attributePointText",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (textField != null)
                {
                    var text = textField.GetValue(__instance) as UnityEngine.UI.Text;
                    if (text != null)
                    {
                        string remainStr = remain.ToString().SetColor("brightblue");
                        text.text = $"{remainStr}/{total}";
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log($"RefreshPointDisplayPostfix 异常: {ex.Message}");
            }
        }

        public static void WeightPostfixFloat(ref float __result)
        {
            if (_bookWeightEnabled) __result = 0f;
        }

        public static void CalcSuccessRatePostfix(ref short __result)
        {
            if (_breakthroughAlwaysSuccess)
                __result = 100;
        }

        public static void UpdateUIPostfix(object __instance)
        {
            try
            {
                var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var t = __instance.GetType();
                if (_enabled)
                {
                    t.GetField("MaxPoints", f)?.SetValue(__instance, 10000);
                    var tf = t.GetField("remainingPointsText", f);
                    if (tf != null)
                    {
                        var to = tf.GetValue(__instance);
                        to?.GetType().GetProperty("text")?.SetValue(to, "无限");
                    }
                }
                else
                {
                    t.GetField("MaxPoints", f)?.SetValue(__instance, 30);
                }
            }
            catch { }
        }

        public static void Log(string msg)
        {
            try
            {
                Console.WriteLine($"[GrantAllTraits] {msg}");
                if (LogPath != null)
                    File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }

    }
}
