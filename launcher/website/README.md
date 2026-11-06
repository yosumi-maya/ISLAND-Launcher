# ISLAND 官网

2026-11-01 按用户选择的 Product Design 第 2 张方案改版。原生 HTML / CSS / JS，无前端运行依赖。

首屏使用浅灰雾气、体素岛屿与大标题，突出 Windows 下载入口。下方为左右分栏的真实截图展示，支持浅色 / 玻璃切换与原图放大；功能、外观、下载要求、服务状态、FAQ、隐私页及中英文切换继续可用。

## 本地预览

```powershell
node launcher/website/build.mjs
node launcher/website/tools/serve.mjs
```

访问 http://127.0.0.1:4183/ 。构建输出为 `dist/`，未执行公开部署。

## 项目与下载

公开仓库：https://github.com/yosumi-maya/ISLAND-Launcher

下载配置位于 `site.config.js`，继续使用原有 v0.10.22 发布入口，本次只改版网页，不推断新版本状态。

## 资源与样式

- `styles.css`：原有通用样式与隐私页样式。
- `redesign.css`：本次官网布局、字体、配色和响应式样式。
- `assets/island-hero.webp`：本次生成的岛屿首屏插画，约 182 KiB；PNG 原稿保留于同目录，不进入构建产物。
- `assets/island-shore.webp`：展示区海岸背景，约 50 KiB。
- `assets/launcher-light.png`（1227 × 817）、`assets/launcher-glass.png`（1231 × 811）：所有者提供的真实截图，文件未修改。玻璃主题原图也用于外观展示。

网页不加载第三方字体、统计或脚本。下载跳转 GitHub，服务状态与隐私说明沿用原有内容。

## 检查

```powershell
node launcher/website/tests/verify.mjs
```

本次构建成功，29 项现有静态检查通过。桌面与手机浏览器验收通过，详细证据见 `design-qa.md`。设计与素材说明见 `DESIGN.md`。
