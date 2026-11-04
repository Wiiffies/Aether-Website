import { rankForAmount } from "./donation-ranks.js?v=1";

(function () {
  "use strict";

  var config = window.AETHER_CONFIG || {};
  var donationConfig = config.donations || {};
  var endpoint = String(donationConfig.endpoint || "/api/donations");
  var turnstileSiteKey = String(donationConfig.turnstileSiteKey || "").trim();
  var minAmount = Number(donationConfig.minAmount || 3);
  var maxAmount = Number(donationConfig.maxAmount || 500);
  var state = { amount: 5, filter: "plain", turnstileToken: "", turnstileWidget: null };
  var validFilters = ["plain", "glow", "sunset", "terminal"];
  var filterLabels = { plain: "CLEAN", glow: "AETHER GLOW", sunset: "SUNSET", terminal: "TERMINAL" };

  function $(selector) { return document.querySelector(selector); }
  function $$(selector) { return Array.prototype.slice.call(document.querySelectorAll(selector)); }

  var form = $("#donationForm");
  if (!form) return;

  var elements = {
    amountChips: $$(".amount-chip"),
    customAmount: $("#customAmount"),
    name: $("#donorName"),
    message: $("#donorMessage"),
    messageCounter: $("#messageCounter"),
    filterChips: $$("#filterChoices [data-filter]"),
    filterInput: $("#messageFilter"),
    showOnWall: $("#showOnWall"),
    company: $("#company"),
    turnstileWrap: $("#turnstileWrap"),
    turnstile: $("#turnstile"),
    submit: $("#donateSubmit"),
    status: $("#donationStatus"),
    preview: $("#donationPreview"),
    previewAmount: $("#previewAmount"),
    previewFilter: $("#previewFilter"),
    previewRank: $("#previewRank"),
    previewName: $("#previewName"),
    previewMessage: $("#previewMessage"),
    wall: $("#supporterWall"),
    wallGrid: $("#supporterGrid")
  };

  function cleanLine(value, max) {
    var text = String(value == null ? "" : value);
    if (text.normalize) text = text.normalize("NFKC");
    return text
      .replace(/[\u0000-\u001f\u007f]/g, " ")
      .replace(/\s+/g, " ")
      .trim()
      .slice(0, max);
  }

  function cleanMessage(value, max) {
    return String(value == null ? "" : value)
      .replace(/<[^>]*>/g, " ")
      .replace(/https?:\/\/\S+/gi, " ")
      .replace(/www\.\S+/gi, " ")
      .replace(/[\u0000-\u001f\u007f]/g, " ")
      .replace(/\s+/g, " ")
      .trim()
      .slice(0, max);
  }

  function filterClass(value) {
    return validFilters.indexOf(value) >= 0 ? value : "plain";
  }

  function amountValue() {
    var value = Number(elements.customAmount && elements.customAmount.value);
    return isFinite(value) ? value : 0;
  }

  function formatAmount(value) {
    var amount = Number(value);
    if (!isFinite(amount)) amount = 0;
    var rounded = Math.round(amount * 100) / 100;
    return rounded.toLocaleString("en-US", {
      minimumFractionDigits: rounded % 1 === 0 ? 0 : 2,
      maximumFractionDigits: 2
    });
  }

  function applyRank(element, rank) {
    if (!element) return;
    element.className = "rank-pill rank-" + ((rank && rank.id) || "supporter");
    element.textContent = (rank && rank.label) || "Supporter";
  }

  function setActiveAmount(value) {
    elements.amountChips.forEach(function (chip) {
      var active = Number(chip.getAttribute("data-amount")) === Number(value);
      chip.classList.toggle("is-active", active);
      chip.setAttribute("aria-pressed", active ? "true" : "false");
    });
  }

  function updatePreview() {
    var amount = amountValue();
    var name = cleanLine(elements.name && elements.name.value, 24) || "Your name";
    var message = cleanMessage(elements.message && elements.message.value, 140) || "Keep Aether quiet, fast and free.";
    var filter = filterClass(state.filter);

    if (elements.preview) elements.preview.className = "donation-card filter-" + filter;
    if (elements.previewAmount) elements.previewAmount.textContent = "$" + formatAmount(amount > 0 ? amount : 0);
    if (elements.previewFilter) elements.previewFilter.textContent = filterLabels[filter] || "CLEAN";
    if (elements.previewName) elements.previewName.textContent = name;
    if (elements.previewMessage) elements.previewMessage.textContent = message;
    applyRank(elements.previewRank, rankForAmount(amount));
    if (elements.messageCounter) {
      elements.messageCounter.textContent = (elements.message && elements.message.value || "").length + " / 140";
    }
  }

  function setStatus(message, kind) {
    if (!elements.status) return;
    elements.status.textContent = message;
    elements.status.className = "donation-status is-visible" + (kind ? " is-" + kind : "");
  }

  function clearStatus() {
    if (!elements.status) return;
    elements.status.textContent = "";
    elements.status.className = "donation-status";
  }

  function setLoading(loading) {
    if (!elements.submit) return;
    elements.submit.disabled = loading;
    elements.submit.setAttribute("aria-busy", loading ? "true" : "false");
    var label = elements.submit.querySelector("span");
    if (label) label.textContent = loading ? "Opening checkout…" : "Continue to crypto checkout";
  }

  function validate() {
    var amount = amountValue();
    var name = cleanLine(elements.name && elements.name.value, 24);
    var message = cleanMessage(elements.message && elements.message.value, 140);

    if (!isFinite(amount) || amount < minAmount || amount > maxAmount) {
      return { error: "Choose an amount between $" + minAmount + " and $" + maxAmount + "." };
    }
    if (name.length < 2) return { error: "Use a display name with at least 2 characters." };
    if (message.length < 2) return { error: "Write a short message (at least 2 characters)." };
    if (!/^[\p{L}\p{N}_. -]+$/u.test(name)) {
      return { error: "The display name can use letters, numbers, spaces, dots, dashes and underscores." };
    }
    if (state.company && elements.company && elements.company.value) {
      return { error: "The form could not be submitted." };
    }
    if (turnstileSiteKey && !state.turnstileToken) {
      return { error: "Complete the Cloudflare check before continuing." };
    }
    return { amount: amount, name: name, message: message, filter: filterClass(state.filter) };
  }

  function submitDonation(event) {
    event.preventDefault();
    clearStatus();
    var validated = validate();
    if (validated.error) {
      setStatus(validated.error, "error");
      return;
    }

    var payload = {
      amount: validated.amount,
      username: validated.name,
      message: validated.message,
      filter: validated.filter,
      showOnWall: !!(elements.showOnWall && elements.showOnWall.checked),
      turnstileToken: state.turnstileToken,
      company: elements.company ? elements.company.value : ""
    };

    setLoading(true);
    setStatus("Opening the secure NOWPayments checkout…", "ok");

    fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json", "Accept": "application/json" },
      body: JSON.stringify(payload)
    })
      .then(function (response) {
        return response.json().catch(function () { return {}; }).then(function (body) {
          return { ok: response.ok, status: response.status, body: body };
        });
      })
      .then(function (result) {
        var checkoutUrl = result.body.invoiceUrl || result.body.checkoutUrl;
        if (!result.ok || !checkoutUrl) {
          throw new Error(result.body.error || "Checkout is not available right now. Try again in a moment.");
        }
        setStatus("Redirecting to NOWPayments…", "ok");
        window.location.assign(checkoutUrl);
      })
      .catch(function (error) {
        setLoading(false);
        setStatus(error.message || "Checkout is not available right now. Try again in a moment.", "error");
        if (state.turnstileWidget && window.turnstile) window.turnstile.reset(state.turnstileWidget);
        state.turnstileToken = "";
      });
  }

  function setFilter(value) {
    state.filter = filterClass(value);
    if (elements.filterInput) elements.filterInput.value = state.filter;
    elements.filterChips.forEach(function (chip) {
      var active = chip.getAttribute("data-filter") === state.filter;
      chip.classList.toggle("is-active", active);
      chip.setAttribute("aria-pressed", active ? "true" : "false");
    });
    updatePreview();
  }

  elements.amountChips.forEach(function (chip) {
    chip.addEventListener("click", function () {
      var value = Number(chip.getAttribute("data-amount"));
      if (elements.customAmount) elements.customAmount.value = value;
      state.amount = value;
      setActiveAmount(value);
      updatePreview();
    });
  });

  if (elements.customAmount) {
    elements.customAmount.addEventListener("input", function () {
      state.amount = amountValue();
      setActiveAmount(state.amount);
      updatePreview();
    });
  }

  [elements.name, elements.message].forEach(function (input) {
    if (!input) return;
    input.addEventListener("input", updatePreview);
  });

  elements.filterChips.forEach(function (chip) {
    chip.addEventListener("click", function () {
      setFilter(chip.getAttribute("data-filter"));
    });
  });

  form.addEventListener("submit", submitDonation);

  function loadTurnstile() {
    if (!turnstileSiteKey) {
      if (elements.turnstileWrap) elements.turnstileWrap.hidden = true;
      return;
    }
    if (elements.turnstileWrap) elements.turnstileWrap.hidden = false;

    function renderWidget() {
      if (!window.turnstile || !elements.turnstile) return;
      state.turnstileWidget = window.turnstile.render(elements.turnstile, {
        sitekey: turnstileSiteKey,
        theme: "dark",
        callback: function (token) {
          state.turnstileToken = token;
          clearStatus();
        },
        "expired-callback": function () { state.turnstileToken = ""; },
        "error-callback": function () {
          state.turnstileToken = "";
          setStatus("Cloudflare could not verify this browser. Reload and try again.", "error");
        }
      });
    }

    if (window.turnstile) {
      renderWidget();
      return;
    }

    var script = document.createElement("script");
    script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
    script.async = true;
    script.defer = true;
    script.onload = renderWidget;
    script.onerror = function () {
      setStatus("Cloudflare Turnstile could not load. Check your connection and reload.", "error");
    };
    document.head.appendChild(script);
  }

  function renderWall(donations) {
    if (!elements.wall || !elements.wallGrid) return;
    if (!donations.length) {
      elements.wall.hidden = true;
      return;
    }
    elements.wall.hidden = false;
    elements.wallGrid.innerHTML = "";
    donations.forEach(function (donation) {
      var card = document.createElement("article");
      card.className = "supporter-card filter-" + filterClass(donation.filter);
      var head = document.createElement("header");
      var date = document.createElement("span");
      date.textContent = donation.date ? new Date(donation.date).toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" }) : "Supporter";
      var amount = document.createElement("span");
      amount.textContent = donation.amount ? "$" + formatAmount(donation.amount) : "Crypto";
      var rank = donation.rank || rankForAmount(donation.amount);
      var rankEl = document.createElement("span");
      applyRank(rankEl, rank);
      rankEl.title = donation.total ? "Confirmed total: $" + formatAmount(donation.total) : "Donation rank";
      head.appendChild(date);
      head.appendChild(amount);
      head.appendChild(rankEl);
      var name = document.createElement("strong");
      name.textContent = donation.username || "Anonymous";
      var message = document.createElement("p");
      message.textContent = donation.message || "Thanks for keeping Aether running.";
      card.appendChild(head);
      card.appendChild(name);
      card.appendChild(message);
      elements.wallGrid.appendChild(card);
    });
  }

  function loadWall() {
    if (!elements.wall || !elements.wallGrid) return;
    fetch("/api/donations/recent", { headers: { Accept: "application/json" }, cache: "no-store" })
      .then(function (response) {
        if (!response.ok) throw new Error("wall unavailable");
        return response.json();
      })
      .then(function (body) {
        if (body && Array.isArray(body.donations)) renderWall(body.donations);
      })
      .catch(function () {
        elements.wall.hidden = true;
      });
  }

  var params = new URLSearchParams(window.location.search);
  if (params.get("status") === "success") {
    setStatus("Payment submitted. NOWPayments is confirming it; your card and rank appear on the wall after confirmation.", "ok");
  } else if (params.get("status") === "cancel") {
    setStatus("Checkout was cancelled. Nothing was charged.", "error");
  }

  setActiveAmount(5);
  updatePreview();
  loadTurnstile();
  loadWall();
})();
