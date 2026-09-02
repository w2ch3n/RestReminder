# 久坐休息提醒（RestReminder）

一个运行在 **Windows 10/11** 上的常驻托盘小工具：按自定义的工作时长提醒久坐用户休息，休息结束时再次提醒。

## 功能

- ✅ **自定义工作时长 / 休息时长 / 推迟时长**（分钟级，设置窗口或配置文件均可改）
- ✅ **锁屏 / 睡眠 / 注销时暂停计时**，解锁 / 唤醒后继续（基于系统会话与电源事件，不会把睡眠时间计入工作）
- ✅ **右下角提醒弹窗**：到点自动弹出，实时显示剩余休息时长倒计时
- ✅ **跳过休息**：点击后直接进入下一轮工作
- ✅ **推迟休息**：推迟可自定义的时长后再开始休息，期间可反复推迟
- ✅ **开机自启**：通过当前用户注册表 Run 键实现，无需管理员权限
- ✅ **系统托盘常驻**：悬浮显示当前状态；右键菜单可 立即休息 / 跳过休息 / 暂停计时 / 设置 / 退出
- ✅ **可选**：提示音、离开电脑（键盘鼠标空闲超 5 分钟）自动暂停
- ✅ 单实例运行、无主窗口、不抢焦点、支持多显示器（弹窗出现在鼠标所在屏幕的右下角）

## 技术栈

| 项 | 选择 | 理由 |
|---|---|---|
| 语言/框架 | C# + WinForms，目标 **.NET Framework 4.8** | Win10 预装 4.8，单 exe 零运行时依赖；`NotifyIcon` 托盘、`SystemEvents.SessionSwitch/PowerModeChanged` 锁屏睡眠事件、注册表自启全部原生支持 |
| 构建 | dotnet SDK（本仓库在 WSL2 上交叉编译出 exe） | `Microsoft.NETFramework.ReferenceAssemblies` 使 net48 可在 Linux 构建 |
| 配置 | `%APPDATA%\RestReminder\config.txt` | 纯文本 key=value，可手改 |
| 自启 | `HKCU\...\CurrentVersion\Run` | 免管理员权限 |

## 构建

环境：任意安装了 .NET SDK（8.0+）的机器，包括 WSL2/Linux（交叉编译）。

```bash
dotnet build -c Release RestReminder.csproj
# 产物：bin/Release/net48/RestReminder.exe
```

在 Windows 上构建结果一致；在 Linux/WSL 上构建的 exe 同样可在 Windows 10 上直接运行。

## 使用

1. 将 `RestReminder.exe` 复制到任意目录（如 `C:\Program Files\RestReminder\`），双击运行。
2. 程序启动后驻留托盘（图标为蓝色对勾）。首次启动会弹出气泡说明。
3. 右键托盘图标 → **设置...** 可修改时长、开关开机自启与提示音。
4. 到点后右下角弹出休息提醒：
   - **跳过休息**：立即开始新一轮工作；
   - **推迟 N 分钟**：休息延后，弹窗显示推迟倒计时，可反复推迟；
   - **✕**：隐藏弹窗（休息在后台继续，托盘可见剩余时间）。
5. 右键托盘图标 → **退出** 结束程序。

> 提示：锁屏/睡眠期间所有计时自动暂停；解锁后从暂停处继续。
> 开启"离开电脑时暂停"后，键盘鼠标空闲超过 5 分钟将暂停工作计时（可通过配置文件 `IdleMinutes` 调整）。

## 配置文件

位置：`%APPDATA%\RestReminder\config.txt`

```
WorkMinutes=45      # 工作时长（分钟）
RestMinutes=5       # 休息时长（分钟）
PostponeMinutes=5   # 推迟时长（分钟）
AutoStart=false     # 开机自启
PauseOnLockScreen=true
PauseOnIdle=false
IdleMinutes=5
SoundOnRemind=true
```

修改后需重启应用生效（设置窗口保存后即时生效）。

## 目录结构

```
├── RestReminder.csproj  # 项目文件（net48 + WinForms）
├── app.manifest         # DPI 感知 / Win10 兼容清单
├── Program.cs           # 入口 + ApplicationContext（事件串联）
├── AppConfig.cs         # 配置读写
├── TimerEngine.cs       # 计时状态机 + 锁屏/睡眠/空闲暂停
├── TrayIcon.cs          # 托盘图标与右键菜单
├── RestPopupForm.cs     # 右下角提醒弹窗（倒计时/跳过/推迟）
├── SettingsForm.cs      # 设置窗口
├── AutoStart.cs         # 开机自启（注册表）
├── IconFactory.cs       # 运行时绘制图标
├── .gitignore
├── LICENSE              # MIT
└── README.md
```

## 已知限制

- 需要 Windows 10（含 .NET Framework 4.8）或更新系统；
- 弹窗样式为自绘圆角卡片，非系统通知（避免依赖通知中心权限与 Win10 通知策略）。
