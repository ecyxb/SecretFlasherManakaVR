# 外部可视化配置工具

双击 **Mod 仓库根目录的 `打开VR配置.cmd`**。浏览器会自动打开配置窗口，工具根据本机记住的游戏目录读取 `BepInEx/config/com.codex.secretflashermanaka.vr.cfg`，不需要启动游戏、导入文件或连接头显。

源码、页面、默认参数、测试和启动脚本都随 Mod 仓库维护。构建产物位于仓库的 `artifacts/config-ui/publish`，本机游戏路径记在 `artifacts/config-ui/launcher.json`；二者都被 Git 忽略，不把机器路径与生成文件提交到仓库。

## 使用

1. 选择传统模式、三点模式或其他预留模式。
2. 从左侧进入对应分类，或通过顶部搜索查找中文名称、英文配置键和说明。
3. 修改开关、数值、下拉选项或文本。每项都能恢复默认，也支持本页/全部恢复默认。
4. 点击 **预览并保存**，核对旧值与新值，再保存到游戏配置。配置在下次启动游戏时生效，追踪眼位或比例修改后需要重新校准。

界面覆盖当前 **95 个可编辑配置项**，包括 12 个配置区和五套独立追踪参数。另有 **28 个程序固定参数** 只读展示；这些是 Mod 中的 `FixedConfigValue`，修改配置文件不能改变其行为。旧三点开关和比例参数仍可查看/编辑，但标记为「仅迁移」，新版本应使用追踪模式与独立配置区。

6/8/10/11 点参数区可以独立保存，但真实追踪器接入仍未实现。选择它们不会自动降级为三点。

## 保存、备份与草稿

- 游戏运行中可查看配置和编辑草稿。保存时要求关闭该游戏，防止游戏覆盖磁盘配置。
- 保存前校验数值、枚举和最小/最大值关系；通过内容版本检查阻止覆盖外部修改。
- 原文件自动备份到 `BepInEx/config/ManakaVRConfigBackups`，随后用原子替换写入新文件。保留注释、未知配置项、原有换行和 UTF-8 BOM。
- **备份历史** 展示最近 30 份备份，不自动删除更早文件。恢复先进入草稿，预览保存后才写入游戏。
- **重新读取** 会保留当前草稿并重新读取磁盘值，保存预览可以核对合并结果。
- 草稿按游戏路径保存在 `%LOCALAPPDATA%/ManakaVRConfigurator`，浏览器地址或端口变化后仍可恢复。关闭工具不等于保存游戏配置。
- 点击右上角 **退出工具** 可停止本地服务；仅关闭浏览器页签时，服务在 30 分钟没有访问后退出。重复启动同一游戏的配置工具会打开已有实例。

设备指示仅显示最近 5 秒内、且对应游戏仍在运行的状态。历史状态不会被显示为当前追踪结果。配置窗口不发送校准或其他游戏输入。

## 开发与安装

此版本使用本机已有的 .NET 10 / ASP.NET Core 运行时，不需要 npm、Python 或网页服务配置。若复制到没有该运行时的电脑，需要先安装对应运行时；当前发布方式不是自包含。

```powershell
scripts\config-ui.ps1 -GameRoot "E:\SecretFlasherManaka v1.1.3" -Action Launch
```

首次在新机器使用时，上面的命令记住游戏目录；以后直接双击仓库启动入口即可。`-Action Build` 用于重新构建；`-Action Install` 保留为可选的游戏目录分发方式。仓库入口使用 Windows 自带的 PowerShell。脚本不替换 VR 插件，也不修改现有游戏配置。页面资源全部嵌入程序，不依赖 CDN。工具通过 [Kestrel](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0) 绑定本机 `127.0.0.1` 的动态端口，使用会话地址和写请求校验；不监听局域网。

源码在 `src/ManakaVR.Configurator`；配置文件包含 BepInEx 元数据时，额外新增的配置项也会自动显示。`Data/defaults.cfg` 是当前版本的完整默认参数与元数据快照，支持缺少部分配置项或尚未生成配置文件的安装；更新 Mod 配置定义时应同步更新该快照与中文标签。

验证命令：

```powershell
dotnet run --project tests/Configurator.Tests/Configurator.Tests.csproj -c Release
node --check src/ManakaVR.Configurator/Web/app.js
```

测试覆盖全配置、同名字段的模式隔离、缺失配置补全、非法输入、冲突、游戏运行时保存保护、原子写入和备份恢复。浏览器的实际保存与恢复测试使用 `artifacts/config-ui/test-game` 下的隔离副本，不修改用户游戏参数。
