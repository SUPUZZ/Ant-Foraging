# 蚁境 · Ant Field

同一仓库内维护两个独立版本，各自的源码、资源、测试、构建配置和说明均在对应目录中。

- [网页版](web/README.md)：浏览器 3D 蚂蚁觅食仿真，以及经典 2D 页面。源码在 `web/src/`，构建产物在 `web/dist/`。
- [桌面版](desktop/README.md)：Windows 原生透明桌面蚁群、蚁窝剖面和系统托盘。源码在 `desktop/AntField.Desktop/` 与 `desktop/AntField.Core/`，发布产物在 `desktop/artifacts/`。

## 网页版

需要 Node.js 20.19+ 或 22.12+。在仓库根目录执行：

```powershell
cd web
npm ci
npm run dev
```

进入 `web/` 后，可运行 `npm test`、`npm run build`。发布时使用 `web/dist/`。

## 桌面版

需要 Windows 10/11 和 .NET 8 SDK。在仓库根目录执行：

```powershell
cd desktop
dotnet run --project AntField.Desktop
```

进入 `desktop/` 后，运行 `./build.ps1` 执行行为检查并打包，生成 `desktop/artifacts/windows-x64/AntField.exe`。正常构建不需要 Node.js 或网页开发服务器。

## 素材工具

桌面图标的独立生成工具在 `desktop/tools/`。桌面动画图集使用网页 3D 模型预渲染，其可选生成工具在 `web/tools/`；生成结果写入桌面版资源目录，详见两个版本的说明。已生成的图标与动画素材随源码保存。

Repository: [SUPUZZ/Ant-Foraging](https://github.com/SUPUZZ/Ant-Foraging)
