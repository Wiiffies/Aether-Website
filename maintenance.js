(function () {
  "use strict";

  var config = window.AETHER_CONFIG || {};
  var settings = config.maintenance || {};
  var params = new URLSearchParams(window.location.search);
  var flag = params.get("maintenance");
  var isPage = document.body && document.body.hasAttribute("data-maintenance-page");
  var storageKey = "aether-maintenance-bypass";
  var bypassed = false;

  try {
    bypassed = sessionStorage.getItem(storageKey) === "1";
    if (flag === "1") {
      sessionStorage.removeItem(storageKey);
      bypassed = false;
    } else if (flag === "0") {
      sessionStorage.setItem(storageKey, "1");
      bypassed = true;
    }
  } catch (error) {

    bypassed = flag === "0";
  }

  var active = settings.enabled === true || flag === "1";
  if (flag === "0") active = false;

  function setText(selector, value) {
    if (!value) return;
    var node = document.querySelector(selector);
    if (node) node.textContent = value;
  }

  if (isPage) {
    var live = active && !bypassed;
    document.documentElement.classList.add(live ? "maintenance-live" : "maintenance-preview");
    if (bypassed) document.documentElement.classList.add("maintenance-bypassed");

    setText("[data-maintenance-title]", settings.title);
    setText("[data-maintenance-title-accent]", settings.titleAccent);
    setText("[data-maintenance-message]", settings.message);
    setText("[data-maintenance-state]", live ? "Live" : "Preview");
    setText("[data-maintenance-mode]", live ? "Live" : "Preview");

    var endsAt = settings.endsAt ? new Date(settings.endsAt) : null;
    var countdown = document.querySelector("[data-maintenance-countdown]");
    var eta = document.querySelector("[data-maintenance-eta]");
    var progress = document.querySelector("[data-maintenance-progress]");
    var progressBar = document.querySelector(".maintenance-progress");

    function tick() {
      if (!countdown) return;
      if (!endsAt || isNaN(endsAt.getTime())) {
        countdown.textContent = "Soon";
        if (eta) eta.textContent = "Soon";
        if (progress) progress.style.width = "38%";
        return;
      }

      var remaining = endsAt.getTime() - Date.now();
      if (remaining <= 0) {
        countdown.textContent = "Any minute now";
        if (eta) eta.textContent = "Now";
        if (progress) progress.style.width = "100%";
        if (progressBar) progressBar.setAttribute("aria-valuenow", "100");
        return;
      }

      var seconds = Math.floor(remaining / 1000);
      var days = Math.floor(seconds / 86400);
      var hours = Math.floor((seconds % 86400) / 3600);
      var minutes = Math.floor((seconds % 3600) / 60);
      var secs = seconds % 60;

      countdown.textContent = (days > 0 ? days + "d " : "") +
        String(hours).padStart(2, "0") + ":" +
        String(minutes).padStart(2, "0") + ":" +
        String(secs).padStart(2, "0");
      if (eta) eta.textContent = endsAt.toLocaleString();
    }

    tick();
    window.setInterval(tick, 1000);
    return;
  }

  document.documentElement.classList.add(active && !bypassed ? "maintenance-active" : "maintenance-clear");
  if (bypassed) document.documentElement.classList.add("maintenance-bypassed");

  if (active && !bypassed) {
    window.location.replace("/maintenance.html" + (flag === "1" ? "?maintenance=1" : ""));
  }
})();
