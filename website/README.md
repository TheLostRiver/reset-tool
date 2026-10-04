# 霜序官网

https://frostbound.helsincy.com/

原生 HTML、CSS、JavaScript，部署在 Cloudflare Workers Static Assets。默认下载最新正式版，可选择历史版本；版本更新来自本项目 GitHub Releases。

```powershell
cd website
npm ci
npm run dev
```

本地地址：`http://127.0.0.1:4173`。

```powershell
npm run refresh
npm run deploy
```

`refresh` 使用已登录的 GitHub CLI 更新本地版本目录。线上 `/api/releases` 自动获取正式版，缓存五分钟，无法刷新时使用随网站发布的版本目录。新 Release 通常无需重新部署网站。

`/download/latest?format=exe` 直接下载最新程序；`/download/v0.3.0?format=exe` 等地址下载指定版本。支持 `exe`、`zip` 和 `checksums`，文件来源是本项目正式 Release 附件。

`wrangler.jsonc` 绑定 `frostbound.helsincy.com`。凭据沿用本机 Wrangler 登录，不写入仓库。
