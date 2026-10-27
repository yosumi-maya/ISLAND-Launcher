# ISLAND 官网

2026-10-27 重新设计的中英文静态官网。白灰底色、岛屿青、原生 SVG 岛屿图案；包含功能、主题预览、材质交互示意、下载、运行要求、服务状态、FAQ 与隐私说明。

## 本地预览

```powershell
node launcher/website/build.mjs
node launcher/website/tools/serve.mjs
```

在浏览器访问 http://127.0.0.1:4183/。也可以直接打开 index.html。无 npm 依赖。

## 项目与下载

公开仓库：https://github.com/yosumi-maya/ISLAND-Launcher

配置集中于 site.config.js。发布入口固定指向 0.10.22 预览版标签，不依赖只包含稳定版本的 releases/latest。

## 接入项目所有者提供的截图

截图由项目所有者提供，本轮不继续生成或截取。三个截图位置默认空白并展示明确占位；不使用模拟图冒充实际界面。把提供的图片保存至 assets/，将 screenshots 中 forest、rose、glass 配置为例如 ./assets/launcher-light.png，再运行构建。图片路径仅接受 assets 下的本地 PNG、JPEG、WebP；图片加载失败时保留占位。

用户未提供全部主题截图时，只填写已有的项目即可。构建只带入 favicon 和显式配置的截图，不打包历史自动截图。

## 发布

构建输出位于 dist/，可部署至支持静态文件的托管服务。当前截图尚待补充，官网先提供本地预览；API 申请需要可公开访问的项目介绍与隐私页面，上线后把实际站点地址填入 siteUrl。没有虚构已上线地址。

## 检查

```powershell
node launcher/website/tests/verify.mjs
```

设计依据见 DESIGN.md。网页无第三方字体、统计、网站账号或表单；外部下载通过 GitHub，隐私说明列出启动器与外部服务的数据处理方式。
