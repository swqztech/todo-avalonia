# todo-avalonia（墨迹清单）

本地待办，**Avalonia**，数据 `%APPDATA%\todo-avalonia\todos.json`。

```bash
dotnet build -c Release
dotnet run -c Release
```

绿色版 / 安装包：`bash packaging/build.sh`（框架依赖，需 .NET 8 运行时；默认同时打 zip 与 `dist/墨迹清单-avalonia-<版本>-win64-setup.exe`）
