(function () {
  "use strict";
  function github(value) {
    if (typeof value !== "string" || !value.trim()) return null;
    try {
      var url = new URL(value.trim());
      var parts = url.pathname.split("/").filter(Boolean);
      if (url.protocol !== "https:" || url.hostname !== "github.com" || url.port || url.username || url.password || parts.length < 2) return null;
      url.hash = ""; url.search = "";
      return url;
    } catch (_) { return null; }
  }
  window.ISLAND_RESOLVE_LINKS = function (config) {
    config = config || {};
    var repository = github(config.githubRepoUrl);
    var release = github(config.githubReleaseUrl);
    if (!repository && release) repository = new URL("/" + release.pathname.split("/").filter(Boolean).slice(0, 2).join("/"), release);
    if (repository) repository = new URL("/" + repository.pathname.split("/").filter(Boolean).slice(0, 2).join("/").replace(/\.git$/, ""), repository);
    if (!release && repository) release = new URL(repository.pathname + "/releases/latest", repository);
    var contact = null;
    if (typeof config.contactUrl === "string" && config.contactUrl.trim()) {
      try {
        var candidate = new URL(config.contactUrl.trim());
        if (candidate.protocol === "https:" && !candidate.username && !candidate.password) contact = candidate.href;
      } catch (_) {}
    }
    return { repository: repository ? repository.href : null, releases: release ? release.href : null, download: release ? release.href : null, contact: contact || (repository ? repository.href : null) };
  };
}());
