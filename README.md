# PCL2 CE 离线解锁启动器

给 [PCL2 CE（Plain Craft Launcher 社区版）](https://github.com/PCL-Community/PCL-CE) 用的本地补丁工具。

解除两处限制，让启动器回归离线可用：

- **正版验证**：首次启动不再强制登录正版账号，可创建离线 / 第三方登录档案
- **区域限制**：解除中国大陆地区的 Minecraft 下载 / 文件补全 / 版本安装 / 整合包安装封锁

> **本仓库不包含 PCL2 CE 的任何代码、二进制、API 密钥或美术资源。**
> 你需要自己从官方下载 PCL2 CE，本工具只对你本机已有的文件做修改。

---

## 快速开始

### 1. 编译工具

需要 Windows 自带的 .NET Framework 4.x（Win10/11 都有，不用装 SDK）：

```bat
build.bat
```

产出 `build\PCLCE-Patch.exe` 和 `build\PCLCE-Launcher.exe`。

### 2. 下载 PCL2 CE

从官方发布页下载，放进 `build\` 目录：

<https://github.com/PCL-Community/PCL-CE/releases>

文件名形如 `PCL2_CE_Release_x64.exe`。

### 3. 双击启动

双击 `build\PCLCE-Launcher.exe`。它会：

1. 检查 .NET 10 桌面运行时，缺了自动下载安装
2. 检查 CE 是否已解锁，没解锁自动打补丁（先备份原版为 `*.pclcebak`）
3. 启动 PCL2 CE

之后再打开会显示「已全部解锁，无需改动」，直接启动。

### 4. 只用补丁器（不要启动器）

```bat
PCLCE-Patch.exe                         自动找当前目录的 PCL2_CE_*.exe
PCLCE-Patch.exe PCL2_CE_Release_x64.exe 指定目标
PCLCE-Patch.exe PCL2_CE_Release_x64.exe --check     只检查不修改
PCLCE-Patch.exe PCL2_CE_Release_x64.exe --restore   从备份还原
```

---

## 原理

PCL2 CE 官方发布的是 **.NET 单文件 bundle**（v6.0，93 个内嵌文件，**未压缩**，框架依赖型）。
因为内嵌程序集是原样存储的，且补丁不改变任何长度，可以直接原地改字节：

> **不需要解包，也不需要重新打包。** 输出文件大小与官方完全一致（23,486,024 字节），
> bundle 布局与全部偏移保持有效，官方内置的凭据也全部保留。

按方法名定位（不是硬编码偏移），替换成常量桩：

| 方法 | 补丁后 | 作用 |
|---|---|---|
| `PCL.RestrictedFeaturePolicy::Check` | `return true` | 所有区域闸门放行 |
| `PCL.RestrictedFeaturePolicy::get_IsRestricted` | `return false` | 不再判定为受限地区 |
| `PCL.RestrictedFeaturePolicy::ThrowIfRestricted` | `return` | 永不抛异常 |
| `PCL.ProfileUi::CanCreateOtherProfile` | `return true` | 允许新建离线 / 第三方档案 |
| `PCL.Core.App.Localization.Lang::get_IsFeaturesUnrestricted` | `return true` | 区域/语言判定恒为不受限 |
| `PCL.Core.Minecraft.Profile.ProfileService::get_HasMicrosoftProfile` | `return true` | 跳过「必须登录正版」的启动前检查 |

### 关于 `get_HasMicrosoftProfile`

这是正版验证的关键。`McLaunchPrecheck` 里的代码是：

```
call  ProfileService::get_HasMicrosoftProfile
brtrue IL_048F          ; 有正版档案 -> 直接放行
```

把它恒置为 `true` 后，没有正版档案时也会走放行分支，不再弹强制登录。

> 注意：网上流传的 `M511.488256` 定位法（见于旧版 Patcher 项目）在 2.15.x 上**已经失效** ——
> 那串字符是 2.13/2.14 里 `CreateProfile` 的图标路径锚点，新版重构后不存在了。
> 本项目按方法签名定位，不依赖字符串锚点。

### IL 常量桩的坑

桩必须是 `prologue + nop… + ret`，**nop 填充在 ret 之前**。

`ret; nop; nop` 静态字节看着没问题，但 JIT 会抛 `InvalidProgramException` ——
.NET 不接受方法体末尾存在没有终止符的死代码块。

---

## 兼容性

- 目标：**PCL2 CE 2.15.x**（当前验证于 2.15.1 x64）
- 系统：64 位 Windows 10 / 11
- 补丁器按方法名定位，相邻版本通常仍然可用
- 若上游大改结构，补丁器会报「没有找到任何可修补的判定点」并退出，**不会产出损坏的文件**

未在 ARM64 版本上测试。

---

## 已知限制

1. **自动更新会覆盖补丁。** PCL CE 更新后会换回官方文件、补丁失效。
   用启动器的话，下次打开会自动重新打上。建议在设置里把更新方式改成「提示更新」或「不自动检查更新」。

2. **只改本机文件。** 不联网、不驻留、不修改注册表、不上传任何数据。

3. **补丁后不再受社区支持。** 遇到崩溃、报错请勿向 PCL CE 或 PCL2 官方仓库反馈。

---

## 免责声明

1. **不含上游资源**：本仓库只有补丁器与启动器的源码，不含任何属于 PCL2 / PCL2 CE 的
   源代码、二进制文件、API 密钥或美术资源。用户需自行从官方下载原版。

2. **仅本地修改**：本工具只对用户电脑上**已存在**的文件做修改，不提供、不代下载任何游戏资源。

3. **禁止分发产物**：**请勿分发使用本工具修补后的 PCL2 CE 二进制文件。**
   补丁后的官方包保留了官方内置的 OAuth / CurseForge 凭据，分发可能侵犯 PCL2 与 PCL CE 作者的权益。
   请仅分发本工具，让用户自行修补。

4. **非官方项目**：本项目与 PCL2 原作者（龙腾猫跃）及 PCL CE 社区开发组**无任何关联**，
   未获其授权或认可。

5. **风险自担**：本软件按「原样」提供，不提供任何担保。
   使用本工具产生的一切后果（包括但不限于软件崩溃、账号风险、数据丢失）由使用者自行承担。

6. **完全免费**：本项目开源免费，作者未从中获取任何收益。
   如果你是付费获得本工具的，请立即退款并举报商家。

---

## 致谢

思路参考了以下项目，特此致谢：

- [Octobersama/PCL-CE-Patcher](https://github.com/Octobersama/PCL-CE-Patcher) — MIT，bundle 穿透与 IL 修补的早期实现
- [Zyx-2012/PCL-CE-Patcher-Next](https://github.com/Zyx-2012/PCL-CE-Patcher-Next) — MIT，版本化补丁方案
- [StillMi/PCLCE-Unlock](https://github.com/StillMi/PCLCE-Unlock) — Apache-2.0，区域闸门的二进制补丁与 IL 合法性踩坑记录

本项目为独立实现：补丁器自带最小化 PE / CLI metadata 解析器（不依赖 AsmResolver 等第三方库），
编译只用到 Windows 自带的 `csc.exe`，产物零运行时依赖。

---

## 许可证

[MIT](LICENSE)

本项目**不包含** PCL2 / PCL2 CE 的代码，其版权归上游作者所有。
