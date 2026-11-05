(function () {
  "use strict";

  document.documentElement.classList.add("js", "motion-forced");

  var reduceMotion = false;
  var ultraMotion = window.matchMedia("(hover: hover) and (pointer: fine)").matches;

  var config = window.AETHER_CONFIG || {};
  var $ = function (selector, root) {
    return (root || document).querySelector(selector);
  };
  var $$ = function (selector, root) {
    return Array.prototype.slice.call((root || document).querySelectorAll(selector));
  };

  function nextFrame(callback) {
    if (document.visibilityState === "hidden") {
      window.setTimeout(callback, 50);
    } else {
      window.requestAnimationFrame(callback);
    }
  }

  function resolveRepo() {
    var configured = String(config.githubRepo || "").trim();
    if (configured && !/your-name|YOUR_/i.test(configured) && /^[\w.-]+\/[\w.-]+$/.test(configured)) {
      return configured;
    }

    var host = location.hostname;
    if (host.slice(-10) === ".github.io") {
      var owner = host.split(".")[0];
      var repo = location.pathname.split("/").filter(Boolean)[0];
      if (owner && repo) return owner + "/" + repo;
    }

    return null;
  }

  var repo = resolveRepo();
  var repoUrl = repo ? "https://github.com/" + repo : "";
  var releasesUrl = repo ? repoUrl + "/releases" : "";
  var latestApi = repo ? "https://api.github.com/repos/" + repo + "/releases/latest" : "";
  var fallbackVersion = String(config.versionFallback || "1.2.0").replace(/^v/i, "");

  var release = {
    version: "",
    date: "",
    downloads: 0,
    size: 0,
    assetName: "",
    assetUrl: "",
    pageUrl: ""
  };

  function text(selector, value) {
    $$(selector).forEach(function (node) {
      node.textContent = value;
    });
  }

  function humanBytes(bytes) {
    if (!bytes || bytes < 1024) return bytes ? bytes + " B" : "—";
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(0) + " KB";
    return (bytes / (1024 * 1024)).toFixed(1) + " MB";
  }

  function formatDate(iso) {
    if (!iso) return "—";
    var date = new Date(iso);
    if (isNaN(date.getTime())) return "—";
    return date.toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });
  }

  var toastTimer = 0;
  var toastNode = null;

  function toast(message) {
    if (!toastNode) {
      toastNode = document.createElement("div");
      toastNode.className = "toast";
      toastNode.setAttribute("role", "status");
      toastNode.setAttribute("aria-live", "polite");
      document.body.appendChild(toastNode);
    }

    toastNode.textContent = message;
    toastNode.classList.remove("show");
    void toastNode.offsetWidth;
    toastNode.classList.add("show");

    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () {
      toastNode.classList.remove("show");
    }, 3800);
  }

  function copyText(value) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(value);
    }

    return new Promise(function (resolve, reject) {
      var area = document.createElement("textarea");
      area.value = value;
      area.style.position = "fixed";
      area.style.left = "-9999px";
      document.body.appendChild(area);
      area.select();

      try {
        var ok = document.execCommand("copy");
        area.remove();
        if (ok) resolve();
        else reject(new Error("copy failed"));
      } catch (error) {
        area.remove();
        reject(error);
      }
    });
  }

  function markNeedsConfig(link, message) {
    link.href = "#";
    link.setAttribute("data-needs-config", "true");
    link.title = message;
  }

  function setLinks() {
    $$("[data-github]").forEach(function (link) {
      if (repo) {
        link.href = repoUrl;
        link.target = "_blank";
        link.rel = "noopener";
        link.removeAttribute("data-needs-config");
        link.title = "";
      } else {
        markNeedsConfig(link, "Set githubRepo in Website/config.js");
      }
    });

    $$("[data-releases]").forEach(function (link) {
      if (releasesUrl) {
        link.href = releasesUrl;
        link.target = "_blank";
        link.rel = "noopener";
        link.removeAttribute("data-needs-config");
        link.title = "";
      } else {
        markNeedsConfig(link, "Set githubRepo in Website/config.js");
      }
    });

    $$("[data-source]").forEach(function (link) {
      if (repoUrl) {
        link.href = repoUrl + "/tree/main/Programm";
        link.target = "_blank";
        link.rel = "noopener";
        link.removeAttribute("data-needs-config");
        link.title = "";
      } else {
        markNeedsConfig(link, "Set githubRepo in Website/config.js");
      }
    });

    if (config.fallbackDownloadUrl) {
      $$("[data-download]").forEach(function (link) {
        link.href = config.fallbackDownloadUrl;
        link.target = "_blank";
        link.rel = "noopener";
      });
    }
  }

  document.addEventListener("click", function (event) {
    var target = event.target.closest("[data-needs-config]");
    if (!target) return;
    event.preventDefault();
    toast("Set githubRepo in Website/config.js to point this at the repository.");
  });

  function pickAsset(assets) {
    var list = assets || [];
    var windowsZip = list.find(function (asset) {
      return /win/i.test(asset.name || "") && /\.zip$/i.test(asset.name || "");
    });
    if (windowsZip) return windowsZip;

    var anyZip = list.find(function (asset) {
      return /\.zip$/i.test(asset.name || "");
    });
    return anyZip || list[0] || null;
  }

  function setReleaseState(state, label) {
    var node = $("[data-release-state]");
    if (!node) return;
    node.textContent = label;
    node.className = "status-pill" + (state === "ready" ? " is-ready" : state === "quiet" ? " is-quiet" : "");
  }

  function renderRelease() {
    var version = release.version || fallbackVersion;
    text("[data-version]", "v" + version);
    text("[data-build-version]", version);
    text("[data-release-version]", release.version || "—");
    text("[data-release-date]", release.date ? formatDate(release.date) : "—");
    text("[data-release-size]", release.size ? humanBytes(release.size) : "—");
    text("[data-release-downloads]", release.downloads ? release.downloads.toLocaleString("en-US") : "—");

    var target = release.assetUrl || release.pageUrl || config.fallbackDownloadUrl || releasesUrl || "";
    $$("[data-download]").forEach(function (link) {
      if (target) {
        link.href = target;
        link.removeAttribute("data-needs-config");
        link.title = "";
        if (/^https?:\/\//i.test(target)) {
          link.target = "_blank";
          link.rel = "noopener";
        }
      } else {
        link.setAttribute("data-needs-config", "true");
        link.title = "Set githubRepo in Website/config.js";
      }
    });

    var meta = $("[data-download-meta]");
    if (meta) {
      if (release.assetUrl) {
        meta.textContent = "Latest release from GitHub. No account required.";
      } else if (repo) {
        meta.textContent = "No packaged release yet — open the releases page or build from source.";
      } else {
        meta.textContent = "Release lookup is not configured yet. Build from source below.";
      }
    }

    if (release.assetUrl) {
      setReleaseState("ready", "Latest");
    } else if (repo) {
      setReleaseState("quiet", "Source");
    } else {
      setReleaseState("quiet", "Setup");
    }
  }

  function adopt(data) {
    var asset = pickAsset(data.assets);
    release = {
      version: String(data.tag_name || "").replace(/^v/i, ""),
      date: data.published_at || "",
      downloads: (data.assets || []).reduce(function (sum, item) {
        return sum + (item.download_count || 0);
      }, 0),
      size: asset ? asset.size || 0 : 0,
      assetName: asset ? asset.name || "" : "",
      assetUrl: asset ? asset.browser_download_url || "" : "",
      pageUrl: data.html_url || releasesUrl
    };
    renderRelease();
  }

  function loadRelease() {
    if (!latestApi) {
      renderRelease();
      return;
    }

    setReleaseState("quiet", "Checking");

    fetch(latestApi, { headers: { Accept: "application/vnd.github+json" } })
      .then(function (response) {
        if (!response.ok) throw new Error("release lookup failed: " + response.status);
        return response.json();
      })
      .then(adopt)
      .catch(function () {
        renderRelease();
      });
  }

  var topbar = $("#topbar");
  var navToggle = $("#navToggle");

  function closeNav() {
    if (!topbar || !navToggle) return;
    topbar.classList.remove("nav-open");
    navToggle.setAttribute("aria-expanded", "false");
    navToggle.setAttribute("aria-label", "Open navigation");
  }

  if (navToggle && topbar) {
    navToggle.addEventListener("click", function () {
      var open = !topbar.classList.contains("nav-open");
      topbar.classList.toggle("nav-open", open);
      navToggle.setAttribute("aria-expanded", open ? "true" : "false");
      navToggle.setAttribute("aria-label", open ? "Close navigation" : "Open navigation");
    });

    $$("#siteNav a").forEach(function (link) {
      link.addEventListener("click", closeNav);
    });

    document.addEventListener("keydown", function (event) {
      if (event.key === "Escape") closeNav();
    });
  }

  function setupScrollProgress() {
    var bar = document.createElement("div");
    bar.className = "scroll-progress";
    bar.setAttribute("aria-hidden", "true");
    document.body.appendChild(bar);

    var scheduled = false;

    function update() {
      scheduled = false;
      var max = document.documentElement.scrollHeight - window.innerHeight;
      var ratio = max > 0 ? Math.min(1, Math.max(0, window.scrollY / max)) : 0;
      bar.style.transform = "scaleX(" + ratio + ")";
      if (topbar) topbar.classList.toggle("is-scrolled", window.scrollY > 8);
    }

    function request() {
      if (scheduled) return;
      scheduled = true;
      nextFrame(update);
    }

    window.addEventListener("scroll", request, { passive: true });
    window.addEventListener("resize", request);
    window.addEventListener("load", request);
    request();
  }

  function sectionLabel(section, index) {
    var explicit = section.getAttribute("data-rail-label");
    if (explicit) return explicit;
    if (section.id === "top") return "Overview";
    if (section.classList.contains("page-hero")) return "Script";
    if (section.classList.contains("script-grid")) return "Files";

    var kicker = section.querySelector(".kicker");
    var label = kicker ? kicker.textContent : "";
    label = label.replace(/\s+/g, " ").replace(/^[0-9]{1,2}\s+/, "").trim();
    if (!label) {
      var heading = section.querySelector("h1, h2");
      if (heading) label = heading.textContent.replace(/\s+/g, " ").trim();
    }
    if (!label) label = "Section " + (index + 1);
    return label.length > 28 ? label.slice(0, 25) + "…" : label;
  }

  function setupSectionRail() {
    var host = $("main");
    if (!host) return;

    var sections = $$("main > section");
    if (!sections.length) return;

    var rail = document.createElement("nav");
    rail.className = "section-rail";
    rail.setAttribute("aria-label", "Page sections");

    var progress = document.createElement("span");
    progress.className = "rail-progress";
    progress.setAttribute("aria-hidden", "true");
    rail.appendChild(progress);

    var buttons = [];
    var scheduled = false;
    var navLinks = $$("#siteNav .nav-tab");
    var navBySection = {};
    var navAliases = { inside: "overlay" };

    navLinks.forEach(function (link) {
      var sectionId = link.getAttribute("data-nav-section");
      if (sectionId) navBySection[sectionId] = link;
    });

    function setNavActive(sectionId) {
      var target = navBySection[navAliases[sectionId] || sectionId];
      if (!target) return;

      navLinks.forEach(function (link) {
        var active = link === target;
        link.classList.toggle("is-active", active);
        if (active) link.setAttribute("aria-current", "location");
        else link.removeAttribute("aria-current");
      });
    }

    navLinks.forEach(function (link) {
      link.addEventListener("click", function () {
        var sectionId = link.getAttribute("data-nav-section");
        if (!sectionId) return;

        var sectionIndex = -1;
        sections.forEach(function (section, index) {
          if (section.id === sectionId) sectionIndex = index;
        });

        if (sectionIndex >= 0) setActive(sectionIndex);
      });
    });

    function setActive(index) {
      buttons.forEach(function (button, buttonIndex) {
        var active = buttonIndex === index;
        button.classList.toggle("is-active", active);
        if (active) button.setAttribute("aria-current", "location");
        else button.removeAttribute("aria-current");
      });
      sections.forEach(function (section, sectionIndex) {
        section.classList.toggle("is-current", sectionIndex === index);
      });
      if (sections[index]) setNavActive(sections[index].id);
    }

    sections.forEach(function (section, index) {
      var label = sectionLabel(section, index);
      var button = document.createElement("button");
      button.type = "button";
      button.className = "rail-button";
      button.setAttribute("aria-label", "Scroll to " + label);
      button.title = label;

      var dot = document.createElement("span");
      dot.className = "rail-dot";
      dot.setAttribute("aria-hidden", "true");
      var text = document.createElement("span");
      text.className = "rail-label";
      text.textContent = label;
      button.appendChild(dot);
      button.appendChild(text);

      button.addEventListener("click", function () {
        section.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
        setActive(index);
        request();
      });

      rail.appendChild(button);
      buttons.push(button);
    });

    document.body.appendChild(rail);

    function update() {
      scheduled = false;
      var max = document.documentElement.scrollHeight - window.innerHeight;
      var ratio = max > 0 ? Math.min(1, Math.max(0, window.scrollY / max)) : 0;
      var marker = window.scrollY + window.innerHeight * 0.35;
      var active = 0;

      sections.forEach(function (section) {
        if (section.getBoundingClientRect().top + window.scrollY <= marker) active = sections.indexOf(section);
      });

      if (max > 0 && window.scrollY >= max - 2) active = sections.length - 1;
      rail.style.setProperty("--rail-progress", ratio.toFixed(3));
      setActive(active);
    }

    function request() {
      if (scheduled) return;
      scheduled = true;
      nextFrame(update);
    }

    window.addEventListener("scroll", request, { passive: true });
    window.addEventListener("resize", request);
    window.addEventListener("load", request);
    window.addEventListener("hashchange", request);
    update();
  }

  function setupScrollTop() {
    var button = document.createElement("button");
    button.type = "button";
    button.className = "scroll-top";
    button.setAttribute("aria-label", "Back to top");
    button.innerHTML = '<svg viewBox="0 0 16 16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M8 13V3M4 7l4-4 4 4"/></svg>';
    document.body.appendChild(button);

    var scheduled = false;

    function update() {
      scheduled = false;
      button.classList.toggle("is-visible", window.scrollY > 620);
    }

    function request() {
      if (scheduled) return;
      scheduled = true;
      nextFrame(update);
    }

    button.addEventListener("click", function () {
      window.scrollTo({ top: 0, behavior: reduceMotion ? "auto" : "smooth" });
    });

    window.addEventListener("scroll", request, { passive: true });
    window.addEventListener("resize", request);
    update();
  }

  function setupReveal() {
    var groups = [
      [".section-head", 0],
      [".feature-grid .feature-card", 50],
      [".split-copy", 0],
      [".split-media", 60],
      [".callout", 0],
      [".download-card", 0],
      [".script-grid > .card", 60],
      [".safety-card", 0],
      [".badges", 0],
      [".settings-list li", 35],
      [".steps li", 45],
      [".release-card", 0],
      [".release-row", 30],
      [".download-copy > .hero-actions", 0],
      [".download-copy > .download-meta", 0],
      [".download-copy > .requirements", 35],
      [".download-copy > .source-row", 70],
      [".footer-inner > *", 35],
      [".code-card .card-head", 0],
      [".code-card .code-block", 35],
      [".code-card .card-actions", 70]
    ];
    var targets = [];

    groups.forEach(function (group) {
      $$(group[0]).forEach(function (node, index) {
        if (node.classList.contains("reveal")) return;
        node.classList.add("reveal");
        node.style.setProperty("--reveal-delay", (index * group[1]) + "ms");
        targets.push(node);
      });
    });

    if (!targets.length) return;

    document.documentElement.classList.add("reveal-ready");

    if (reduceMotion || !("IntersectionObserver" in window) || document.visibilityState === "hidden") {
      targets.forEach(function (node) { node.classList.add("in"); });
      return;
    }

    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        entry.target.classList.add("in");
        observer.unobserve(entry.target);
      });
    }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 });

    targets.forEach(function (node) { observer.observe(node); });
  }

  function setupCounters() {
    if (reduceMotion) return;

    $$("[data-count]").forEach(function (node) {
      var target = parseInt(node.getAttribute("data-count"), 10);
      if (isNaN(target)) return;

      var pad = parseInt(node.getAttribute("data-pad"), 10) || 0;
      var started = 0;

      function step(now) {
        if (!started) started = now;
        var progress = Math.min(1, (now - started) / 900);
        var eased = 1 - Math.pow(1 - progress, 3);
        node.textContent = String(Math.round(target * eased)).padStart(pad, "0");
        if (progress < 1) window.requestAnimationFrame(step);
      }

      window.requestAnimationFrame(step);
    });
  }

  function setupTilt() {
    if (reduceMotion) return;
    if (!window.matchMedia("(hover: hover) and (pointer: fine)").matches) return;

    $$(".screenshot-frame, .split-media img").forEach(function (target) {
      target.classList.add("tilt");
      var pending = 0;

      target.addEventListener("pointerenter", function () {
        target.classList.add("is-tilting");
      });

      target.addEventListener("pointermove", function (event) {
        if (pending) return;
        var x = event.clientX;
        var y = event.clientY;

        pending = window.requestAnimationFrame(function () {
          pending = 0;
          var rect = target.getBoundingClientRect();
          if (!rect.width || !rect.height) return;

          var px = Math.min(1, Math.max(0, (x - rect.left) / rect.width));
          var py = Math.min(1, Math.max(0, (y - rect.top) / rect.height));

          target.style.setProperty("--tilt-x", ((py - 0.5) * -3.5).toFixed(2) + "deg");
          target.style.setProperty("--tilt-y", ((px - 0.5) * 4.5).toFixed(2) + "deg");
          target.style.setProperty("--tilt-shift-x", ((px - 0.5) * 4).toFixed(2) + "px");
          target.style.setProperty("--tilt-shift-y", ((py - 0.5) * 3).toFixed(2) + "px");
        });
      });

      target.addEventListener("pointerleave", function () {
        if (pending) {
          window.cancelAnimationFrame(pending);
          pending = 0;
        }

        target.classList.remove("is-tilting");
        target.style.setProperty("--tilt-x", "0deg");
        target.style.setProperty("--tilt-y", "0deg");
        target.style.setProperty("--tilt-shift-x", "0px");
        target.style.setProperty("--tilt-shift-y", "0px");
      });
    });
  }

  function setupIntro() {
    if (reduceMotion) return;

    try {
      if (sessionStorage.getItem("aether-intro-seen") === "1") return;
      sessionStorage.setItem("aether-intro-seen", "1");
    } catch (error) {

    }

    var intro = document.createElement("div");
    intro.className = "intro-curtain";
    intro.setAttribute("aria-hidden", "true");
    intro.innerHTML =
      '<div class="intro-mark">' +
        '<span class="intro-logo">A</span>' +
        '<span class="intro-word">AETHER</span>' +
        '<span class="intro-sub">MOTION / ONLINE</span>' +
      '</div>' +
      '<span class="intro-line"></span>';
    document.body.appendChild(intro);
    window.setTimeout(function () {
      intro.remove();
    }, 1500);
  }

  function setupCursorAura() {
    if (!ultraMotion) return;

    var aura = document.createElement("div");
    aura.className = "cursor-aura";
    aura.setAttribute("aria-hidden", "true");
    document.body.appendChild(aura);

    var radius = (aura.offsetWidth || 200) / 2;
    var pointerX = window.innerWidth / 2;
    var pointerY = window.innerHeight / 2;
    var currentX = pointerX;
    var currentY = pointerY;

    document.addEventListener("pointermove", function (event) {
      pointerX = event.clientX;
      pointerY = event.clientY;
      document.body.classList.add("has-aura");
    });

    document.addEventListener("pointerleave", function () {
      document.body.classList.remove("has-aura");
    });

    function frame() {
      currentX += (pointerX - currentX) * 0.12;
      currentY += (pointerY - currentY) * 0.12;
      aura.style.transform = "translate3d(" + (currentX - radius) + "px," + (currentY - radius) + "px,0)";
      window.requestAnimationFrame(frame);
    }

    window.requestAnimationFrame(frame);
  }

  function setupHeroParticles() {
    var host = $(".hero-card");
    if (!host || !ultraMotion) return;

    var canvas = document.createElement("canvas");
    canvas.className = "fx-canvas";
    canvas.setAttribute("aria-hidden", "true");
    host.prepend(canvas);

    var context = canvas.getContext("2d");
    if (!context) return;

    var points = [];
    var mouse = { x: -1000, y: -1000 };
    var width = 1;
    var height = 1;
    var dpr = 1;

    function resetPoints() {
      points = [];
      var count = Math.max(18, Math.min(38, Math.floor(width / 24)));
      for (var index = 0; index < count; index += 1) {
        points.push({
          x: Math.random() * width,
          y: Math.random() * height,
          vx: (Math.random() - 0.5) * 0.34,
          vy: (Math.random() - 0.5) * 0.34,
          size: 0.8 + Math.random() * 1.4
        });
      }
    }

    function resize() {
      var rect = host.getBoundingClientRect();
      width = Math.max(1, rect.width);
      height = Math.max(1, rect.height);
      dpr = Math.min(2, window.devicePixelRatio || 1);
      canvas.width = Math.floor(width * dpr);
      canvas.height = Math.floor(height * dpr);
      context.setTransform(dpr, 0, 0, dpr, 0, 0);
      context.globalCompositeOperation = "lighter";
      resetPoints();
    }

    host.addEventListener("pointermove", function (event) {
      var rect = host.getBoundingClientRect();
      mouse.x = event.clientX - rect.left;
      mouse.y = event.clientY - rect.top;
    });
    host.addEventListener("pointerleave", function () {
      mouse.x = -1000;
      mouse.y = -1000;
    });

    function frame() {
      if (!canvas.isConnected) return;

      context.clearRect(0, 0, width, height);

      for (var index = 0; index < points.length; index += 1) {
        var point = points[index];
        point.x += point.vx;
        point.y += point.vy;

        if (point.x < 0) point.x = width;
        if (point.x > width) point.x = 0;
        if (point.y < 0) point.y = height;
        if (point.y > height) point.y = 0;

        var mouseDx = point.x - mouse.x;
        var mouseDy = point.y - mouse.y;
        var mouseDistance = Math.sqrt(mouseDx * mouseDx + mouseDy * mouseDy);
        if (mouseDistance < 120 && mouseDistance > 0.1) {
          point.x += (mouseDx / mouseDistance) * 0.65;
          point.y += (mouseDy / mouseDistance) * 0.65;
        }

        context.beginPath();
        context.fillStyle = "rgba(126, 170, 255, 0.5)";
        context.arc(point.x, point.y, point.size, 0, Math.PI * 2);
        context.fill();

        for (var otherIndex = index + 1; otherIndex < points.length; otherIndex += 1) {
          var other = points[otherIndex];
          var dx = point.x - other.x;
          var dy = point.y - other.y;
          var distance = Math.sqrt(dx * dx + dy * dy);
          if (distance > 84) continue;

          context.beginPath();
          context.strokeStyle = "rgba(86, 150, 255, " + (0.14 * (1 - distance / 84)).toFixed(3) + ")";
          context.lineWidth = 0.7;
          context.moveTo(point.x, point.y);
          context.lineTo(other.x, other.y);
          context.stroke();
        }
      }

      window.requestAnimationFrame(frame);
    }

    resize();
    window.addEventListener("resize", resize);
    window.requestAnimationFrame(frame);
  }

  function setupHeroOrbs() {
    var host = $(".hero-card");
    if (!host || !ultraMotion) return;

    var orbs = document.createElement("div");
    orbs.className = "fx-orbs";
    orbs.setAttribute("aria-hidden", "true");
    orbs.innerHTML = "<i></i><i></i><i></i>";
    host.prepend(orbs);
  }

  function setupParallax() {
    if (!ultraMotion) return;

    var targets = $$(".split-media img, .fx-orbs i, .motion-debris span");
    if (!targets.length) return;
    var scheduled = false;

    function update() {
      scheduled = false;
      targets.forEach(function (target) {
        var rect = target.getBoundingClientRect();
        var centered = rect.top + rect.height / 2 - window.innerHeight / 2;
        var shift = Math.max(-16, Math.min(16, centered * -0.035));
        target.style.setProperty("--parallax-y", shift.toFixed(1) + "px");
      });

    }

    function request() {
      if (scheduled) return;
      scheduled = true;
      nextFrame(update);
    }

    window.addEventListener("scroll", request, { passive: true });
    window.addEventListener("resize", request);
    request();
  }

  function setupHoverSparks() {
    if (!ultraMotion) return;

    var selector = ".btn, .nav-tab, .github-link, .feature-card, .stat, .panel-chip, .badge, .release-card, .release-row, .download-card, .callout, .split-card, .screenshot-frame, .settings-list li, .steps li, .notice, .footer-links a, .copy-btn, .text-link, .maintenance-pill, .maintenance-meta > div";
    var palette = ["#b4c7ff", "#5696ff", "#8fe3a8", "#ff9ec4", "#ffd479"];

    document.addEventListener("pointerover", function (event) {
      var target = event.target && event.target.closest ? event.target.closest(selector) : null;
      if (!target) return;
      if (event.relatedTarget && event.relatedTarget.nodeType === 1 && target.contains(event.relatedTarget)) return;

      var rect = target.getBoundingClientRect();
      if (!rect.width || !rect.height) return;

      for (var index = 0; index < 10; index += 1) {
        var spark = document.createElement("span");
        var color = palette[Math.floor(Math.random() * palette.length)];
        spark.className = "hover-spark";
        spark.setAttribute("aria-hidden", "true");
        spark.style.left = (rect.left + rect.width * (0.18 + Math.random() * 0.64)) + "px";
        spark.style.top = (rect.top + rect.height * (0.18 + Math.random() * 0.64)) + "px";
        spark.style.background = color;
        spark.style.boxShadow = "0 0 14px " + color;
        spark.style.setProperty("--sx", ((Math.random() - 0.5) * 72).toFixed(1) + "px");
        spark.style.setProperty("--sy", (-18 - Math.random() * 52).toFixed(1) + "px");
        spark.style.setProperty("--spin", ((Math.random() - 0.5) * 220).toFixed(0) + "deg");
        spark.style.setProperty("--spark-size", (3 + Math.random() * 4).toFixed(1) + "px");
        spark.style.animationDelay = (index * 0.026) + "s";
        document.body.appendChild(spark);
        window.setTimeout((function (node) {
          return function () {
            if (node.parentNode) node.parentNode.removeChild(node);
          };
        })(spark), 1000);
      }
    });
  }

  function setupMagnetic() {
    if (!ultraMotion) return;

    var selector = ".btn, .nav-tab, .github-link, .panel-chip, .badge, .copy-btn, .footer-links a";
    $$(selector).forEach(function (target) {
      target.classList.add("magnetic");
      target.addEventListener("pointermove", function (event) {
        var rect = target.getBoundingClientRect();
        if (!rect.width || !rect.height) return;
        var x = ((event.clientX - rect.left) / rect.width - 0.5) * 12;
        var y = ((event.clientY - rect.top) / rect.height - 0.5) * 9;
        target.style.setProperty("--mag-x", x.toFixed(1) + "px");
        target.style.setProperty("--mag-y", y.toFixed(1) + "px");
      });
      target.addEventListener("pointerleave", function () {
        target.style.setProperty("--mag-x", "0px");
        target.style.setProperty("--mag-y", "0px");
      });
    });
  }

  function setupCardTilt() {
    if (!ultraMotion) return;

    $$(".feature-card").forEach(function (card) {
      card.classList.add("tilt-card");
      card.addEventListener("pointermove", function (event) {
        var rect = card.getBoundingClientRect();
        var x = (event.clientX - rect.left) / rect.width - 0.5;
        var y = (event.clientY - rect.top) / rect.height - 0.5;
        card.style.setProperty("--rx", (y * -3.4).toFixed(2) + "deg");
        card.style.setProperty("--ry", (x * 4.2).toFixed(2) + "deg");
        card.style.setProperty("--spot-x", ((x + 0.5) * 100).toFixed(1) + "%");
        card.style.setProperty("--spot-y", ((y + 0.5) * 100).toFixed(1) + "%");
      });
      card.addEventListener("pointerleave", function () {
        card.style.setProperty("--rx", "0deg");
        card.style.setProperty("--ry", "0deg");
        card.style.setProperty("--spot-x", "50%");
        card.style.setProperty("--spot-y", "0%");
      });
    });
  }

  function setupCursorTrail() {
    if (!ultraMotion) return;

    var glyphs = ["·", "+", "✦"];
    var palette = ["#b4c7ff", "#5696ff"];
    var last = 0;

    document.addEventListener("pointermove", function (event) {
      var now = window.performance.now();
      if (now - last < 95) return;
      last = now;

      var trail = document.createElement("span");
      trail.className = "cursor-trail";
      trail.setAttribute("aria-hidden", "true");
      trail.textContent = glyphs[Math.floor(Math.random() * glyphs.length)];
      trail.style.left = event.clientX + "px";
      trail.style.top = event.clientY + "px";
      trail.style.color = palette[Math.floor(Math.random() * palette.length)];
      trail.style.fontSize = (6 + Math.random() * 4).toFixed(1) + "px";
      trail.style.setProperty("--drift-x", ((Math.random() - 0.5) * 22).toFixed(1) + "px");
      trail.style.setProperty("--drift-y", (6 + Math.random() * 12).toFixed(1) + "px");
      trail.style.setProperty("--spin", ((Math.random() - 0.5) * 120).toFixed(0) + "deg");
      document.body.appendChild(trail);

      window.setTimeout(function () {
        trail.remove();
      }, 620);
    }, { passive: true });
  }

  function setupFloatingDebris() {
    if (!ultraMotion) return;

    var host = document.createElement("div");
    host.className = "motion-debris";
    host.setAttribute("aria-hidden", "true");
    var glyphs = ["+", "×", "·", "✦", "◇"];

    for (var index = 0; index < 18; index += 1) {
      var speck = document.createElement("span");
      speck.textContent = glyphs[index % glyphs.length];
      speck.style.left = (4 + Math.random() * 92) + "%";
      speck.style.top = (6 + Math.random() * 88) + "%";
      speck.style.fontSize = (6 + Math.random() * 12).toFixed(1) + "px";
      speck.style.setProperty("--drift-x", ((Math.random() - 0.5) * 160).toFixed(0) + "px");
      speck.style.setProperty("--drift-y", (-30 - Math.random() * 150).toFixed(0) + "px");
      speck.style.setProperty("--spin", ((Math.random() - 0.5) * 360).toFixed(0) + "deg");
      speck.style.animationDuration = (10 + Math.random() * 16).toFixed(1) + "s";
      speck.style.animationDelay = (-Math.random() * 20).toFixed(1) + "s";
      host.appendChild(speck);
    }

    document.body.appendChild(host);
  }

  function setupMotion() {
    setupScrollProgress();
    setupSectionRail();
    setupScrollTop();
    setupReveal();
    setupCounters();
    setupTilt();
    setupIntro();
    setupCursorAura();
    setupHeroParticles();
    setupHeroOrbs();
    setupFloatingDebris();
    setupParallax();
    setupHoverSparks();
    setupMagnetic();
    setupCardTilt();
    setupCursorTrail();
  }

  function flash(button, label) {
    var previous = button.textContent;
    button.textContent = label || "Copied";
    button.classList.add("copied");
    setTimeout(function () {
      button.textContent = previous;
      button.classList.remove("copied");
    }, 1800);
  }

  $$("[data-copy]").forEach(function (button) {
    button.addEventListener("click", function () {
      var value = button.getAttribute("data-copy") || "";
      copyText(value)
        .then(function () {
          flash(button, "Copied");
          toast("Copied to the clipboard.");
        })
        .catch(function () {
          toast("Copying is blocked by the browser. Select the text manually.");
        });
    });
  });

  $$("[data-script-copy]").forEach(function (button) {
    button.addEventListener("click", function () {
      var url = button.getAttribute("data-script-copy");
      var label = button.querySelector("span") || button;
      var original = label.textContent;

      button.disabled = true;
      button.setAttribute("aria-busy", "true");
      label.textContent = "Copying…";

      fetch(url, { cache: "no-store" })
        .then(function (response) {
          if (!response.ok) throw new Error("script fetch failed: " + response.status);
          return response.text();
        })
        .then(function (source) {
          return copyText(source);
        })
        .then(function () {
          label.textContent = "Copied";
          toast("The complete aether-menu.lua script is in your clipboard.");
          setTimeout(function () {
            label.textContent = original;
            button.disabled = false;
            button.removeAttribute("aria-busy");
          }, 1800);
        })
        .catch(function () {
          label.textContent = original;
          button.disabled = false;
          button.removeAttribute("aria-busy");
          toast("Could not load the script. Use the Download .lua button instead.");
        });
    });
  });

  setupMotion();
  setLinks();
  loadRelease();
})();
