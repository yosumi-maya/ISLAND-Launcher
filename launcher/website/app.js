(function () {
  "use strict";
  var config = window.ISLAND_SITE || {};
  var links = window.ISLAND_RESOLVE_LINKS(config);
  var language = new URLSearchParams(window.location.search).get("lang") === "en" ? "en" : "zh";
  var currentTheme = "forest";
  var currentMotion = "tilt";
  var reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  var originalText = new Map();
  var originalAttributes = new Map();
  var en = {
    "skip": "Skip to main content",
    "home": "ISLAND Launcher home",
    "menu": "Open navigation",
    "navigation": "Main navigation",
    "nav.features": "Features",
    "nav.look": "Appearance",
    "nav.download": "Download",
    "nav.privacy": "Privacy",
    "hero.preview": "Development preview",
    "hero.title1": "Your worlds,",
    "hero.title2": "at home.",
    "hero.description": "From game folders to individual instances. Keep your versions, mods and personal style together, ready for wherever you go next.",
    "hero.explore": "Explore ISLAND",
    "hero.platform": "Made for Windows x64 desktops",
    "showcase.label": "Your desktop. Your kind of island.",
    "showcase.aria": "Choose a screenshot theme",
    "showcase.zoom": "View the launcher screenshot at full size",
    "showcase.enlarge": "View full size",
    "showcase.caption": "Actual app screenshots · Fictional instances and a local demo player",
    "showcase.captionRight": "A native desktop app / One island, many themes",
    "theme.forest": "Forest",
    "theme.rose": "Rose",
    "theme.glass": "Glass",
    "features.overview": "Feature overview",
    "index.instances": "A place for every instance",
    "index.runtime": "Your runtime, in order",
    "index.appearance": "A little more you",
    "features.eyebrow": "A PLACE FOR EVERY WORLD",
    "features.title": "Your favorite worlds,\nright where you left them.",
    "features.description": "Bring multiple game folders into one place. Favorite the instances you return to, find them with search, and pick up your last adventure from a convenient continue button.",
    "features.a": "Import existing game folders and keep your directory structure",
    "features.b": "Set Java, memory and launch options per instance",
    "features.c": "Right-click for mods, saves, resource packs and shaders",
    "demo.label": "A little interaction. A lighter touch.",
    "demo.tag": "Interactive illustration",
    "demo.aria": "Card animation illustration",
    "demo.world1": "Forest Cottage",
    "demo.world2": "Island Journey",
    "demo.world3": "Star Workshop",
    "demo.ready": "Ready to explore",
    "motion.off": "Off",
    "motion.tilt": "Float + tilt",
    "motion.lift": "Lift",
    "motion.zoom": "Zoom",
    "motion.spring": "Spring",
    "motion.magnetic": "Magnetic",
    "motion.description.tilt": "Move your pointer over a card for a subtle lift and tilt.",
    "runtime.eyebrow": "YOUR RUNTIME, IN ORDER",
    "runtime.title": "The details,\non your terms.",
    "runtime.description": "Install games and loaders, browse Modrinth packs and manage a pausable download queue in the download center. Java detection results are remembered, and instances can keep their own runtime settings.",
    "runtime.limit": "Install 64-bit Java separately. Memory requirements and compatibility depend on the game and modpack.",
    "runtime.java": "Match and validate Java",
    "runtime.javaNote": "Saved results; automatic redetection for missing paths",
    "runtime.memory": "Automatic or manual memory",
    "runtime.memoryNote": "Global preferences and independent instance settings",
    "runtime.mods": "Mods, within easy reach",
    "runtime.modsNote": "Find, add, enable and disable installed mods",
    "runtime.visualNote": "Settings illustration · No browser device information is read",
    "appearance.eyebrow": "A CORNER OF YOUR DESKTOP",
    "appearance.title": "More than a color.\nA little more you.",
    "appearance.description": "Forest green, soft rose or translucent glass. Choose a background, tune the cards' opacity and make your launcher feel like your own.",
    "appearance.zoom": "View the appearance settings screenshot at full size",
    "appearance.alt": "ISLAND Launcher appearance settings, including preview, colors, opacity and materials",
    "appearance.caption": "Preview first. Save when you're happy. Make each area your own.",
    "appearance.presets": "Theme presets",
    "appearance.presetsNote": "Apply with one click, then fine-tune your own colors.",
    "appearance.materials": "Background materials",
    "appearance.materialsNote": "Solid color / image, Gaussian blur and liquid glass.",
    "appearance.skin": "Skin paper doll",
    "appearance.skinNote": "Double-layer skins, poses and a pointer-following 3D preview.",
    "appearance.note": "The desktop interface is currently presented in Chinese",
    "trust.eyebrow": "TRANSPARENCY GOES BEYOND APPEARANCE.",
    "trust.title": "Your account.\nYour authorization.",
    "trust.read": "Read the data & privacy notice",
    "trust.status": "Microsoft sign-in · Access preparation",
    "trust.statusNote": "Browser sign-in with PKCE and a device-code alternative are implemented; application API access is still awaiting confirmation. See release notes for availability and requirements.",
    "trust.a": "Authorization takes place on Microsoft's official website. The launcher does not collect Microsoft account passwords.",
    "trust.b": "Refresh credentials are encrypted locally using Windows DPAPI.",
    "trust.c": "This introduction website has no sign-in form, advertising or tracking scripts.",
    "download.eyebrow": "THE NEXT ADVENTURE STARTS HERE",
    "download.title": "Make room for exploration.",
    "download.runtime": ".NET runtime included in the portable package",
    "download.java": "Install 64-bit Java separately",
    "download.curseforge": "CurseForge modpack and mod downloads are not available yet: API access has not been requested.",
    "download.release": "GitHub releases",
    "faq.title": "Before you\nhead out.",
    "faq.q1": "Is this the official Minecraft launcher?",
    "faq.a1": "No. ISLAND Launcher is an independently developed, unofficial tool. It is not affiliated with or endorsed by Mojang Studios or Microsoft.",
    "faq.q2": "Can I sign in with my Microsoft account?",
    "faq.a2": "Browser authorization-code sign-in with PKCE and a device-code alternative are implemented, but this application's Minecraft API access is still awaiting confirmation. Online sign-in requires an approved application and an account with access to Minecraft: Java Edition. Refer to release notes for availability.",
    "faq.q3": "Do I need to install Java and .NET?",
    "faq.a3": "The portable package includes the .NET runtime. Install the game's required 64-bit Java separately; the launcher detects and remembers installations and validates the selected runtime before launch.",
    "faq.q4": "Can I import existing game directories?",
    "faq.a4": "You can add existing .minecraft folders and browse their versions, instances and saves. Modpack and loader compatibility depends on their metadata, dependencies and Java requirements; not every pack is guaranteed to work directly.",
    "faq.q5": "Why is downloading not available yet?",
    "faq.a5": "Downloads have not opened yet. Once the project is published, download links will lead to GitHub Releases. This website does not host launcher binaries.",
    "footer.note": "An independent launcher for Minecraft: Java Edition.\nMay every launch lead to a good journey.",
    "footer.contact": "Project feedback",
    "footer.independent": "An independent, unofficial Minecraft: Java Edition tool.",
    "dialog.label": "Full-size application screenshot",
    "dialog.close": "Close the full-size screenshot",
    "dialog.hint": "Press Esc or click outside to close",
    "privacy.home": "← Back to home",
    "privacy.title": "Data & privacy notice",
    "privacy.updated": "Updated October 25, 2026",
    "privacy.version": "Launcher version",
    "privacy.lead": "How the introduction website and Windows launcher handle information, and which actions contact external services.",
    "privacy.contents": "On this page",
    "privacy.nav1": "01 Introduction website",
    "privacy.nav2": "02 Local information",
    "privacy.nav3": "03 Accounts & skins",
    "privacy.nav4": "04 External connections",
    "privacy.nav5": "05 Your choices",
    "privacy.h1": "Introduction website",
    "privacy.p1a": "This is a static introduction website. It has no account registration, Microsoft sign-in form or file upload. The website code includes no advertising, analytics, third-party fonts or tracking scripts, and does not use cookies or local storage to track visitors. Language selection is retained through a URL parameter.",
    "privacy.p1b": "Browsing still generates normal network requests. The website hosting service may process IP addresses, request times and other network information according to its configuration and privacy policy. This notice does not claim that the hosting service produces no logs.",
    "privacy.p1c": "Once downloads open, download links will lead to GitHub. GitHub's own terms and privacy policy apply when you visit GitHub.",
    "privacy.h2": "Information stored locally by the launcher",
    "privacy.p2a": "The launcher stores game directories, instance preferences, favorites, play history, Java detection records, launch options and appearance settings on your computer. Account information may include the player's name, UUID, skin image and the Microsoft application ID in use.",
    "privacy.p2b": "The default configuration folder is %LOCALAPPDATA%\\IslandLauncher; an independent folder can also be supplied through launch arguments. Java detection results are stored there in java-runtimes.json. Games, mods and saves are kept in the game directories you select.",
    "privacy.p2c": "The launcher saves launch diagnostics and game output logs. Games and mods may write additional information. Review logs before sharing them publicly.",
    "privacy.h3": "Microsoft accounts and skins",
    "privacy.p3a": "Microsoft sign-in uses the system browser with an authorization code and PKCE by default, with device-code sign-in available as an alternative. A loopback callback is opened only during browser authorization; no client secret is used. Passwords are entered on Microsoft's official website, never collected by the launcher.",
    "privacy.p3b": "Authorization contacts Microsoft, Xbox and Minecraft services to obtain a game session and player profile. Refresh credentials are protected with DPAPI for the current Windows user before being stored. Game session credentials are used for authentication and launch.",
    "privacy.p3c": "Syncing an online skin reads the official player profile. Skin uploads or default-skin restoration that you explicitly request contact Minecraft's official API and change your account's skin. Selecting a skin for local preview does not automatically upload it.",
    "privacy.p3d": "This application's Minecraft API access is still awaiting confirmation. Refer to future release notes for the availability of online sign-in and official skin operations.",
    "privacy.h4": "Downloads and external connections",
    "privacy.p4a": "Downloading games, loaders, dependencies or skins contacts their distribution services and content delivery networks. Depending on your choices, the launcher may also contact mirrors and modpack services. Those services may process network requests under their own policies.",
    "privacy.p4b": "Games, mods and third-party modpacks have their own network behavior and privacy policies; this notice does not cover all their behavior. Website illustrations use fictional instances. Actual application screenshots are supplied by the project owner and reviewed for private information before publication.",
    "privacy.h5": "Your choices and project feedback",
    "privacy.p5a": "You can remove accounts, change instance directories and adjust preferences in the launcher. You can also clean up local configuration after making a backup. Distinguish launcher settings from game saves and preserve any data you need.",
    "privacy.p5b": "Public feedback channels will be supplied with the GitHub project. Until download and feedback URLs are available, the website clearly marks these links as unavailable and does not redirect to another project or a fabricated address.",
    "privacy.p5c": "ISLAND Launcher is unofficial and is not affiliated with, partnered with or endorsed by Mojang Studios or Microsoft. The project license and third-party component notices accompany the release package.",
    "privacy.contact": "Visit project feedback"
  };
  Object.assign(en, {
  "brand.subtitle": "Island Launcher",
  "nav.faq": "FAQ",
  "nav.download": "Get ISLAND",
  "hero.title": "Your next adventure\nstarts here.",
  "hero.description": "The worlds you return to. The mods you love. The way you like things.\nBring them together in your own little island.",
  "hero.preview": "Windows development preview",
  "hero.art": "A place for every world.",
  "hero.platform": "Windows x64 · Portable · MIT open source",
  "hero.explore": "Take a look inside",
  "showcase.title": "Familiar worlds. A fresh way in.",
  "showcase.description": "A different palette. The same familiar home.",
  "showcase.pending": "Application screenshots coming soon",
  "showcase.pendingNote": "This space will show the real launcher interface.",
  "theme.forest": "Tundra teal",
  "theme.rose": "Rose",
  "theme.glass": "Island night",
  "showcase.caption": "ISLAND Launcher / Windows desktop application",
  "showcase.captionRight": "Your instances. Your style.",
  "features.title": "Less organizing.\nMore playing.",
  "features.description": "From choosing a game version to finding the right content,\nkeep the preparations in one place.",
  "features.demo": "Instance management illustration, not an application screenshot",
  "features.demoTitle": "My worlds",
  "features.demoNote": "Illustration",
  "features.demoBottom": "Pick up your favorite adventure next time.",
  "world.forest": "Forest cottage",
  "world.island": "Island journey",
  "world.favorite": "Favorite",
  "world.ready": "Ready to play",
  "features.instances": "A place for each world",
  "features.instancesBody": "Add existing game folders, favorite and search instances, and configure Java, memory and launch options per instance.",
  "features.downloads": "Find what you want to play",
  "features.downloadsBody": "Search Modrinth modpacks, mods, resource packs and shaders. Select a target instance and browse compatible releases.",
  "features.queue": "Keep track of downloads",
  "features.queueBody": "Queue game and content downloads with pause, resume, cancel and retry controls. Open task logs when you need details.",
  "features.runtime": "Keep the runtime in order",
  "features.runtimeBody": "Detect and remember installed Java runtimes, allocate memory per game, and check required files before launch.",
  "appearance.title": "Make it feel\nlike your desktop.",
  "appearance.body": "Light or dark, solid, frosted or glass. Customize backgrounds, opacity and card motion. The recent-world card can have its own picture and text color.",
  "appearance.materials": "Background material illustration",
  "material.solid": "Solid",
  "material.frosted": "Frosted",
  "material.glass": "Glass",
  "appearance.hint": "Switch materials to see the card change. This is a web illustration.",
  "appearance.mapLabel": "Last world played",
  "appearance.mapAction": "Ready for a little more exploring.",
  "appearance.demo": "Material illustration",
  "download.preview": "Development preview",
  "download.title": "A way into\nyour next adventure.",
  "download.body": "Download the Windows portable package, extract it and run ISLAND Launcher.",
  "download.source": "View source code",
  "download.requirements": "Before you begin",
  "download.system": "System",
  "download.runtime": "Runtime",
  "download.runtimeBody": ".NET runtime included in the portable package",
  "download.java": "Install the 64-bit Java required by your game",
  "download.account": "Online services",
  "download.accountBody": "Requires ownership of Java Edition and approved application access",
  "download.notes": "Release notes and file checksums",
  "services.title": "Know what is available.",
  "services.body": "Access approval affects some third-party features.\nRelease notes will reflect their actual availability.",
  "services.available": "Integrated",
  "services.pending": "Access pending confirmation",
  "services.notYet": "Not available yet",
  "services.modrinth": "Search and download modpacks, mods, resource packs and shaders. Installation depends on Minecraft version, loader and content compatibility.",
  "services.microsoft": "System-browser PKCE and alternative device-code flows are implemented. Minecraft Services API access is pending confirmation; online sign-in and official skin operations are not promised yet.",
  "services.curseforge": "API access has not yet been applied for. CurseForge online downloads are unavailable. Modrinth and supported local content can be used now.",
  "faq.title": "Before you set off",
  "faq.q3": "How do I install mods, resource packs and shaders?",
  "faq.a3": "In the download center, choose a content type and target instance, search Modrinth and select a compatible release. Mods need the corresponding loader. Shaders usually require Iris or OptiFine. Enable downloaded resource packs and shaders in game.",
  "faq.q5": "Where do I find downloads, updates and support?",
  "faq.a5": "Downloads and release notes are provided through GitHub Releases, and issues can be reported in the project repository. This website has no account sign-in and does not host Minecraft game files.",
  "footer.note": "May every launch lead to a good journey.",
  "footer.independent": "An unofficial Minecraft: Java Edition tool. Minecraft is a trademark of Mojang Studios.",
  "privacy.updated": "Updated October 27, 2026",
  "privacy.p1c": "Download links lead to GitHub Releases. GitHub’s own terms and privacy policy apply when you visit GitHub.",
  "privacy.p5b": "Use the public GitHub Issues page for project feedback. Do not include account credentials or private game data in a public issue."
});
  var dynamic = {
    zh: {
      download: "前往 GitHub 下载", unavailable: "下载即将开放",
      downloadNote: "下载通过 GitHub Releases 提供，请以发布页的文件和说明为准。",
      unavailableNote: "下载尚未开放。发布后将通过 GitHub Releases 提供。",
      linkUnavailable: "项目发布后开放", motionReduced: "已遵循系统的减少动态效果偏好。",
      screenshotAlt: "ISLAND Launcher 实际界面",
      faqQ: "下载和更新在哪里提供？", faqA: "下载与版本更新通过 GitHub Releases 提供，请查看发布页的说明、版本号和适用条件。此网站本身不托管启动器安装包。",
      motion: {
        off: "关闭卡片动效，保持静止。",
        tilt: "把鼠标移到卡片上，感受轻微的抬升与倾斜。",
        lift: "卡片平稳上浮，带有一层柔和阴影。",
        zoom: "从中心轻轻放大，不发生位移或倾斜。",
        spring: "弹起，再轻轻回落。移动鼠标试试。",
        magnetic: "卡片会向鼠标方向轻移，像一枚小磁铁。"
      }
    },
    en: {
      download: "Download on GitHub", unavailable: "Downloads coming soon",
      downloadNote: "Downloads are provided through GitHub Releases. Check the release page for files and instructions.",
      unavailableNote: "Downloads have not opened yet. Releases will be available on GitHub.",
      linkUnavailable: "Available when the project is published", motionReduced: "Your system's reduced-motion preference is respected.",
      screenshotAlt: "Actual ISLAND Launcher interface",
      faqQ: "Where are downloads and updates provided?", faqA: "Downloads and version updates are provided through GitHub Releases. Check its release notes, version and requirements. This website does not host launcher binaries.",
      motion: {
        off: "Card animation is off. The cards stay still.",
        tilt: "Move your pointer over a card for a subtle lift and tilt.",
        lift: "A smooth upward lift with a soft shadow.",
        zoom: "A gentle zoom from the center, without movement or tilt.",
        spring: "A little bounce before settling. Move your pointer over a card.",
        magnetic: "Cards drift gently toward your pointer, like little magnets."
      }
    }
  };

  document.querySelectorAll("[data-i18n]").forEach(function (element) {
    var copy = element.cloneNode(true);
    copy.querySelectorAll("br").forEach(function (br) { br.replaceWith("\n"); });
    originalText.set(element, copy.textContent);
  });
  ["aria-label", "alt"].forEach(function (attribute) {
    var marker = attribute === "alt" ? "data-i18n-alt" : "data-i18n-aria";
    document.querySelectorAll("[" + marker + "]").forEach(function (element) {
      originalAttributes.set(element, { marker: marker, attribute: attribute, text: element.getAttribute(attribute) });
    });
  });
  document.querySelectorAll("[data-version]").forEach(function (element) { element.textContent = String(config.version || "0.10.22").slice(0, 30); });
  function wireLinks(selector, value) {
    document.querySelectorAll(selector).forEach(function (link) {
      if (value) {
        link.href = value; link.target = "_blank"; link.rel = "noopener noreferrer";
        link.removeAttribute("aria-disabled"); link.removeAttribute("tabindex"); link.classList.remove("unavailable");
      } else {
        link.removeAttribute("href"); link.setAttribute("aria-disabled", "true"); link.setAttribute("tabindex", "-1"); link.classList.add("unavailable");
        link.addEventListener("click", function (event) { event.preventDefault(); });
      }
    });
  }
  wireLinks("[data-github]", links.repository);
  wireLinks("[data-download]", links.download);
  wireLinks("[data-releases]", links.releases);
  wireLinks("[data-contact]", links.contact);
  function updateMotionDescription() {
    var description = document.getElementById("motion-description");
    if (description) description.textContent = dynamic[language].motion[currentMotion] + (reducedMotion.matches ? " " + dynamic[language].motionReduced : "");
  }
  function applyLanguage(updateUrl) {
    document.documentElement.lang = language === "en" ? "en" : "zh-CN";
    document.querySelectorAll("[data-i18n]").forEach(function (element) {
      var key = element.dataset.i18n;
      element.textContent = language === "en" ? (en[key] || originalText.get(element)) : originalText.get(element);
    });
    originalAttributes.forEach(function (value, element) {
      element.setAttribute(value.attribute, language === "en" ? (en[element.getAttribute(value.marker)] || value.text) : value.text);
    });
    document.querySelectorAll("[data-language-toggle]").forEach(function (button) {
      button.textContent = language === "en" ? "中文" : "EN";
      button.setAttribute("aria-label", language === "en" ? "切换为中文" : "Switch to English");
    });
    document.querySelectorAll("[data-download-label]").forEach(function (element) { element.textContent = dynamic[language][links.download ? "download" : "unavailable"]; });
    document.querySelectorAll("[data-download-note]").forEach(function (element) { element.textContent = dynamic[language][links.download ? "downloadNote" : "unavailableNote"]; });
    document.querySelectorAll(".unavailable").forEach(function (element) { element.title = dynamic[language].linkUnavailable; });
    var isPrivacy = document.body.dataset.page === "privacy";
    document.title = isPrivacy ? (language === "en" ? "Data & privacy notice — ISLAND Launcher" : "数据与隐私说明 — ISLAND Launcher")
      : (language === "en" ? "ISLAND Launcher — Your next adventure starts here." : "ISLAND 岛屿启动器 — 下一次出发，从这里开始。");
    if (mainImage) mainImage.alt = dynamic[language].screenshotAlt;
    document.querySelectorAll('a[href]').forEach(function (link) {
      var raw = link.getAttribute("href");
      if (!raw || raw.charAt(0) === "#" || !/\.html(?:[?#]|$)/.test(raw)) return;
      try {
        var destination = new URL(link.href);
        if (destination.origin !== location.origin) return;
        if (language === "en") destination.searchParams.set("lang", "en"); else destination.searchParams.delete("lang");
        link.href = destination.href;
      } catch (_) {}
    });
    updateMotionDescription();
    updateScreenshot();
    if (updateUrl) {
      var address = new URL(location.href);
      if (language === "en") address.searchParams.set("lang", "en"); else address.searchParams.delete("lang");
      try { history.replaceState(null, "", address.href); } catch (_) {}
    }
  }
  var mainImage = document.getElementById("main-screenshot");
  document.querySelectorAll("[data-language-toggle]").forEach(function (button) { button.addEventListener("click", function () { language = language === "en" ? "zh" : "en"; applyLanguage(true); }); });
  var screenshotRevision = 0;
  function updateScreenshot() {
    var button = document.querySelector('[data-zoom-main]');
    var placeholder = document.getElementById('main-placeholder');
    if (!mainImage || !button || !placeholder) return;
    var themeName = document.querySelector('[data-theme-choice="' + currentTheme + '"] [data-i18n]');
    var label = document.getElementById('placeholder-theme');
    if (label && themeName) label.textContent = themeName.textContent;
    var source = (config.screenshots || {})[currentTheme];
    var revision = ++screenshotRevision;
    button.hidden = true; placeholder.hidden = false;
    if (!source || !/^\.\/assets\/[a-zA-Z0-9_./-]+\.(png|webp|jpe?g)$/i.test(source) || source.includes('..')) { mainImage.removeAttribute('src'); return; }
    mainImage.onload = function () { if (revision === screenshotRevision) { button.hidden = false; placeholder.hidden = true; } };
    mainImage.onerror = function () { if (revision === screenshotRevision) { button.hidden = true; placeholder.hidden = false; } };
    mainImage.src = source;
    if (mainImage.complete && mainImage.naturalWidth) mainImage.onload();
  }
  document.querySelectorAll('[data-theme-choice]').forEach(function (button) {
    button.addEventListener('click', function () {
      currentTheme = button.dataset.themeChoice;
      document.documentElement.dataset.palette = currentTheme;
      document.querySelectorAll('[data-theme-choice]').forEach(function (item) { item.setAttribute('aria-pressed', String(item === button)); });
      updateScreenshot();
    });
  });
  document.querySelectorAll('[data-material]').forEach(function (button) {
    button.addEventListener('click', function () {
      document.querySelector('[data-material-demo]').dataset.materialDemo = button.dataset.material;
      document.querySelectorAll('[data-material]').forEach(function (item) { item.setAttribute('aria-pressed', String(item === button)); });
    });
  });
  var playground = document.getElementById("card-playground");
  document.querySelectorAll("[data-motion]").forEach(function (button) {
    button.addEventListener("click", function () {
      currentMotion = button.dataset.motion;
      if (playground) playground.dataset.effect = currentMotion;
      document.querySelectorAll("[data-motion]").forEach(function (item) { item.setAttribute("aria-pressed", String(item === button)); });
      document.querySelectorAll(".demo-card").forEach(function (card) { ["--rx", "--ry", "--tx", "--ty"].forEach(function (name) { card.style.removeProperty(name); }); });
      updateMotionDescription();
    });
  });
  document.querySelectorAll(".demo-card").forEach(function (card) {
    var frame = 0;
    card.addEventListener("pointermove", function (event) {
      if (reducedMotion.matches || event.pointerType === "touch") return;
      var bounds = card.getBoundingClientRect();
      var x = Math.max(-1, Math.min(1, (event.clientX - bounds.left) / bounds.width * 2 - 1));
      var y = Math.max(-1, Math.min(1, (event.clientY - bounds.top) / bounds.height * 2 - 1));
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(function () {
        card.style.setProperty("--rx", (x * 4) + "deg"); card.style.setProperty("--ry", (-y * 4) + "deg");
        card.style.setProperty("--tx", (x * 5) + "px"); card.style.setProperty("--ty", (y * 4) + "px");
      });
    });
    card.addEventListener("pointerleave", function () {
      cancelAnimationFrame(frame);
      ["--rx", "--ry", "--tx", "--ty"].forEach(function (name) { card.style.removeProperty(name); });
    });
  });
  reducedMotion.addEventListener("change", updateMotionDescription);
  var menu = document.querySelector(".menu-toggle"), nav = document.getElementById("main-nav");
  function closeMenu() { if (menu && nav) { menu.setAttribute("aria-expanded", "false"); nav.classList.remove("is-open"); } }
  if (menu && nav) {
    menu.addEventListener("click", function () { var open = menu.getAttribute("aria-expanded") !== "true"; menu.setAttribute("aria-expanded", String(open)); nav.classList.toggle("is-open", open); });
    nav.querySelectorAll("a").forEach(function (link) { link.addEventListener("click", closeMenu); });
    document.addEventListener("keydown", function (event) { if (event.key === "Escape") closeMenu(); });
    document.addEventListener("click", function (event) { if (!nav.contains(event.target) && !menu.contains(event.target)) closeMenu(); });
  }
  var dialog = document.getElementById("image-dialog"), dialogImage = document.getElementById("dialog-image");
  function openImage(src, alt) {
    if (!dialog || !dialogImage) return;
    dialogImage.src = src; dialogImage.alt = alt;
    dialog.showModal(); document.body.classList.add("modal-open");
  }
  if (dialog) {
    dialog.querySelector("[data-close-dialog]").addEventListener("click", function () { dialog.close(); });
    dialog.addEventListener("click", function (event) { if (event.target === dialog) dialog.close(); });
    dialog.addEventListener("close", function () { document.body.classList.remove("modal-open"); });
    document.querySelectorAll("[data-zoom-main]").forEach(function (button) { button.addEventListener("click", function () { openImage(mainImage.src, mainImage.alt); }); });
    document.querySelectorAll("[data-zoom-image]").forEach(function (button) { button.addEventListener("click", function () { openImage(button.dataset.zoomImage, button.querySelector("img").alt); }); });
  }
  applyLanguage(false);
}());

