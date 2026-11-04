(function () {
  "use strict";

  var data = window.AETHER_FLAGS;
  if (!data || !Array.isArray(data.presets)) return;

  var favoritesKey = "aether-fastflags-favorites-v1";
  var state = { query: "", filter: "all", view: "cards" };
  var favorites = new Set();

  try {
    var saved = JSON.parse(localStorage.getItem(favoritesKey) || "[]");
    if (Array.isArray(saved)) favorites = new Set(saved.filter(function (name) { return typeof name === "string"; }));
  } catch (error) {
    favorites = new Set();
  }

  function $(selector) { return document.querySelector(selector); }
  function $$(selector) { return Array.prototype.slice.call(document.querySelectorAll(selector)); }
  function escapeHtml(value) {
    return String(value == null ? "" : value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }
  function normalize(value) { return String(value || "").toLocaleLowerCase(); }
  function saveFavorites() {
    try { localStorage.setItem(favoritesKey, JSON.stringify(Array.from(favorites))); } catch (error) { /* private mode */ }
  }
  function filterLabel(id) {
    var labels = { plain: "CLEAN", glow: "AETHER GLOW", sunset: "SUNSET", terminal: "TERMINAL" };
    return labels[id] || "CLEAN";
  }
  function normalizedFilter(value) {
    return ["plain", "glow", "sunset", "terminal"].indexOf(value) >= 0 ? value : "plain";
  }

  var elements = {
    search: $("#fastflagSearch"),
    clear: $("#clearFastflagSearch"),
    filters: $("#fastflagFilters"),
    grid: $("#flagsGrid"),
    indexView: $("#flagIndexView"),
    indexBody: $("#flagIndexBody"),
    empty: $("#fastflagEmpty"),
    count: $("#flagCount"),
    reset: $("#resetFastflagFilters"),
    viewButtons: $$("[data-view]"),
    totals: {
      presets: $$("[data-total-presets]"),
      flags: $$("[data-total-flags]"),
      distinct: $$("[data-total-distinct]"),
      allowlisted: $$("[data-total-allowlisted]")
    }
  };

  function setText(nodes, value) {
    nodes.forEach(function (node) { node.textContent = String(value); });
  }

  function categoryById(id) {
    return data.categories.find(function (category) { return category.id === id; });
  }

  var categoryByFlag = {};
  data.presets.forEach(function (preset) {
    preset.flags.forEach(function (flag) {
      if (!categoryByFlag[flag.key]) categoryByFlag[flag.key] = [];
      if (categoryByFlag[flag.key].indexOf(preset.category) < 0) categoryByFlag[flag.key].push(preset.category);
    });
  });

  function matchesPreset(preset) {
    var query = normalize(state.query);
    var filter = state.filter;
    var haystack = normalize([
      preset.name,
      preset.description,
      preset.group,
      preset.categoryLabel,
      preset.flags.map(function (flag) { return flag.key + " " + flag.value; }).join(" ")
    ].join(" "));

    if (query && haystack.indexOf(query) < 0) return false;
    if (filter === "working") return preset.allowlisted;
    if (filter === "legacy") return !preset.allowlisted;
    if (filter === "favorites") return favorites.has(preset.name);
    if (filter !== "all") return preset.category === filter;
    return true;
  }

  function flagMatches(flag) {
    var query = normalize(state.query);
    var filter = state.filter;
    var haystack = normalize([
      flag.key,
      flag.values.join(" "),
      flag.presets.join(" ")
    ].join(" "));

    if (query && haystack.indexOf(query) < 0) return false;
    if (filter === "working") return flag.allowlisted;
    if (filter === "legacy") return flag.legacy;
    if (filter === "favorites") {
      return flag.presets.some(function (name) { return favorites.has(name); });
    }
    if (filter !== "all") {
      return (categoryByFlag[flag.key] || []).indexOf(filter) >= 0;
    }
    return true;
  }

  function visiblePresets() {
    return data.presets.filter(matchesPreset).slice().sort(function (a, b) {
      if (a.allowlisted !== b.allowlisted) return a.allowlisted ? -1 : 1;
      if (a.categoryOrder !== b.categoryOrder) return a.categoryOrder - b.categoryOrder;
      return a.name.localeCompare(b.name);
    });
  }

  function visibleFlags() {
    return data.flags.filter(flagMatches);
  }

  function renderFilters() {
    var counts = {
      all: data.presets.length,
      working: data.totals.allowlisted,
      legacy: data.totals.legacy,
      favorites: favorites.size
    };
    var chips = [
      { id: "all", label: "All" },
      { id: "working", label: "Working" },
      { id: "legacy", label: "Legacy" },
      { id: "favorites", label: "★ Favorites" }
    ].concat(data.categories.map(function (category) {
      return { id: category.id, label: category.label };
    }));

    elements.filters.innerHTML = chips.map(function (chip) {
      var count = counts[chip.id];
      if (count == null) {
        var category = categoryById(chip.id);
        count = category ? category.count : 0;
      }
      return '<button type="button" class="filter-chip' + (state.filter === chip.id ? " is-active" : "") +
        '" data-filter="' + escapeHtml(chip.id) + '" aria-pressed="' + (state.filter === chip.id ? "true" : "false") + '">' +
        escapeHtml(chip.label) + ' <span class="chip-count">' + count + "</span></button>";
    }).join("");
  }

  function flagRows(preset) {
    return preset.flags.map(function (flag) {
      return '<div class="flag-key"><code>' + escapeHtml(flag.key) + "</code><span>" +
        escapeHtml(flag.value === "" ? "(empty)" : flag.value) + "</span></div>";
    }).join("");
  }

  function cardMarkup(preset) {
    var favorite = favorites.has(preset.name);
    return '<article class="flag-card cat-' + escapeHtml(preset.category) + (preset.allowlisted ? "" : " is-legacy") + '">' +
      '<div class="flag-card-head"><span class="flag-category">' + escapeHtml(preset.categoryLabel) + "</span>" +
      '<button class="flag-star' + (favorite ? " is-favorite" : "") + '" type="button" data-favorite="' + escapeHtml(preset.name) + '" aria-label="' +
      (favorite ? "Remove from favorites" : "Add to favorites") + '" aria-pressed="' + (favorite ? "true" : "false") + '">' +
      (favorite ? "★" : "☆") + "</button></div>" +
      "<h3>" + escapeHtml(preset.name) + "</h3>" +
      '<p class="flag-description">' + escapeHtml(preset.description) + "</p>" +
      '<div class="flag-key-list">' + flagRows(preset) + "</div>" +
      '<div class="flag-card-foot"><span class="flag-state ' + (preset.allowlisted ? "is-live" : "is-legacy") + '">' +
      (preset.allowlisted ? "Allowlisted" : "Legacy") + "</span><span>" + preset.flags.length +
      " flag" + (preset.flags.length === 1 ? "" : "s") + "</span></div></article>";
  }

  function renderCards() {
    var list = visiblePresets();
    elements.grid.innerHTML = list.map(cardMarkup).join("");
    elements.grid.hidden = state.view !== "cards" || list.length === 0;
    return list;
  }

  function indexMarkup(flag) {
    var status = flag.allowlisted && flag.legacy ? "Mixed" : flag.allowlisted ? "Allowlisted" : "Legacy";
    var stateClass = flag.allowlisted ? "is-live" : "is-legacy";
    return "<tr><td><code>" + escapeHtml(flag.key) + "</code></td>" +
      '<td class="index-values">' + escapeHtml(flag.values.map(function (value) { return value === "" ? "(empty)" : value; }).join(", ")) + "</td>" +
      '<td class="index-used" title="' + escapeHtml(flag.presets.join(", ")) + '">' + flag.presets.length + " preset" + (flag.presets.length === 1 ? "" : "s") + "</td>" +
      '<td><span class="flag-state ' + stateClass + '">' + status + "</span></td></tr>";
  }

  function renderIndex() {
    var list = visibleFlags();
    elements.indexBody.innerHTML = list.map(indexMarkup).join("");
    elements.indexView.hidden = state.view !== "index" || list.length === 0;
    return list;
  }

  function render() {
    var presets = renderCards();
    var flags = renderIndex();
    var activeList = state.view === "cards" ? presets : flags;
    var currentCount = activeList.length;
    var flagCount = presets.reduce(function (sum, preset) { return sum + preset.flags.length; }, 0);

    elements.empty.hidden = currentCount !== 0;
    elements.count.textContent = state.view === "cards"
      ? presets.length + " of " + data.totals.presets + " presets · " + flagCount + " flags"
      : flags.length + " of " + data.totals.distinctFlags + " distinct flags";

    elements.viewButtons.forEach(function (button) {
      var active = button.getAttribute("data-view") === state.view;
      button.classList.toggle("is-active", active);
      button.setAttribute("aria-selected", active ? "true" : "false");
    });

    renderFilters();
  }

  if (elements.search) {
    elements.search.addEventListener("input", function () {
      state.query = elements.search.value;
      render();
    });
  }

  if (elements.clear) {
    elements.clear.addEventListener("click", function () {
      if (elements.search) elements.search.value = "";
      state.query = "";
      render();
      if (elements.search) elements.search.focus();
    });
  }

  if (elements.filters) {
    elements.filters.addEventListener("click", function (event) {
      var button = event.target.closest("[data-filter]");
      if (!button) return;
      state.filter = button.getAttribute("data-filter") || "all";
      render();
    });
  }

  if (elements.grid) {
    elements.grid.addEventListener("click", function (event) {
      var button = event.target.closest("[data-favorite]");
      if (!button) return;
      var name = button.getAttribute("data-favorite");
      if (!name) return;
      if (favorites.has(name)) favorites.delete(name);
      else favorites.add(name);
      saveFavorites();
      render();
    });
  }

  if (elements.viewButtons.length) {
    elements.viewButtons.forEach(function (button) {
      button.addEventListener("click", function () {
        state.view = button.getAttribute("data-view") === "index" ? "index" : "cards";
        render();
        var target = state.view === "index" ? elements.indexView : elements.grid;
        if (target && !target.hidden) target.scrollIntoView({ behavior: "smooth", block: "start" });
      });
    });
  }

  if (elements.reset) {
    elements.reset.addEventListener("click", function () {
      state.query = "";
      state.filter = "all";
      state.view = "cards";
      if (elements.search) {
        elements.search.value = "";
        elements.search.focus();
      }
      render();
    });
  }

  setText(elements.totals.presets, data.totals.presets);
  setText(elements.totals.flags, data.totals.flags);
  setText(elements.totals.distinct, data.totals.distinctFlags);
  setText(elements.totals.allowlisted, data.totals.allowlisted);
  render();
})();
