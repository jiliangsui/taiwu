# GUIDE — 项目指南

> 本文档帮助你快速了解这个项目：架构设计、开发踩坑记录、DLL 反编译分析。
> 新上手时先看这里，避免重复踩坑。

---

## 目录

- [1. 项目架构](#1-项目架构)
- [2. 踩坑记录](#2-踩坑记录)
- [3. DLL 分析](#3-dll-分析)
  - [3.1 物品重量系统](#31-物品重量系统)
  - [3.2 功法突破系统](#32-功法突破系统)
  - [3.3 蛐蛐系统](#33-蛐蛐系统)
  - [3.4 五行属性系统](#34-五行属性系统)
- [附录：常用 analyzer 命令](#附录常用-analyzer-命令)

---

## 1. 项目架构

### 1.1 前后端分离架构

太吾绘卷是**前后端分离**架构：

- **前端**（Unity 进程）：加载 `Assembly-CSharp.dll`，负责 UI
- **后端**（GameData.Program 进程）：加载 `GameData.Shared.dll`，负责游戏逻辑

**关键区别：**

| 类型 | 前端可用 | 后端可用 |
|---|---|---|
| `Assembly-CSharp` 中的 UI 类型 | ✅ | ❌ |
| `GameData.Shared` 中的逻辑类型 | 部分 | ✅ |
| `SkillBook`、`ItemBase` 等物品类型 | ❌ | ✅ |
| `NewGameSubPageFeature` 等 UI 类型 | ✅ | ❌ |

**Config.lua 的插件声明：**

```lua
-- 前端插件（处理 UI）
FrontendPlugins = { [1] = "GrantAllTraitsMod.dll" }
-- 后端插件（处理游戏逻辑）
BackendPlugins = { [1] = "GrantAllTraitsMod.dll" }
```

同一个 DLL 可以同时注册为前端和后端插件。代码中通过检测 `Assembly-CSharp` 是否存在来判断运行环境。

### 1.2 程序集功能分布

| 程序集 | 文件路径 | 用途 |
|--------|---------|------|
| **GameData.dll** | `Backend/GameData.dll` | 后端游戏逻辑（物品类、角色、战斗等） |
| **GameData.Shared.dll** | `Backend/GameData.Shared.dll` | 共享工具/配置/Helper 类 |
| Assembly-CSharp.dll | `*_Data/Managed/Assembly-CSharp.dll` | 前端 UI |

> ⚠️ `SkillBook`、`ItemBase`、`Clothing` 等实际物品类型在 **GameData.dll** 中，**不在** GameData.Shared.dll 中。GameData.Shared.dll 只包含对应的 Helper 类（如 `SkillBookHelper`、`ClothingHelper`）。

---

## 2. 踩坑记录

### 2.1 PluginConfig 属性必填

`TaiwuRemakePlugin` 基类强制要求插件类上标记 `[PluginConfig]` 属性，否则加载时报错：

```
Plugin entry class must have the TaiwuModdingLib.Core.PluginConfig attribute.
```

**正确写法：**

```csharp
[PluginConfig("我全都要", "1.0.0.0", "jiliangsui")]
public class GrantAllTraitsPlugin : TaiwuRemakePlugin
```

### 2.2 MaxPoints 是字段不是属性

`NewGameSubPageFeature.MaxPoints` 是一个**公有字段**（public field），**不是属性**（property）。

错误写法（找不到）：
```csharp
var prop = pageType.GetProperty("MaxPoints"); // → NULL
```

正确写法：
```csharp
var field = pageType.GetField("MaxPoints");    // → OK
```

### 2.3 CheckCanSelect 是局部函数

`CheckCanSelect` 不是独立方法，而是 `OnFeatureClick` 方法内部的 **C# 局部函数**（local function）。Harmony 无法 Patch 局部函数。

正确做法：直接 Patch `OnFeatureClick` 方法，绕过内部检查。

### 2.4 重量相关方法的正确 Patch 目标

**错误做法：** 遍历所有 `GameData.Domains.Item` 类型，统一 Patch 它们的重量方法（会导致所有物品重量为 0）

**正确做法：** 按分类分别 Patch 各类型的 `GetBaseWeight()`，在 Postfix 中根据实例类型判断对应开关

```csharp
// 分类定义
string[][] categoryTypes = new[] {
    new[] { "SkillBook" },                          // 书籍
    new[] { "Food" },                               // 食物
    new[] { "Medicine", "TeaWine" },                // 药物茶酒
    new[] { "Weapon", "Armor", "Accessory",         // 装备
            "Clothing", "Carrier" },
    new[] { "CraftTool" },                          // 工具
    new[] { "Material" },                           // 材料
    new[] { "Misc" },                               // 杂项
    new[] { "Cricket" },                            // 促织
};

// Postfix 中根据类型判断
public static void WeightPostfix(ref int __result, object __instance)
{
    var typeName = __instance.GetType().Name;
    // 根据 typeName 判断对应开关...
    if (shouldZero) __result = 0;
}
```

`ItemTemplateHelper.GetBaseWeight` 是模板配置读取方法（static），会影响所有物品类型，不应直接 Patch 它。

### 2.5 Cost 是属性、PrerequisiteCost 是字段

在 `Config.ProtagonistFeatureItem` 中：
- `Cost` 是属性（有 `get_Cost` 方法，可用 Harmony Prefix Patch）
- `PrerequisiteCost` 是字段（需要通过反射直接 SetValue 修改，不能用 Harmony Patch）

### 2.6 开关需要读 Settings.Lua

游戏的设置开关存储在 `Mod/<Mod名称>/Settings.Lua` 中，格式：

```lua
return {
    Enabled = true,
    BookWeightEnabled = true,
}
```

`GetSetting` 不是框架内置方法（是某些 Mod 自己实现的）。正确做法是直接读取 `Settings.Lua` 文件，但必须注意：

**⚠️ 不能用 `content.Contains("Enabled = false")` 做匹配。**
当有多个开关时，`FoodWeightEnabled = false` 也会被匹配到，导致 `Enabled` 被误设为 `false`。

**正确做法：逐行精确匹配：**

```csharp
string[] lines = File.ReadAllText(path).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
foreach (var line in lines)
{
    var trimmed = line.Trim().TrimEnd(',');
    if (trimmed == "Enabled = false") _enabled = false;
    else if (trimmed == "Enabled = true") _enabled = true;
    else if (trimmed == "BookWeightEnabled = false") _bookWeightEnabled = false;
    // ... 每个字段单独匹配
}
```

`OnModSettingUpdate()` 回调在用户切换开关时触发，应在此方法中重新读取 Settings.Lua。

### 2.7 Harmony Patch 后需动态恢复

Patch 是永久性的——一旦 Harmony 安装了 Postfix，它就会一直拦截目标方法的调用。如果要实现开关效果，必须在 Postfix 内部检查开关状态，不能在开关关闭后"卸载"Patch。

```csharp
public static void WeightPostfix(ref int __result)
{
    if (_bookWeightEnabled) __result = 0;
    // 关闭时不做任何事，原始返回值保持不变
}
```

### 2.8 DeclaredOnly vs FlattenHierarchy

查找继承的方法时：
- `BindingFlags.DeclaredOnly` → 只找当前类声明的方法（不找继承的）
- `BindingFlags.FlattenHierarchy` → 包含继承的公有/保护方法

如果 `GetBaseWeight` 定义在 `ItemBase` 中，`SkillBook` 继承它但不重写，则需要在 `SkillBook` 上用 `FlattenHierarchy` 才能找到。

### 2.9 用 DLL 路径定位 Mod 资源，不依赖 BaseDirectory

`AppDomain.CurrentDomain.BaseDirectory` 在不同进程下指向不同位置：
- 前端 (Unity) → 游戏根目录
- **后端** (GameData.exe) → **`Backend/`** 目录

直接用 `BaseDirectory` 拼接 Mod 资源路径不可靠。改为多级回退定位：

```csharp
private static string GetModRootDirectory()
{
    // 方案一：从 DLL 自身位置推算
    try
    {
        var dllPath = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrEmpty(dllPath) && Path.IsPathRooted(dllPath))
        {
            var pluginsDir = Path.GetDirectoryName(dllPath);
            if (pluginsDir != null && pluginsDir.EndsWith("Plugins"))
                return Path.GetDirectoryName(pluginsDir);
        }
    }
    catch { }

    // 方案二：从 BaseDirectory 向上找 Mod/我全都要/Settings.Lua
    var dir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
    for (int i = 0; i < 3; i++)
    {
        var settingsPath = Path.Combine(dir, "Mod", "我全都要", "Settings.Lua");
        if (File.Exists(settingsPath))
            return Path.Combine(dir, "Mod", "我全都要");
        var parent = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
        if (parent == null || parent == dir) break;
        dir = parent;
    }

    // 方案三：扫描所有 Mod/ 子目录，找同时有 Settings.Lua 和 Plugins/GrantAllTraitsMod.dll 的
    var modDir = Path.Combine(dir, "Mod");
    if (Directory.Exists(modDir))
    {
        foreach (var subDir in Directory.GetDirectories(modDir))
        {
            if (File.Exists(Path.Combine(subDir, "Settings.Lua"))
                && File.Exists(Path.Combine(subDir, "Plugins", "GrantAllTraitsMod.dll")))
                return subDir;
        }
    }

    return dir; // 最后回退
}
```

注意：Unity 前端加载 DLL 时 `Assembly.Location` 可能为空（内存加载），所以方案二/三是必须的。

路径结构固定为：

```
Mod/{任意Mod名}/
├── Plugins/{DLL}.dll     ← DLL 所在位置
├── Settings.Lua          ← 同目录
├── Config.lua
└── cover.png
```

这样不管 Mod 在本地 `Mod/我全都要/` 还是创意工坊 `Mod/Workshop/xxxxx/`，都能正确找到资源。

### 2.10 后端进程收不到 OnModSettingUpdate，需要热刷新

太吾绘卷是前后端分离架构，Mod 的 `OnModSettingUpdate()` 回调**只在前端（Unity）进程**中触发。
后端（GameData.exe）进程中的静态字段永远停留在 `Initialize()` 时的值。

这意味着如果用户在游戏中修改了重量开关，后端进程不知道，重量不会变化。

**解决方案：每次 WeightPostfix 执行时检查 Settings.Lua 的修改时间。**

```csharp
private static System.DateTime _lastSettingsReadTime = System.DateTime.MinValue;

private static void RefreshSettingsIfChanged()
{
    try
    {
        var lastWrite = System.IO.File.GetLastWriteTimeUtc(SettingsPath);
        if (lastWrite > _lastSettingsReadTime)
        {
            _lastSettingsReadTime = lastWrite;
            ReadSettings();
            Log("设置已热刷新");
        }
    }
    catch { }
}

// 在 WeightPostfix 开头调用
public static void WeightPostfix(ref int __result, object __instance)
{
    RefreshSettingsIfChanged();  // ← 每次重量被读取时检查设置是否变更
    var typeName = __instance.GetType().Name;
    // ... 后续判断逻辑
}
```

原理：
1. `File.GetLastWriteTimeUtc` 极快，每次 `GetBaseWeight()` 调用时检查没有性能问题
2. 用户在前端改了设置 → Lua 文件写盘 → 修改时间更新
3. 下次任何物品调用 `GetBaseWeight()` → 发现文件更新 → 重读设置 → 开关立即生效

**哪些设置支持热变配：**

| 设置 | 热变配 | 说明 |
|------|-------|------|
| 各类重量开关 | ✅ | 游戏内切换后立即生效 |
| 五行锁定混元 | ✅ | 游戏内切换后立即生效 |
| 开局特质无限 | ❌ | **不支持热变配**，需重启生效 |
| 自定义开局点数 | ⚠️ **有限支持** | 在**主页**（新游戏界面）修改后立即生效；**进入游戏世界后**修改需重启 |

`OnModSettingUpdate()` 中不仅刷新重量/五行开关，还会重新读取并应用点数设置到 GlobalConfig，确保在主页修改点数后回到新游戏页面能看到新值。

`OnModSettingUpdate()` 和 `RefreshSettingsIfChanged()` 中只读取热变配字段，不触碰 Enabeld（特质）和 CustomPoint*（点数），避免意外覆盖。

### 2.11 前端 UI 修改需要引用 UnityEngine 程序集
### 2.12 五行锁定混元用 Postfix 改显示结果，不碰底层索引

**错误做法**：Patch `GetInnateFiveElementsType`（Prefix 强行返回 5）。
`GetInnateFiveElementsType` 的返回值会被当作数组索引使用（只能 0-4），返回 5 导致 `IndexOutOfRangeException`。

**正确做法**：Patch `NeiliProportionHelper.GetNeiliType`，这是一个计算并返回当前五行类型的方法，用 **Postfix** 覆盖返回值即可：

```csharp
public static void GetNeiliTypePostfix(ref sbyte __result)
{
    if (enabled) __result = 5; // FiveElementsType.Mix = 混元
}
```

注意：游戏原本的 `GetNeiliType` 在内力比例均分时（各属性值 18-22）也会返回 5（混元），所以 Postfix 只是强行锁定为混元，与游戏原有逻辑一致。

### 2.12 Config.lua 支持的设置类型

Mod 的 `Config.lua` 中 `DefaultSettings` 数组支持以下 `SettingType`：

| 类型 | 说明 | 额外属性 |
|------|------|---------|
| `"Toggle"` | 布尔开关，写入 Settings.Lua 为 `Key = true/false` | `DefaultValue` (bool) |
| `"ToggleGroup"` | 开关组 | — |
| `"InputField"` | 文本输入框，写入为 `Key = "值"` | `DefaultValue` (string) |
| `"Slider"` | 滑动条 | `DefaultValue`(int), `MinValue`(int), `MaxValue`(int) |
| `"Dropdown"` | 下拉选择 | `DefaultValue`(string), `Options`(数组) |

示例 - Slider 类型：
```lua
[11] = {
    SettingType = "Slider",
    Key = "CustomPointMainAttribute",
    DisplayName = "主属性总点数",
    Description = "默认300",
    DefaultValue = 300,
    MinValue = 300,
    MaxValue = 540,
}
```

注意：Settings.Lua 中 `Toggle` 写入 `Key = true/false`（无引号），而 `InputField` 写入 `Key = "值"`（带引号），`Slider` 写入 `Key = 数字`（无引号）。代码解析时需要根据设置类型正确处理引号。

### 2.13 Config.lua 顶层配置属性

`Config.lua` 的根表（与 `DefaultSettings` 同级）支持以下属性：

| 属性 | 类型 | 说明 |
|------|------|------|
| `Title` | string | Mod 名称 |
| `Author` | string | 作者 |
| `Version` | string | 版本号 |
| `Description` | string | 描述 |
| `Cover` | string | 封面图片路径 |
| `FrontendPlugins` | table | 前端（Unity）插件 DLL 列表 |
| `BackendPlugins` | table | 后端插件 DLL 列表 |
| `TagList` | table | 标签列表 |
| `DefaultSettings` | table | 设置项定义（上节详述） |
| `SettingGroups` | table | 设置在 UI 中的分组 |
| `Visibility` | bool | 控制整个 Mod 在列表中的显示/隐藏 |
| `ChangeConfig` | bool | 是否可修改配置 |
| `HasArchive` | bool | 是否有存档数据 |
| `NeedRestartWhenSettingChanged` | bool | 修改设置后是否需要重启 |
| `UpdateLogList` | table | 更新日志 |

注意：
- `Visibility` 控制的是**整个 Mod** 的显隐，不是单个设置项。游戏框架不支持按单个设置控制显示条件。
- `SettingGroups` 可以将设置分组显示在 UI 中。
- `NeedRestartWhenSettingChanged` 会影响 `OnModSettingUpdate()` 是否在用户修改设置后被调用。

### 2.14 创意工坊 Mod 路径

太吾绘卷创意工坊 Mod 的原始文件存储在 Steam 的 Workshop 目录下：

```
D:\SteamLibrary\steamapps\workshop\content\838350\<fileid>\
├── Config.lua
├── GrantAllTraitsMod.cs
├── GrantAllTraitsMod.csproj
├── Plugins/
│   └── GrantAllTraitsMod.dll
├── cover.png
└── ...
```

其中 `838350` 是太吾绘卷的 App ID，`<fileid>` 是创意工坊物品的唯一 ID。

游戏启动时会从 Workshop 路径同步到 `Mod/<Mod名>/` 目录。如果本地 `Mod/<Mod名>/Settings.Lua` 被用户修改过，同步**不会覆盖** Settings.Lua（保留用户设置）。但如果删掉本地 Mod 文件夹重新订阅，游戏会从 Workshop 重新复制，包括 Config.lua 的 `DefaultValue` 会用于生成新的 Settings.Lua。

**调试建议**：当创意工坊版出现设置不生效的问题时，可以对比 Workshop 路径和 `Mod/` 路径下的 Config.lua 版本号是否一致。Workshop 路径下的文件是上传时的原始版本。

### 2.15 上传更新创意工坊

第一次上传：
1. 在游戏 Mod 管理界面点击"上传到创意工坊"
2. 游戏会生成 `Source = 1` 和 `FileId`（创意工坊物品 ID）

后续更新：
- 方法一：在游戏 Mod 管理界面点击"更新到创意工坊"
- 方法二：手动修改 `Config.lua`，将 `Source` 改为 `1`，`FileId` 设为第一次上传时生成的 ID，再通过游戏上传

> ⚠️ 注意：如果 `Source = 0`，游戏会认为这是一个新的本地 Mod，每次上传都会创建新项目而非更新已有的。

创意工坊 Mod 在本地硬盘的存放路径：
```
D:\SteamLibrary\steamapps\workshop\content\838350\&lt;FileId&gt;\
```
其中 `838350` 是太吾绘卷的 App ID。

上传前请确保 `Config.lua` 中的 `Version` 已更新为最新版本号。

---

## 3. DLL 分析

### 3.1 物品重量系统

> 以下内容通过 analyzer 工具反编译游戏 DLL 获得。

### 3.1.1 重量方法继承链

```
GameData.Common.BaseGameDataObject
  └── GameData.Domains.Item.ItemBase (abstract)
       ├── GetBaseWeight()  ← abstract, 返回 int
       │    [各子类必须实现此方法来提供基础重量]
       │
       ├── GetWeight()  ← virtual, 返回 int
       │    [有默认实现: 调 GetBaseWeight() + 耐久度修正]
       │    也被 EquipmentBase 重写(增加装备特殊修正)
       │
       ├── EquipmentBase (abstract)
       │    ├── GetWeight()  ← override, 返回 int [装备特殊修正]
       │    ├── Weapon.GetBaseWeight() + Weapon.GetWeight()
       │    ├── Armor.GetBaseWeight()  + Armor.GetWeight()
       │    └── Accessory.GetBaseWeight()
       │
       ├── SkillBook.GetBaseWeight()  ★ 书籍
       ├── Clothing.GetBaseWeight()   ★ 衣物
       ├── Food.GetBaseWeight()       ★ 食物
       ├── CraftTool.GetBaseWeight()  ★ 工具
       ├── Material.GetBaseWeight()   ★ 材料
       ├── Medicine.GetBaseWeight()   ★ 药物
       ├── Misc.GetBaseWeight()       ★ 杂项
       ├── TeaWine.GetBaseWeight()    ★ 茶酒
       ├── Carrier.GetBaseWeight()    ★ 载具
       └── Cricket.GetBaseWeight()    ★ 促织
```

### 3.1.2 各物品类型的重量方法详情

所有物品类的重量方法都在 `GameData.dll` 中：

| 类型 | 完整路径 | GetBaseWeight | GetWeight |
|------|---------|--------------|----------|
| **ItemBase** | `GameData.Domains.Item.ItemBase` | abstract → int | virtual → int |
| **SkillBook** | `GameData.Domains.Item.SkillBook` | override → int | (继承) |
| **Clothing** | `GameData.Domains.Item.Clothing` | override → int | (继承) |
| **Food** | `GameData.Domains.Item.Food` | override → int | (继承) |
| **CraftTool** | `GameData.Domains.Item.CraftTool` | override → int | (继承) |
| **Material** | `GameData.Domains.Item.Material` | override → int | (继承) |
| **Medicine** | `GameData.Domains.Item.Medicine` | override → int | (继承) |
| **Misc** | `GameData.Domains.Item.Misc` | override → int | (继承) |
| **TeaWine** | `GameData.Domains.Item.TeaWine` | override → int | (继承) |
| **Carrier** | `GameData.Domains.Item.Carrier` | override → int | (继承) |
| **Cricket** | `GameData.Domains.Item.Cricket` | override → int | (继承) |
| **Weapon** | `GameData.Domains.Item.Weapon` | override → int | override → int |
| **Armor** | `GameData.Domains.Item.Armor` | override → int | override → int |
| **Accessory** | `GameData.Domains.Item.Accessory` | override → int | (继承) |
| **EquipmentBase** | `GameData.Domains.Item.EquipmentBase` | (来自子类) | override → int |
| **ItemTemplateHelper** | `GameData.Domains.Item.ItemTemplateHelper` | static → int | — |

### 3.1.3 重量计算的关键实现

#### ItemTemplateHelper.GetBaseWeight (静态方法)

**位置**: `GameData.Shared.dll` → `GameData.Domains.Item.ItemTemplateHelper`

**作用**: 从配置模板中读取物品的基础重量值。所有物品类型的重量配置都经过这里。

```csharp
public static int GetBaseWeight(sbyte itemType, short templateId)
{
    return itemType switch {
        0 => Weapon.Instance[templateId].BaseWeight,
        1 => Armor.Instance[templateId].BaseWeight,
        2 => Accessory.Instance[templateId].BaseWeight,
        3 => Clothing.Instance[templateId].BaseWeight,
        4 => Carrier.Instance[templateId].BaseWeight,
        5 => Material.Instance[templateId].BaseWeight,
        6 => CraftTool.Instance[templateId].BaseWeight,
        7 => Food.Instance[templateId].BaseWeight,
        8 => Medicine.Instance[templateId].BaseWeight,
        9 => TeaWine.Instance[templateId].BaseWeight,
        10 => SkillBook.Instance[templateId].BaseWeight,
        11 => Cricket.Instance[templateId].BaseWeight,
        12 => Misc.Instance[templateId].BaseWeight,
        _ => throw CreateItemTypeException(itemType),
    };
}
```

#### SkillBook.GetBaseWeight (实例方法)

**位置**: `GameData.dll` → `GameData.Domains.Item.SkillBook`

**作用**: SkillBook 实例重写 GetBaseWeight，从配置中读取该书籍的 BaseWeight。

```csharp
public override int GetBaseWeight()
{
    return Config.SkillBook.Instance[TemplateId].BaseWeight;
}
```

#### ItemBase.GetWeight (默认重量计算)

**位置**: `GameData.dll` → `GameData.Domains.Item.ItemBase`

默认的 GetWeight 实现考虑了耐久度对重量的影响。当耐久度降低时，重量可能会减少。

### 3.1.4 配置数据 (DefValue)

**位置**: `GameData.Shared.dll` → `DefValue`

| 属性/方法 | 说明 |
|----------|------|
| `ItemWeight` | 物品重量属性(getter) |
| `Weight` | 重量属性(getter) |

### 3.1.5 前端重量相关方法

前端 (Assembly-CSharp.dll) 中的重量方法主要用于 UI 显示：

| 类型 | 方法 | 说明 |
|------|------|------|
| `AreaItemKey` | `GetWeight()` | 获取物品重量(前端) |
| `CommonTableRowForItem` | `get_Weight()` | 表格行中的重量获取 |
| `CommonUtils` | `GetWeightString()` | 格式化重量显示 |
| `NumberFormatUtils` | `FormatItemWeight()` | 格式化重量数字 |
| `TooltipItemCommonArea` | `RefreshWeight()` | 刷新提示框中的重量显示 |

### 3.1.6 Patch 目标选择

| 优先级 | 目标 | 所在 DLL | 方法 | 说明 |
|--------|------|---------|------|------|
| ⭐ 推荐 | `SkillBook.GetBaseWeight()` | `GameData.dll` | 实例 override → int | 只影响书籍 |
| 备选 | `ItemTemplateHelper.GetBaseWeight()` | `GameData.Shared.dll` | 静态方法 → int | 会影响所有物品类型 |

### 3.1.7 Patch 代码示例

```csharp
var skillBookType = typeof(GameData.Domains.Item.SkillBook);
var getBaseWeight = skillBookType.GetMethod("GetBaseWeight",
    BindingFlags.Public | BindingFlags.Instance);
var harmony = new Harmony("com.example.weightpatch");
harmony.Patch(getBaseWeight, postfix: new HarmonyMethod(
    typeof(MyPatch).GetMethod("Postfix")));

public static void Postfix(ref int __result)
{
    if (IsEnabled) __result = 0;
}
```

### 3.2 功法突破系统

> 功法突破（Skill BreakPlate）是太吾绘卷的核心玩法之一。
> 以下内容通过 analyzer 反编译 Backend/GameData.Shared.dll 和 Backend/GameData.dll 获得。

#### 3.2.1 程序集分布

| 程序集 | 关键类型 |
|--------|---------|
| **GameData.Shared.dll** | `SkillBreakPlate`(突破棋盘), `SkillBreakPlateGrid`(格子), `SkillBreakPlateBonus`(奖励), `SkillBreakPlateIndex`/`SkillBreakPlateAxial`(坐标), `SkillBreakPlateConstants`, `SkillBreakPlateGridStateHelper`, `SkillBreakPlateBonusList`, `ESkillBreakPlateState`/`ESkillBreakPlateBonusType`, `Config.SkillBreakPlateItem`/`Config.SkillBreakPlate` |
| **GameData.dll** | `TaiwuDomain`(入口方法), `CombatSkill.CanBreakout()`(突破条件), `SkillBreakPlateList`(列表), `SkillBreakPlateBonusHelper`(奖励创建), `ExtraDomain`(存档数据) |

#### 3.2.2 核心类结构

```
SkillBreakPlate (突破棋盘)
├── 网格系统: Width × Height, 六边形网格(SkillBreakPlateAxial)
├── 坐标: SkillBreakPlateIndex (x, y)
├── 格子: SkillBreakPlateGrid
│   ├── Template → Config.SkillBreakGridTypeItem (格子类型配置)
│   └── State → ESkillBreakGridState (格子状态: 未激活/可选/已选/等等)
├── 步数系统:
│   ├── StepBase (基础步数)
│   ├── StepNormal (已消耗普通步数)
│   ├── StepGoneMad (已消耗走火步数)
│   └── StepTotal = StepNormal + StepGoneMad
├── 成功率:
│   ├── BaseSuccessRate (基础成功率, byte 0-100)
│   └── CalcSuccessRate() → int (包含各种加成后的实际成功率)
├── 路径: SelectPath (IReadOnlyList<SkillBreakPlateIndex>)
├── 状态: State → ESkillBreakPlateState
│   ├── Success (突破成功)
│   ├── Failed (突破失败)
│   └── Finished (突破结束)
└── 奖励: SkillBreakPlateBonus 列表

SkillBreakPlateBonus (突破奖励)
├── Type → ESkillBreakPlateBonusType (物品/阅历/人情/好友)
├── 物品类: ItemType + ItemTemplateId + MedicineEffectType
├── 阅历类: ExpLevel
├── 人情类: RelationKey + Favorability + FavorabilityType
├── 好友类: FriendAttainment
└── 效果计算: CalcAddPower, CalcReduceCostBreath, CalcMakeDamage 等

SkillBreakPlateConstants (常量)
├── ExpLevelValues / ExpEffectValues (阅历等级数值表)
├── FriendLevelValues / FriendAddPowerBase / FriendAddPowerDivisor (好友相关)
└── IsBonusItem() (判断是否为奖励物品)
```

#### 3.2.3 突破流程

```
TaiwuDomain.EnterSkillBreakPlate(skillId, selectedPages)
│
├── 1. 检查: CombatSkill.CanBreakout()
│   ├── HasReadOutlinePages (已读总纲)
│   ├── IsReadNormalPagesMeetConditionOfBreakout (已读5篇正篇)
│   └── !_revoked (未废弃)
│
├── 2. 检查: CombatSkillStateHelper.GetActiveOutlinePageType() != -1
│         && GetReadNormalPagesCount() == 5
│
├── 3. 创建/恢复 SkillBreakPlate 对象
│   ├── 如果 skillId 没有进行中的突破:
│   │   ├── 从 Config.SkillBreakPlate.Instance 获取配置
│   │   ├── 读取历史清除数据 (succeedCount, failCount)
│   │   └── new SkillBreakPlate(random, config, selectedPages, succeed, fail)
│   └── 如果已有进行中的突破: 直接继续
│
├── 4. 设置步数: StepBase = GetSkillBreakoutAvailableStepsCount()
├── 5. 设置传家宝: LegendaryBookState
├── 6. 计算成功率: BaseSuccessRate = CalcTaiwuBreakBaseSuccessRate()
│   ├── 基础值: 30 + (9 - grade) * 5
│   ├── 门派加成: Sect.CalcApprovingRate() >= 500 → +10
│   ├── 悟性加成: Min(maxMainAttr[5]/10, 30)
│   ├── 造诣加成: ConsummateLevel.AddBreakSuccessRate
│   ├── 建筑加成: BuildingBlockEffect (BreakOutSuccessRate)
│   ├── 难度加成: WorldCreation.InfluenceFactors[difficulty]
│   └── 命令 Bonus: _nextBreakoutSuccessRateBonus
│
├── 7. 保存: SetOrAddSkillBreakPlate()
└── 8. 转移前一阶段奖励: TransferNextBreakoutBonusToSkill()
```

#### 3.2.4 关键方法

**TaiwuDomain** (GameData.dll) — 突破入口：

| 方法 | 说明 |
|------|------|
| `EnterSkillBreakPlate(DataContext, short skillId, ushort selectedPages)` | 进入突破，创建/恢复棋盘 |
| `GetEnterSkillBreakPlateInfo(DataContext, short skillId)` | 获取突破界面所需信息(资质、消耗等) |
| `GetSkillBreakPlateSkillInfo(DataContext, short skillId)` | 获取功法在突破界面的展示数据 |
| `ChangeCombatSkillBreakPlate(DataContext, short skillId, ...)` | 修改突破中的操作 |
| `SetOrAddSkillBreakPlate(DataContext, short skillId, SkillBreakPlate)` | 保存突破进度 |
| `GetConflictRelationSkillBreakPlates()` | 获取冲突关系 |
| `CalcTaiwuBreakBaseSuccessRate(CombatSkillItem, ...)` | 计算基础成功率 |
| `CanBreakOut()` / `CalcCanBreakOut()` | 判断是否能突破 |

**SkillBreakPlate** (GameData.Shared.dll) — 突破棋盘逻辑：

| 方法 | 说明 |
|------|------|
| `GenerateBreakGrids()` | 生成突破棋盘格子 |
| `SelectBreak(SkillBreakPlateIndex)` | 选择格子前进 |
| `CanSelectBreak(SkillBreakPlateIndex)` | 判断格子是否可选 |
| `CalcSuccessRate(SkillBreakPlateIndex)` | 计算某个格子的成功率 |
| `CalcCostExp(SkillBreakPlateIndex)` | 计算消耗的历练 |
| `CalcCostStep(SkillBreakPlateIndex)` | 计算消耗的步数 |
| `GetNeighbors(SkillBreakPlateIndex)` | 获取相邻格子 |
| `GetBonus(SkillBreakPlateIndex)` | 获取格子奖励 |
| `SwapGrid(SkillBreakPlateIndex)` | 交换格子位置 |
| `UpdateState()` | 更新突破状态(成功/失败) |
| `MaybeSuccess()` | 判定是否突破成功 |

#### 3.2.5 数据存储

突破数据存储在后端的 `ExtraDomain` 中：

```
ExtraDomain
├── _skillBreakPlates (所有进行中的突破, keyed by skillId)
├── _combatSkillBreakPlateList (所有已完成的突破列表)
├── _combatSkillBreakPlateLastClearTimeList (清除时间记录)
└── _combatSkillBreakPlateLastForceBreakoutStepsCount (强制出关步数)
```

### 3.3 蛐蛐系统（抓蛐蛐 + 蛐蛐对战）

> 蛐蛐是太吾绘卷的特色系统。以下内容通过 analyzer 反编译获得。

#### 3.3.1 程序集分布

| 程序集 | 关键类型 |
|--------|---------|
| **GameData.dll** | `Cricket`(蛐蛐物品), `CricketBattler`(战斗属性), `CricketBattleSimulator`(战斗模拟器), `CricketGenerator`(生成器), `CricketBackendExtensions`, `CricketCombatPlan`(配队方案), `ItemDomain.CatchCricket`(捕捉), `ItemDomain.CreateCricket`(创建) |
| **GameData.Shared.dll** | `CricketData`(属性数据), `CricketCombatConfig`(战斗配置), `CricketCombineHelper`(合促织), `CricketHelper`, `CricketPreset`(预设), `CricketPlaceData`(场所), `CricketPlaceExtraData`(场所扩展), `CricketCollectionData`(收集册), `CricketSpecialConstants`(常量), `CricketWagerData`(赌注), `CricketSettlementResult`(结算), `CricketSpiritProperty`(灵属性), `Config.CricketItem`/`Config.CricketPlace` 等配置 |
| **GameData.Shared.dll** (事件) | `MonthlyEventCollection.AddCricketInDream`(入梦), `AddGiveBirthToCricket`(产卵), `AddAutumnCricketContest`(秋促织大赛), `AddRequestCricketBattle`(促织挑战), `AddCricketsAppeared`(促织出现) |

#### 3.3.2 蛐蛐捕捉

**入口**: `ItemDomain.CatchCricket(DataContext, short colorId, short partId, short singLevel, sbyte cricketPlaceId)`

**流程**:
```
CatchCricket(colorId, partId, singLevel, cricketPlaceId)
│
├── 1. 计算品质等级: b = Max(CricketParts[colorId].Level, CricketParts[partId].Level)
│
├── 2. 计算捕捉概率基数: num = 10 + singLevel - Min(b * 5, 40)
│
├── 3. 检查是否必定成功:
│   ├── CricketParts[colorId].MustSuccessLoud → 是则必定成功
│   └── singLevel >= (必成所需等级 ?? GlobalConfig.CatchCricketSuccessSingLevel)
│       └── 如果达到了，直接成功
│
├── 4. 概率判定:
│   ├── singLevel >= 80 → 使用 num 作为概率
│   └── singLevel < 80 → 使用 num/2 作为概率
│
└── 5. 成功时:
    ├── CreateCricket(context, colorId, partId) → 创建蛐蛐
    ├── 增加促织运势: CricketLuckPoint += CalcCatchLucky()
    ├── 增加遗惠: AddLegacyPoint(32, 100 + |CalcCatchLucky()| * 5)
    ├── 加入背包
    ├── 概率掉落耐久(获得蛐蛐罐) 或 概率额外获得同级-1的蛐蛐
    └── 记录生平/成就
```

**CreateCricket**:
```csharp
public ItemKey CreateCricket(DataContext context, short colorId, short partId)
{
    int num = GenerateNextItemId(context);
    Cricket cricket = new Cricket(context.Random, colorId, partId, num);
    AddElement_Crickets(num, cricket);
    Events.RaiseCricketCreated(context, cricket.GetItemKey());
    return cricket.GetItemKey();
}
```

#### 3.3.3 蛐蛐战斗属性

**CricketBattler** — 战斗单位的完整属性：

| 属性 | 说明 |
|------|------|
| `Level` | 等级（由品级决定） |
| `Vigor` | 气势（决定先手和 SP 伤害） |
| `Strength` | 角力 |
| `Bite` | 牙钳（基础伤害） |
| `Deadliness` | 暴击率 |
| `Damage` | 暴击时额外伤害 |
| `Cripple` | 致伤率（造成伤口） |
| `Defence` | 防御率 |
| `DamageReduce` | 减伤值 |
| `Counter` | 反击率 |
| `MaxHP` / `MaxSP` | 血量 / 气势上限 |
| `IsTrash` | 是否垃圾（杂色蛐蛐） |
| `IsFail` | 是否战败 |

**CricketData** — 蛐蛐伤势数据：

| 属性 | 说明 |
|------|------|
| `InjuryHp` | 血量伤势 |
| `InjurySp` | 气势伤势 |
| `InjuryVigor` | 气势伤势 |
| `InjuryStrength` | 角力伤势 |
| `InjuryBite` | 牙钳伤势 |
| `AgeStr` | 年龄阶段文字 |
| `NaturalDeath` | 是否自然死亡 |

#### 3.3.4 蛐蛐对战流程

**入口**: `ItemDomain.GmCmd_StartCricketCombat(context, enemyId)` 或通过事件触发

**战斗模拟器**: `CricketBattleSimulator`（静态方法）

```
GetBattleResult()
│
├── 1. CheckWinBeforeFight() → 战前判定
│   ├── 双方都是垃圾 → 50% 随机胜负
│   ├── 一方垃圾一方不是 → 非垃圾方胜
│   ├── 等级差 >= 6 → 高等级大概率直接胜
│   └── 否则 → 返回 -1（需要实际战斗）
│
├── 2. 循环战斗（直到一方 IsFail）:
│   ├── 比气势(Vigor)决定先手:
│   │   ├── A气势 > B气势 → A先手(80%概率), B的SP -= A的气势
│   │   ├── A气势 < B气势 → B先手(20%概率), A的SP -= B的气势
│   │   └── 相等 → 50%随机
│   │
│   ├── DoNormalAttack()
│   │   ├── 暴击判定: Deadliness 概率 → flag
│   │   ├── 防御判定: Defence 概率 → flag2
│   │   ├── 反击判定: Counter 概率 → canCounter
│   │   ├── 伤害 = Bite + (暴击 ? Damage : 0)
│   │   └── 触发防御时: 伤害 = Max(伤害 - DamageReduce, 0)
│   │
│   └── SettleNormalAttackDamage()
│       ├── 扣除 SP (暴击或反击时附加气势伤害)
│       ├── 暴击时耐久-1, 概率造成伤口
│       ├── 扣除 HP
│       └── 能反击 → 递归反击, 次数+1
│           └── 反击次数越高, 后续反击率递减(-5%/次)
│
└── 3. 返回结果: 0 = A胜, 1 = B胜
```

#### 3.3.5 蛐蛐生成与品级

**品级体系**:
- 颜色(colorId) + 部位(partId) 决定蛐蛐品级
- 合促织时: 品级 = Max(颜色等级, 部位等级)
- `CricketGenerator.Generator1/2/3` + `Generate` 决定具体属性生成
- `CricketParts.Item.Level` 决定品级数字（1-9）
- `ECricketPartsType` 区分是否为 Trash（垃圾/杂色）

**关键常量和配置**:
- `Config.CricketPlace` / `Config.CricketPlaceItem` — 捕捉场所配置
- `Config.CricketAffixes` — 蛐蛐词缀配置
- `Config.CricketSkill` — 蛐蛐技能
- `CricketSpecialConstants`:
  - `BaseWagerGrade` / `CalcWagerGradeRange` — 赌注等级
  - `GradeToPriceResource` / `GradeToPriceExp` — 品级与价格换算

#### 3.3.6 蛐蛐生命周期与事件

- **每月更新**: `ItemDomain.UpdateCrickets` / `Cricket.UpdateCricketAge` (年龄增长)
- **繁殖**: `MonthlyEventCollection.AddGiveBirthToCricket` (产卵繁殖)
- **入梦**: `MonthlyEventCollection.AddCricketInDream` (入梦抓蛐蛐)
- **秋促织大赛**: `MonthlyEventCollection.AddAutumnCricketContest` (秋季自动触发)
- **促织挑战**: `MonthlyEventCollection.AddRequestCricketBattle` (NPC 发起挑战)
- **寿命结束**: `MonthlyNotificationCollection.AddCricketEndLife` (寿命到期死亡)
- **DLC 蛐蛐化人**: `AddDLCTransmogrifyingCricketToHumanbeing` / `HumanbeingToCricket`
- **收集册**: `ExtraDomain.CricketCollectionData` / `ItemDomain.SetCricketRecord`

### 3.4 五行属性系统

> 五行（金木水火土）是太吾绘卷内力系统的核心。以下通过 analyzer 反编译获得。

#### 3.4.1 程序集分布

| 程序集 | 关键类型 |
|--------|---------|
| **GameData.Shared.dll** | `FiveElementsType`(五行类型枚举/sbyte), `NeiliProportionOfFiveElements`(内力五行比例), `SharedMethods.GetInnateFiveElementsType`(先天五行), `BodyPartType.TransferTo/FromFiveElementsType`(身体部位↔五行转换), `DefValue`(五行石/属性配置) |
| **GameData.dll** | `SpecialEffect.AffectedData.GetNeiliProportionOfFiveElements`(获取内力五行), `NeiliAllocation` 相关 (内力分配) |

#### 3.4.2 五行类型枚举 (FiveElementsType)

`sbyte` 常量定义在 `GameData.Domains.CombatSkill.FiveElementsType` 中（**静态类**，不是 enum）。
游戏中显示的名称（通过 `Config.NeiliType.Instance[type].Name` 配置）对应关系如下：

| 值 | 常量名 | 五行 | 游戏内名称 | NeiliType 配置 |
|----|--------|------|-----------|---------------|
| 0 | `Metal` | **金** | **金刚**·金刚伏魔 | ID 0 |
| 1 | `Wood` | **木** | **纯阳**·纯阳炽火 | ID 3 |
| 2 | `Water` | **水** | **玄阴**·玄阴冰寒 | ID 2 |
| 3 | `Fire` | **火** | **紫霞**·紫气东来 | ID 1 |
| 4 | `Earth` | **土** | **归元**·归元化蕴 | ID 4 |
| **5** | **`Mix`** | **混元** | **混元**·天人一体 | ID 5 |

> ⚠️ 注意：NeiliType 的 ID 与 FiveElementsType 值**不完全一致**（金刚=0 对应 金=0，但紫霞=1 对应 火=3，纯阳=3 对应 木=1）。  
> 查找游戏内名称时，需要通过 `Config.NeiliType.Instance[fiveElementsType]` 获取，NeiliType 数据中的 `FiveElements` 字节字段记录了其对应的五行类型。

**反编译源码**：
```csharp
public static class FiveElementsType
{
    public const sbyte Metal  = 0;
    public const sbyte Wood   = 1;
    public const sbyte Water  = 2;
    public const sbyte Fire   = 3;
    public const sbyte Earth  = 4;
    public const sbyte Mix    = 5;    // 混元
    public const int Count    = 5;

    // 五行生克关系表（各数组大小=5，索引对应 0金 1木 2水 3火 4土）
    public static readonly sbyte[] Countering = new sbyte[5] { 1, 4, 3, 0, 2 };
    public static readonly sbyte[] Countered  = new sbyte[5] { 3, 0, 4, 2, 1 };
    public static readonly sbyte[] Producing  = new sbyte[5] { 2, 3, 1, 4, 0 };
    public static readonly sbyte[] Produced   = new sbyte[5] { 4, 2, 0, 1, 3 };
}
```

**五行生克关系**（数组 `[金,木,水,火,土]`）：

| 关系 | 数组 | 含义 |
|------|------|------|
| 克它 `Countering` | `[木,土,火,金,水]` | 金克木、木克土、水克火、火克金、土克水 |
| 被克 `Countered` | `[火,金,土,水,木]` | 金被火克、木被金克、水被土克、火被水克、土被木克 |
| 它生 `Producing` | `[水,火,木,土,金]` | 金生水、木生火、水生木、火生土、土生金 |
| 被生 `Produced` | `[土,水,金,木,火]` | 金被土生、木被水生、水被金生、火被木生、土被火生 |

#### 3.4.3 先天五行 (Innate Five Elements)

**获取**: `SharedMethods.GetInnateFiveElementsType(sbyte birthMonth)`

```csharp
public static sbyte GetInnateFiveElementsType(sbyte birthMonth)
{
    return Config.Month.Instance[birthMonth].FiveElementsType;
}
```

即：根据角色的**出生月份**，从配置表 `Month.Instance` 中读取对应的五行类型。

#### 3.4.4 内力五行比例 (NeiliProportionOfFiveElements)

一个包含 5 个 `sbyte` 值的结构体，分别代表金木水火土的比例，总和必须为 100。

```
NeiliProportionOfFiveElements
├── Items[0..4]: sbyte (各元素比例 0-100)
├── Sum() / SumCheck(): 验证总和是否为 100
├── CheckValid(): 检查所有值在 0-100 且总和=100
├── Transfer(destType, transferType, amount): 内力转移
│   ├── 将 amount 点内力从其他属性转移到 destType
│   ├── 按 transferType 决定转移来源顺序
│   └── 确保单个属性不超过 100
├── GetTransferSources(transferType): 获取转移来源顺序表
├── GetTotal(): 返回总和（格式化）
└── Initialize(): 初始化
```

**创建默认比例** (`CustomProtagonistPresetItem.GenerateNeiliProportionByNeiliType`)：
```csharp
public static NeiliProportionOfFiveElements GenerateNeiliProportionByNeiliType(sbyte innateType)
{
    var result = default(NeiliProportionOfFiveElements);
    if (innateType >= 0 && innateType <= 4)
    {
        // 主属性 40，其余 15
        for (int i = 0; i < 5; i++)
            result[i] = (sbyte)((i == innateType) ? 40 : 15);
    }
    else
    {
        // 无属性时均匀分配
        for (int j = 0; j < 5; j++)
            result[j] = 20;
    }
    return result;
}
```

#### 3.4.5 配置数据 (DefValue)

| 配置项 | 说明 |
|--------|------|
| `FiveElements` | 五行属性集合 |
| `FiveElementsType` | 五行类型 |
| `EditCharBaseNeiliProportionOfFiveElements` | 角色创建基础内力五行比例 |
| `GetCharacterFiveElements` | 获取角色五行 |
| `FiveElementsChange` | 五行变化 |
| `FiveElementsStoneMetal/Wood/Water/Fire/Earth` | 五行石(金木水火土) |
| `FiveElementsStoneMetal0/Wood0/Water0/Fire0/Earth0` | 五行石 Lv0 |
| `FiveElementsStoneMetal1/Wood1/Water1/Fire1/Earth1` | 五行石 Lv1 |

#### 3.4.6 五行与内力分配

游戏中的内力分配系统 (NeiliAllocation) 与五行紧密相关：
- **分配类型**: 攻击/轻灵/防御/辅助 (`Attack/Agile/Defense/Assist`)
- **五行相生相克**: 内力转移时通过 `GetTransferSources(transferType)` 决定转移顺序（生克关系）
- **冲突检测**: `WorldStateDataHelper.DetectNeiliConflicting`
- **特殊功法影响**: 多个内功/绝技会改变内力分配（如混元无相功、童子血炼法等）
- **事件**: `ChangeNeiliAllocationAfterCombatBegin`(战斗后内力分配变更)、`NeiliAllocationChanged`(分配变更)

#### 3.4.7 Mod 实现：五行锁定混元

Patch 目标：`Character.CalcNeiliProportionOfFiveElements()`（GameData.dll 中 Character 类的私有实例方法）

```csharp
[HarmonyPostfix]
public static void CalcNeiliProportionPostfix(
    ref NeiliProportionOfFiveElements __result, object __instance)
{
    if (!_lockFiveElementsToMix) return;
    // 只对太吾本人生效
    if (!(bool)__instance.GetType()
        .GetMethod("IsTaiwu")?.Invoke(__instance, null)) return;
    
    for (int i = 0; i < 5; i++)
        __result[i] = 20;  // 5 个元素全设为 20
}
```

关键点：
- 使用 **Postfix**（非 Prefix），因为需要在原方法执行后修改返回值
- `__result` 用 `ref NeiliProportionOfFiveElements` 获取值类型 struct 的引用，直接修改其内部值
- 通过 `Character.IsTaiwu()` 只影响太吾本人，不影响 NPC
- `NeiliProportionOfFiveElements` 的 `fixed sbyte[5]` 内联数组通过 `ref return` 索引器 (`get_Item`) 修改

### 3.5 开局创建角色系统（属性/特质/点数）

> 开局创建角色时的属性分配、特质选择、点数系统。
> 以下通过 analyzer 反编译获得。

#### 3.5.1 程序集分布

| 程序集 | 关键类型 |
|--------|---------|
| **GameData.Shared.dll** | `CustomProtagonistPreset`(预设), `CustomProtagonistPresetItem`(单个预设项), `Config.ProtagonistFeatureItem`(特质配置), `GlobalConfig`(点数上限配置) |
| **Assembly-CSharp.dll** (前端) | `NewGameFeatureItem`(特质UI项), `NewGameSubPageFeature`(特质选择页), `NewGameSubPageCustomPresetAttribute`(属性分配), `NewGameSubPageCustomPresetQualification`(资质分配), `NewGameCustomPresetHelper`(预设管理), `NewGameCustomPresetPointItem`(点数UI), `CreationInfoHelper`(创建信息) |

#### 3.5.2 核心数据结构

**CustomProtagonistPresetItem** — 单个角色预设，包含完整创建数据：

```
CustomProtagonistPresetItem
├── MainAttributes (主属性，16 项)
├── LifeSkillQualifications (技艺资质)
├── CombatSkillQualifications (功法资质)
├── LifeSkillQualificationGrowthType (技艺成长类型)
├── CombatSkillQualificationGrowthType (功法成长类型)
├── NeiliProportion (内力五行比例)
└── SelectedFeatures (已选特质列表)
```

**点数系统**（通过 `GlobalConfig` 配置）：

| 配置项 | 说明 |
|--------|------|
| `CustomProtagonistMainAttributeTotalPoint` | 主属性总点数 |
| `CustomProtagonistMainAttributeMaxPoint` | 主属性单属性上限 |
| `CustomProtagonistMainAttributeDefaultPoint` | 主属性默认值 |
| `CustomProtagonistLifeSkillQualificationTotalPoint` | 技艺资质总点数 |
| `CustomProtagonistLifeSkillQualificationMaxPoint` | 技艺资质单属性上限 |
| `CustomProtagonistLifeSkillQualificationDefaultPoint` | 技艺资质默认值 |
| `CustomProtagonistCombatSkillQualificationTotalPoint` | 功法资质总点数 |
| `CustomProtagonistCombatSkillQualificationMaxPoint` | 功法资质单属性上限 |
| `CustomProtagonistCombatSkillQualificationDefaultPoint` | 功法资质默认值 |

**ProtagonistFeatureItem** — 特质配置项：

```csharp
public readonly short TemplateId;        // 唯一 ID
public readonly sbyte Type;              // 类型（0-2，对应特质分类）
public readonly sbyte Cost;              // 消耗点数
public readonly sbyte PrerequisiteCost;  // 前置消耗（条件）
public readonly string Name;             // 名称
public readonly string Desc;             // 描述
public readonly List<PropertyAndValueAndModifyType> PermanentBonus;  // 永久加成
```

#### 3.5.3 点数计算逻辑

```
CustomProtagonistPresetItem
├── MainAttributeRemainPoints = AttributeTotal - sum(MainAttributes)
│   （主属性剩余可分配点数）
├── LifeSkillQualificationRemainPoints = LifeSkillTotal - sum(LifeSkillQualifications)
│   （技艺资质剩余点数）
└── CombatSkillQualificationRemainPoints = CombatSkillTotal - sum(CombatSkillQualifications)
    （功法资质剩余点数）

ChangeMainAttribute(type, delta):
  deltaValue = Min(deltaValue, MainAttributeRemainPoints)  // 正数时不能超剩余
  MainAttributes[type] = Clamp(MainAttributes[type] + delta, 0, AttributeMax)
```

#### 3.5.4 特质选择逻辑

**选择条件** (`NewGameSubPageFeature.CheckCanSelect`)：

```csharp
private bool CheckCanSelect(ProtagonistFeatureItem feature)
{
    // 前置消耗：已在该类型上消耗的点数必须 >= 所需前置
    if (feature.PrerequisiteCost > _spentPoints[feature.Type]) return false;
    // 当前点数：剩余总点数必须 >= 特质消耗
    if (feature.Cost > MaxPoints - _spentPoints[3]) return false;
    return true;
}
```

其中 `_spentPoints` 数组：
- `[0]` = 第1类特质已消耗
- `[1]` = 第2类特质已消耗
- `[2]` = 第3类特质已消耗
- `[3]` = 总消耗（前三类之和）

`MaxPoints` 是 **公有字段**（不是属性），通过 `NewGameSubPageFeature.MaxPoints` 访问。

#### 3.5.5 Mod 实现要点

Patch `NewGameFeatureItem.Init` 和 `NewGameSubPageFeature.UpdateUI`：

```csharp
// Init Postfix: 强制可选中
_canSelect = true;
_maxPoints = 10000;
PrerequisiteCost = 0;

// UpdateUI Postfix: 显示"无限"
MaxPoints = 10000;
remainingPointsText.text = "无限";
```

注意：
- `MaxPoints` 是**字段**不是属性，不能用 `GetProperty` 只能用 `GetField`
- `CheckCanSelect` 是 `OnFeatureClick` 内部的**局部函数**，无法 Harmony Patch，只能通过 Patch `OnFeatureClick` 绕过

---

## 附录：常用 analyzer 命令

```bash
# 搜索重量相关方法
analyzer search Backend/GameData.dll "GetBaseWeight" --kind method

# 查看类型详情
analyzer get-type Backend/GameData.dll "GameData.Domains.Item.SkillBook"

# 反编译具体方法
analyzer decompile-method Backend/GameData.dll "GameData.Domains.Item.SkillBook" "GetBaseWeight"

# 列出物品命名空间下的所有类型
analyzer list-types Backend/GameData.dll --namespace GameData.Domains.Item

# 扫描文件夹中的程序集
analyzer scan-folder Backend/

# 整类型反编译
analyzer decompile-type Backend/GameData.Shared.dll "GameData.Domains.Item.ItemTemplateHelper"
```

---

*最后更新: 2025-07*
