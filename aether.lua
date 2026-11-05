--[[
	get-aether.de — the site, in a Roblox window
	===========================================

	Paste this whole file into an executor and run it, or drop it into a
	LocalScript under StarterPlayer → StarterPlayerScripts. It opens straight
	into the Aether page: nav, hero, ticker, feature index, overlay spread,
	editions, the release box and feedback.

	Roblox cannot host a live webview, so the page is built from Roblox GUI.
	Every action is wired to the real site rather than a demo toast:

		Download            the published v1.2.0 installer
		GitHub / releases   the repository and its release list
		Build from source   the source tree
		Feedback            copies what you typed for get-aether.de

	Right Ctrl hides it, Right Ctrl again brings it back.

	Version, size, download count and publish date are read from the GitHub
	API when HTTP is available; otherwise the release box keeps its dashes.

	The AetherUI library sits between this header and the divider below it and
	is left exactly as shipped.
]]

local AetherUI = (function()

--[[
	AetherUI 1.0.0
	The Aether website, rebuilt as a Roblox UI library.

	Twelve editions, the same palette tokens, the same editorial layout:
	a split hero, a mono ticker, a feature index, an overlay spread,
	an edition gallery, numbers, download and feedback.

	Everything is plain Roblox GUI — no images required, no packages.

	Quick start (LocalScript):
		local AetherUI = require(game.ReplicatedStorage.AetherUI)
		local app = AetherUI.new({ Theme = "midnight" })
		app:Toast("Hello from AetherUI")

	Read README.md next to this file for the full guide.
]]

local TweenService = game:GetService("TweenService")
local RunService = game:GetService("RunService")
local TextService = game:GetService("TextService")
local GuiService = game:GetService("GuiService")
local Players = game:GetService("Players")

local AetherUI = {}
AetherUI.__index = AetherUI
AetherUI.Version = "1.0.0"

-- Design canvas. Every offset in this file is in these pixels; one UIScale
-- shrinks the whole page to whatever screen it lands on.
local PAGE_WIDTH = 1280
local GUTTER = 56
local NAV_HEIGHT = 68

local function hex(value)
	local clean = string.gsub(value, "#", "")
	return Color3.fromRGB(
		tonumber(string.sub(clean, 1, 2), 16),
		tonumber(string.sub(clean, 3, 4), 16),
		tonumber(string.sub(clean, 5, 6), 16)
	)
end

local THEME_ORDER = {
	"midnight", "amethyst", "emerald", "ember", "rose", "glacier",
	"sandstone", "onyx", "aurora", "crimson", "moss", "dusk",
}

-- ------------------------------------------------------------------ themes
-- Palettes are lifted straight from Website/styles.css so the two stay 1:1.

local THEME_DATA = {
	midnight = {
		name = "Midnight", meta = "Ink / cobalt · split", spec = "Serif · 6 px radius",
		hint = "Midnight — a split hero, crisp rules and a two-column index.",
		color = "#0A0C11",
		bg = "#0B0D10", bgDeep = "#07080B", panel = "#101318", panel2 = "#151920",
		line = "#262B33", lineSoft = "#1A1E25", text = "#F2EFE8", textMid = "#B8BEC8",
		textDim = "#7C8490", accent = "#7C9CFF", accentHi = "#B4C7FF",
		accentSoft = "#1A2743", accentDeep = "#0E172A", accentInk = "#0B0D10",
		ok = "#95C69A", danger = "#E78374",
		display = "serif", h1 = 88, h2 = 66, radius = 6, radiusSm = 4,
		featureCols = 2, featureHeight = 126, heroCenter = false, heroMirror = false,
		overlayVisualFirst = false, frameAngle = -0.8, frameOffset = 14,
		sectionGap = 132, previewDirection = "row", upper = false,
	},
	amethyst = {
		name = "Amethyst", meta = "Plum / lilac · mirrored", spec = "Sans · 24 px radius",
		hint = "Amethyst — mirrored sections, wide radii and a softer sans display.",
		color = "#0B0912",
		bg = "#0F0A15", bgDeep = "#09060D", panel = "#171020", panel2 = "#1D1428",
		line = "#332741", lineSoft = "#241A2F", text = "#F4EDF6", textMid = "#C1B4CA",
		textDim = "#8A7A96", accent = "#C49BFF", accentHi = "#E0C8FF",
		accentSoft = "#35244C", accentDeep = "#1C1230", accentInk = "#120B18",
		ok = "#A8D4A2", danger = "#E18383",
		display = "sansBold", h1 = 86, h2 = 60, radius = 24, radiusSm = 14,
		featureCols = 2, featureHeight = 126, heroCenter = false, heroMirror = true,
		overlayVisualFirst = true, frameAngle = 1.3, frameOffset = 18,
		sectionGap = 118, previewDirection = "column", upper = false,
	},
	emerald = {
		name = "Emerald", meta = "Sage / green ink · centred", spec = "Serif · 2 px radius",
		hint = "Emerald — a centred serif hero, green ink and a three-column index.",
		color = "#07110D",
		bg = "#07110D", bgDeep = "#040A07", panel = "#0C1712", panel2 = "#111E18",
		line = "#22352B", lineSoft = "#17271F", text = "#EDF3EA", textMid = "#B1C2B4",
		textDim = "#748A79", accent = "#A3D7A8", accentHi = "#C9ECC9",
		accentSoft = "#1D3B2B", accentDeep = "#10251A", accentInk = "#07110D",
		ok = "#A3D7A8", danger = "#E08A72",
		display = "serif", h1 = 112, h2 = 68, radius = 2, radiusSm = 2,
		featureCols = 3, featureHeight = 132, heroCenter = true, heroMirror = false,
		overlayVisualFirst = true, frameAngle = 0, frameOffset = 10,
		sectionGap = 128, previewDirection = "column", upper = false,
	},
	ember = {
		name = "Ember", meta = "Rust / soot · condensed", spec = "Condensed · 0 px radius",
		hint = "Ember — condensed caps, hard corners and a rust accent.",
		color = "#120C09",
		bg = "#130D09", bgDeep = "#0C0705", panel = "#1B1310", panel2 = "#221915",
		line = "#3A291F", lineSoft = "#291D16", text = "#F5ECE4", textMid = "#C9B5A8",
		textDim = "#8C786C", accent = "#E98A5C", accentHi = "#FFB495",
		accentSoft = "#4F2D1D", accentDeep = "#2A170E", accentInk = "#160C07",
		ok = "#C9C07A", danger = "#E26154",
		display = "condensed", h1 = 66, h2 = 56, radius = 0, radiusSm = 0,
		featureCols = 3, featureHeight = 128, heroCenter = false, heroMirror = false,
		overlayVisualFirst = false, frameAngle = 1.2, frameOffset = 8,
		sectionGap = 110, previewDirection = "row", upper = true,
	},
	rose = {
		name = "Rose", meta = "Plum / pink · soft", spec = "Sans · 26 px radius",
		hint = "Rose — centred headings, pill controls and generous rounding.",
		color = "#120A10",
		bg = "#140A10", bgDeep = "#0C0609", panel = "#1C1018", panel2 = "#24151F",
		line = "#3E2633", lineSoft = "#2B1A24", text = "#F7ECF1", textMid = "#CDB6C1",
		textDim = "#927B86", accent = "#F08AB4", accentHi = "#FFC0D7",
		accentSoft = "#4A2235", accentDeep = "#2A1220", accentInk = "#170A10",
		ok = "#A3D5BD", danger = "#E4778C",
		display = "sans", h1 = 108, h2 = 64, radius = 26, radiusSm = 16,
		featureCols = 2, featureHeight = 126, heroCenter = true, heroMirror = false,
		overlayVisualFirst = false, frameAngle = -1.4, frameOffset = 16,
		sectionGap = 120, previewDirection = "column", upper = false,
	},
	glacier = {
		name = "Glacier", meta = "Ice / cyan · technical", spec = "Mono · 0 px radius",
		hint = "Glacier — a thin, technical grid with mono labels and cold cyan.",
		color = "#071116",
		bg = "#061015", bgDeep = "#04090C", panel = "#0A171D", panel2 = "#0E1D24",
		line = "#1C333D", lineSoft = "#12262E", text = "#EAF3F5", textMid = "#AEC0C5",
		textDim = "#6F858D", accent = "#7ED6EE", accentHi = "#B5ECF8",
		accentSoft = "#14323E", accentDeep = "#0B2029", accentInk = "#061015",
		ok = "#88D9C2", danger = "#E28D7D",
		display = "mono", h1 = 64, h2 = 44, radius = 0, radiusSm = 0,
		featureCols = 2, featureHeight = 118, heroCenter = false, heroMirror = false,
		overlayVisualFirst = false, frameAngle = 0, frameOffset = 0,
		sectionGap = 116, previewDirection = "row", upper = true,
	},
	sandstone = {
		name = "Sandstone", meta = "Warm ink / amber · offset", spec = "Serif · 2 px radius",
		hint = "Sandstone — warm ink, a serif display and a deliberately offset frame.",
		color = "#131009",
		bg = "#14110B", bgDeep = "#0D0B07", panel = "#1B1710", panel2 = "#221D14",
		line = "#3B3223", lineSoft = "#2A2317", text = "#F5EFE3", textMid = "#CDBEA8",
		textDim = "#8D7E68", accent = "#DCA268", accentHi = "#F2C28E",
		accentSoft = "#4A3822", accentDeep = "#291E10", accentInk = "#16100A",
		ok = "#A9C48A", danger = "#DF7D63",
		display = "serif", h1 = 74, h2 = 60, radius = 2, radiusSm = 2,
		featureCols = 2, featureHeight = 128, heroCenter = false, heroMirror = false,
		overlayVisualFirst = false, frameAngle = -1.6, frameOffset = 20,
		sectionGap = 142, previewDirection = "row", upper = false,
	},
	onyx = {
		name = "Onyx", meta = "Monochrome · bare", spec = "Sans · 0 px radius",
		hint = "Onyx — monochrome, enormous type and nothing decorative in the way.",
		color = "#0A0B0C",
		bg = "#0A0B0C", bgDeep = "#060708", panel = "#101113", panel2 = "#151619",
		line = "#2A2C30", lineSoft = "#1B1D20", text = "#F4F4F2", textMid = "#B7B8BA",
		textDim = "#7A7C80", accent = "#E6E7E3", accentHi = "#FFFFFF",
		accentSoft = "#2D2E31", accentDeep = "#191A1C", accentInk = "#0A0B0C",
		ok = "#B9C7B0", danger = "#D97D70",
		display = "sansBlack", h1 = 126, h2 = 72, radius = 0, radiusSm = 0,
		featureCols = 1, featureHeight = 154, heroCenter = true, heroMirror = false,
		overlayVisualFirst = false, frameAngle = 0, frameOffset = 0,
		sectionGap = 144, previewDirection = "row", upper = false,
	},
	aurora = {
		name = "Aurora", meta = "Sea glass / teal · centred", spec = "Sans · 18 px radius",
		hint = "Aurora — centred type, pill controls and a cold sea-glass accent.",
		color = "#061014",
		bg = "#061014", bgDeep = "#04090C", panel = "#0B171B", panel2 = "#101F24",
		line = "#1D353A", lineSoft = "#13272B", text = "#EEF7F3", textMid = "#B4C8C1",
		textDim = "#78938B", accent = "#5EE6D0", accentHi = "#A8FFF1",
		accentSoft = "#143A37", accentDeep = "#0A2825", accentInk = "#061014",
		ok = "#9ACB9F", danger = "#E68B7B",
		display = "sansBold", h1 = 88, h2 = 62, radius = 18, radiusSm = 10,
		featureCols = 3, featureHeight = 132, heroCenter = true, heroMirror = false,
		overlayVisualFirst = false, frameAngle = 0, frameOffset = 10,
		sectionGap = 122, previewDirection = "column", upper = false,
	},
	crimson = {
		name = "Crimson", meta = "Oxblood / red · compact", spec = "Serif · 2 px radius",
		hint = "Crimson — a compact serif page with oxblood surfaces and a sharp red accent.",
		color = "#130B0D",
		bg = "#130B0D", bgDeep = "#0D0608", panel = "#1B1013", panel2 = "#221418",
		line = "#3A2329", lineSoft = "#28181D", text = "#F6ECEA", textMid = "#CAB6B4",
		textDim = "#8D7473", accent = "#F25863", accentHi = "#FF9AA2",
		accentSoft = "#4F252B", accentDeep = "#2B1217", accentInk = "#130B0D",
		ok = "#C7A978", danger = "#FF7A7A",
		display = "serif", h1 = 84, h2 = 60, radius = 2, radiusSm = 2,
		featureCols = 3, featureHeight = 128, heroCenter = false, heroMirror = false,
		overlayVisualFirst = false, frameAngle = -0.9, frameOffset = 9,
		sectionGap = 118, previewDirection = "row", upper = false,
	},
	moss = {
		name = "Moss", meta = "Olive / lichen · indexed", spec = "Serif · 3 px radius",
		hint = "Moss — a field-guide layout in olive ink, lichen accents and three-column indexes.",
		color = "#0E1108",
		bg = "#0E1108", bgDeep = "#090C05", panel = "#151A0F", panel2 = "#1B2113",
		line = "#303A22", lineSoft = "#202817", text = "#F2F4E7", textMid = "#C3C9B0",
		textDim = "#858D72", accent = "#B7D06A", accentHi = "#DCF1A5",
		accentSoft = "#3C4D24", accentDeep = "#222D15", accentInk = "#0E1108",
		ok = "#A9C48A", danger = "#DF8063",
		display = "serif", h1 = 70, h2 = 58, radius = 3, radiusSm = 2,
		featureCols = 3, featureHeight = 128, heroCenter = false, heroMirror = false,
		overlayVisualFirst = false, frameAngle = 0.7, frameOffset = 16,
		sectionGap = 138, previewDirection = "row", upper = false,
	},
	dusk = {
		name = "Dusk", meta = "Indigo / peach · mirrored", spec = "Serif · 10 px radius",
		hint = "Dusk — mirrored sections, a big serif and a low peach light.",
		color = "#0D0912",
		bg = "#0D0912", bgDeep = "#08050C", panel = "#15101A", panel2 = "#1B1421",
		line = "#33263A", lineSoft = "#231A2A", text = "#F5EDF2", textMid = "#C7B5C3",
		textDim = "#8F7A89", accent = "#FF9E6B", accentHi = "#FFC9A3",
		accentSoft = "#4A2B30", accentDeep = "#2A1820", accentInk = "#0D0912",
		ok = "#B2C98F", danger = "#E4897B",
		display = "serif", h1 = 84, h2 = 66, radius = 10, radiusSm = 7,
		featureCols = 2, featureHeight = 128, heroCenter = false, heroMirror = true,
		overlayVisualFirst = true, frameAngle = 1.5, frameOffset = 18,
		sectionGap = 130, previewDirection = "column", upper = false,
	},
}

local THEMES = {}
for index, id in ipairs(THEME_ORDER) do
	local theme = THEME_DATA[id]
	for key, value in pairs(theme) do
		if type(value) == "string" and string.sub(value, 1, 1) == "#" then
			theme[key] = hex(value)
		end
	end
	theme.id = id
	theme.index = index
	THEMES[id] = theme
end

AetherUI.ThemeIds = THEME_ORDER

local FONTS = {
	serif = Enum.Font.Merriweather,
	sans = Enum.Font.Gotham,
	sansBold = Enum.Font.GothamBold,
	sansBlack = Enum.Font.GothamBlack,
	mono = Enum.Font.RobotoMono,
	condensed = Enum.Font.Oswald,
	arrow = Enum.Font.Arial,
}

local function makeStyles(app)
	local t = app.t
	return {
		H1 = { font = t.display, size = math.floor(t.h1 * 0.9), color = "text", lineHeight = 0.95 },
		H2 = { font = t.display, size = math.floor(t.h2 * 0.9), color = "text", lineHeight = 0.98 },
		H3 = { font = t.display, size = 22, color = "text", lineHeight = 1.2 },
		H4 = { font = t.display, size = 17, color = "text", lineHeight = 1.25 },
		Display = { font = t.display, size = 38, color = "text", lineHeight = 1.1 },
		Glyph = { font = t.display, size = 120, color = "text", lineHeight = 0.8 },
		Lede = { font = "sans", size = 17, color = "textMid", lineHeight = 1.7 },
		Body = { font = "sans", size = 14, color = "textMid", lineHeight = 1.65 },
		Small = { font = "sans", size = 13, color = "textMid", lineHeight = 1.6 },
		Fine = { font = "mono", size = 10, color = "textDim", lineHeight = 1.55 },
		Label = { font = "mono", size = 9, color = "textDim", lineHeight = 1.4 },
		Mono = { font = "mono", size = 12, color = "text", lineHeight = 1.5 },
		Number = { font = t.display, size = 58, color = "text", lineHeight = 1 },
	}
end

-- ------------------------------------------------------------------ app

local buildNav, buildPage

local function applyPadding(instance, top, right, bottom, left)
	local padding = Instance.new("UIPadding")
	padding.PaddingTop = UDim.new(0, top or 0)
	padding.PaddingRight = UDim.new(0, right or 0)
	padding.PaddingBottom = UDim.new(0, bottom or 0)
	padding.PaddingLeft = UDim.new(0, left or 0)
	padding.Parent = instance
	return padding
end

local function applyCorner(instance, radius)
	if not radius or radius == 0 then return end
	local corner = Instance.new("UICorner")
	if radius == "pill" then
		corner.CornerRadius = UDim.new(1, 0)
	else
		corner.CornerRadius = UDim.new(0, radius)
	end
	corner.Parent = instance
	return corner
end

local function applyStroke(instance, color, thickness, transparency)
	local stroke = Instance.new("UIStroke")
	stroke.Color = color
	stroke.Thickness = thickness or 1
	stroke.Transparency = transparency or 0
	stroke.ApplyStrokeMode = Enum.ApplyStrokeMode.Border
	stroke.Parent = instance
	return stroke
end

function AetherUI.new(options)
	options = options or {}
	local self = setmetatable({}, AetherUI)
	self.options = options
	self.config = options.Config or {}
	self.state = { feedback = "", contact = "", requirements = false }
	self.sections = {}
	self._connections = {}
	self._permanentConnections = {}
	self._themeListeners = {}
	self._order = 0
	self._toastToken = 0
	self.themeId = THEMES[options.Theme] and options.Theme or "midnight"
	self.t = THEMES[self.themeId]
	self.styles = makeStyles(self)

	local parent = options.Parent
	if not parent then
		local player = Players.LocalPlayer
		parent = player and player:FindFirstChildOfClass("PlayerGui")
	end
	if not parent then
		error("AetherUI.new: pass Parent = PlayerGui when no LocalPlayer is available", 2)
	end

	local inset = 36
	local ok, topbar = pcall(function() return GuiService.TopbarInset end)
	-- TopbarInset is a Rect; its Height is the vertical inset (not a Vector2 Y).
	if ok and topbar then inset = topbar.Height end
	self.inset = inset

	local gui = Instance.new("ScreenGui")
	gui.Name = options.Name or "AetherUI"
	gui.ResetOnSpawn = false
	gui.IgnoreGuiInset = true
	gui.DisplayOrder = options.DisplayOrder or 50
	gui.ZIndexBehavior = Enum.ZIndexBehavior.Sibling
	self.gui = gui
	gui.Parent = parent

	self.backdrop = Instance.new("Frame")
	self.backdrop.Name = "Backdrop"
	self.backdrop.Size = UDim2.fromScale(1, 1)
	self.backdrop.BackgroundColor3 = self.t.bg
	self.backdrop.BorderSizePixel = 0
	self.backdrop.ZIndex = 0
	self.backdrop.Parent = gui

	self.page = Instance.new("ScrollingFrame")
	self.page.Name = "Page"
	self.page.Size = UDim2.fromScale(1, 1)
	self.page.BackgroundTransparency = 1
	self.page.BorderSizePixel = 0
	self.page.ScrollingDirection = Enum.ScrollingDirection.Y
	self.page.CanvasSize = UDim2.new()
	self.page.ScrollBarThickness = 4
	self.page.ScrollBarImageTransparency = 0.55
	self.page.ScrollBarImageColor3 = self.t.textDim
	self.page.ZIndex = 1
	self.page.Parent = gui
	self.scrolling = self.page

	self.root = Instance.new("Frame")
	self.root.Name = "Canvas"
	self.root.Size = UDim2.fromOffset(PAGE_WIDTH, 200)
	self.root.Position = UDim2.new(0.5, 0, 0, 0)
	self.root.AnchorPoint = Vector2.new(0.5, 0)
	self.root.BackgroundTransparency = 1
	self.root.BorderSizePixel = 0
	self.root.ZIndex = 1
	self.root.Parent = self.page

	self.scale = Instance.new("UIScale")
	self.scale.Parent = self.root

	self.stack = Instance.new("Frame")
	self.stack.Name = "Stack"
	self.stack.Size = UDim2.new(1, 0, 0, 0)
	self.stack.AutomaticSize = Enum.AutomaticSize.Y
	self.stack.BackgroundTransparency = 1
	self.stack.BorderSizePixel = 0
	self.stack.Parent = self.root

	local layout = Instance.new("UIListLayout")
	layout.FillDirection = Enum.FillDirection.Vertical
	layout.SortOrder = Enum.SortOrder.LayoutOrder
	layout.Padding = UDim.new(0, 0)
	layout.Parent = self.stack

	self.navHost = Instance.new("Frame")
	self.navHost.Name = "NavHost"
	self.navHost.Size = UDim2.new(1, 0, 0, NAV_HEIGHT)
	self.navHost.BackgroundColor3 = self.t.bg
	self.navHost.BackgroundTransparency = 1
	self.navHost.BorderSizePixel = 0
	self.navHost.ZIndex = 10
	self.navHost.Parent = gui

	self.navInner = Instance.new("Frame")
	self.navInner.Name = "Nav"
	self.navInner.Size = UDim2.fromOffset(PAGE_WIDTH, NAV_HEIGHT)
	self.navInner.Position = UDim2.new(0.5, 0, 0, inset)
	self.navInner.AnchorPoint = Vector2.new(0.5, 0)
	self.navInner.BackgroundTransparency = 1
	self.navInner.BorderSizePixel = 0
	self.navInner.ZIndex = 11
	self.navInner.Parent = self.navHost

	self.navScale = Instance.new("UIScale")
	self.navScale.Parent = self.navInner

	self.navRule = Instance.new("Frame")
	self.navRule.Name = "NavRule"
	self.navRule.Size = UDim2.new(1, 0, 0, 1)
	self.navRule.Position = UDim2.new(0, 0, 1, -1)
	self.navRule.BackgroundColor3 = self.t.lineSoft
	self.navRule.BorderSizePixel = 0
	self.navRule.Visible = false
	self.navRule.ZIndex = 12
	self.navRule.Parent = self.navHost

	self.toast = Instance.new("CanvasGroup")
	self.toast.Name = "Toast"
	self.toast.Size = UDim2.fromOffset(340, 0)
	self.toast.AutomaticSize = Enum.AutomaticSize.Y
	self.toast.AnchorPoint = Vector2.new(1, 1)
	self.toast.Position = UDim2.new(1, -20, 1, 4)
	self.toast.BackgroundColor3 = self.t.panel
	self.toast.BorderSizePixel = 0
	self.toast.GroupTransparency = 1
	self.toast.Visible = false
	self.toast.ZIndex = 30
	self.toast.Parent = gui
	applyCorner(self.toast, 2)
	applyStroke(self.toast, self.t.line)
	applyPadding(self.toast, 11, 14, 11, 14)

	local toastBar = Instance.new("Frame")
	toastBar.Name = "Accent"
	toastBar.Size = UDim2.new(0, 3, 1, 0)
	toastBar.BackgroundColor3 = self.t.accent
	toastBar.BorderSizePixel = 0
	toastBar.Parent = self.toast

	self.toastText = Instance.new("TextLabel")
	self.toastText.BackgroundTransparency = 1
	self.toastText.Size = UDim2.new(1, 0, 0, 0)
	self.toastText.AutomaticSize = Enum.AutomaticSize.Y
	self.toastText.Font = FONTS.mono
	self.toastText.TextSize = 10
	self.toastText.TextColor3 = self.t.textMid
	self.toastText.TextWrapped = true
	self.toastText.TextXAlignment = Enum.TextXAlignment.Left
	self.toastText.TextYAlignment = Enum.TextYAlignment.Top
	self.toastText.LineHeight = 1.55
	self.toastText.Text = ""
	self.toastText.Parent = self.toast

	self:_applyScale()
	self:_applyPalette()

	self:_permanent(self.page:GetPropertyChangedSignal("CanvasPosition"):Connect(function()
		self:_updateNav()
	end))
	self:_permanent(self.stack:GetPropertyChangedSignal("AbsoluteSize"):Connect(function()
		self:_syncCanvas()
	end))

	local function watchCamera(camera)
		if not camera then return end
		self:_permanent(camera:GetPropertyChangedSignal("ViewportSize"):Connect(function()
			self:_applyScale()
		end))
		self:_applyScale()
	end

	watchCamera(workspace.CurrentCamera)
	self:_permanent(workspace:GetPropertyChangedSignal("CurrentCamera"):Connect(function()
		watchCamera(workspace.CurrentCamera)
	end))

	self:Render()
	return self
end

function AetherUI:_permanent(connection)
	if connection then table.insert(self._permanentConnections, connection) end
	return connection
end

function AetherUI:_track(connection)
	if connection then table.insert(self._connections, connection) end
	return connection
end

function AetherUI:_disconnect()
	for _, connection in ipairs(self._connections) do
		if typeof(connection) == "RBXScriptConnection" then connection:Disconnect() end
	end
	self._connections = {}
end

function AetherUI:_nextOrder()
	self._order = self._order + 1
	return self._order
end

function AetherUI:Color(value)
	if typeof(value) == "Color3" then return value end
	if type(value) == "string" then
		local token = self.t[value]
		if typeof(token) == "Color3" then return token end
		if string.sub(value, 1, 1) == "#" then return hex(value) end
	end
	return self.t.text
end

function AetherUI:Font(value)
	if typeof(value) == "EnumItem" then return value end
	return FONTS[value] or FONTS.sans
end

function AetherUI:Padding(instance, top, right, bottom, left)
	return applyPadding(instance, top, right, bottom, left)
end

function AetherUI:Corner(instance, radius)
	return applyCorner(instance, radius)
end

function AetherUI:Stroke(instance, color, thickness, transparency)
	return applyStroke(instance, self:Color(color or "line"), thickness, transparency)
end

function AetherUI:_measure(text, size, font)
	local resolved = self:Font(font)
	local ok, bounds = pcall(function()
		return TextService:GetTextSize(text, size, resolved, Vector2.new(10000, 10000))
	end)
	if ok and bounds then return bounds.X end
	return #text * size * 0.58
end

function AetherUI:_applyScale()
	local viewport = nil
	local camera = workspace.CurrentCamera
	if camera then viewport = camera.ViewportSize end
	if not viewport or viewport.X <= 0 then
		viewport = self.gui.AbsoluteSize
	end
	if not viewport or viewport.X <= 0 then viewport = Vector2.new(PAGE_WIDTH, 720) end

	local value = 1
	if self.options.Scale ~= false then
		value = math.clamp(viewport.X / PAGE_WIDTH, 0.28, 1.6)
	end
	self.scale.Scale = value
	self.navScale.Scale = value
	self.navHost.Size = UDim2.new(1, 0, 0, math.ceil(self.inset + NAV_HEIGHT * value))
	if self.topSpacer then
		self.topSpacer.Size = UDim2.new(1, 0, 0, math.ceil(self.inset / value) + NAV_HEIGHT)
	end
	self:_syncCanvas()
end

function AetherUI:_applyPalette()
	local t = self.t
	self.gui.DisplayOrder = self.options.DisplayOrder or 50
	self.backdrop.BackgroundColor3 = t.bg
	self.page.ScrollBarImageColor3 = t.textDim
	self.navHost.BackgroundColor3 = t.bg
	self.navRule.BackgroundColor3 = t.lineSoft
	self.toast.BackgroundColor3 = t.panel
	self.toastText.TextColor3 = t.textMid
	local toastBar = self.toast:FindFirstChild("Accent")
	if toastBar then toastBar.BackgroundColor3 = t.accent end
	local toastStroke = self.toast:FindFirstChildOfClass("UIStroke")
	if toastStroke then toastStroke.Color = t.line end
	self:_updateNav()
end

function AetherUI:_syncCanvas()
	local scale = self.scale.Scale
	if not scale or scale <= 0 then scale = 1 end
	local designHeight = self.stack.AbsoluteSize.Y / scale
	if designHeight > 0 and math.abs(self.root.Size.Y.Offset - designHeight) > 1 then
		self.root.Size = UDim2.new(0, PAGE_WIDTH, 0, math.ceil(designHeight))
	end
	self.page.CanvasSize = UDim2.new(0, 0, 0, self.root.AbsoluteSize.Y)
end

function AetherUI:_updateNav()
	local scrolled = self.page.CanvasPosition.Y > 6
	self.navHost.BackgroundTransparency = scrolled and 0.02 or 1
	self.navRule.Visible = scrolled
end

function AetherUI:_buildTopSpacer()
	local spacer = Instance.new("Frame")
	spacer.Name = "NavSpacer"
	spacer.BackgroundTransparency = 1
	spacer.BorderSizePixel = 0
	spacer.Size = UDim2.new(1, 0, 0, math.ceil(self.inset / self.scale.Scale) + NAV_HEIGHT)
	spacer.LayoutOrder = self:_nextOrder()
	spacer.Parent = self.stack
	self.topSpacer = spacer
	return spacer
end

function AetherUI:Render()
	self:_disconnect()
	for _, child in ipairs(self.stack:GetChildren()) do
		if child:IsA("GuiObject") then child:Destroy() end
	end
	for _, child in ipairs(self.navInner:GetChildren()) do
		if child:IsA("GuiObject") then child:Destroy() end
	end
	self._order = 0
	self.sections = {}
	self.styles = makeStyles(self)
	self:_applyPalette()
	self.navHost.Visible = self.options.Page ~= false
	if self.options.Page ~= false then
		self:_buildTopSpacer()
		buildNav(self)
		buildPage(self)
	end
	task.defer(function() self:_syncCanvas() end)
end

function AetherUI:Canvas()
	return self.stack
end

function AetherUI:GetTheme()
	return self.themeId
end

function AetherUI:GetThemes()
	local list = {}
	for _, id in ipairs(THEME_ORDER) do
		local t = THEMES[id]
		table.insert(list, { id = id, name = t.name, meta = t.meta, hint = t.hint, spec = t.spec, accent = t.accent })
	end
	return list
end

function AetherUI:OnThemeChanged(callback)
	table.insert(self._themeListeners, callback)
	return function()
		local index = table.find(self._themeListeners, callback)
		if index then table.remove(self._themeListeners, index) end
	end
end

function AetherUI:SetTheme(id)
	if not THEMES[id] or id == self.themeId then return end
	local previous = self.page.CanvasPosition.Y
	self.themeId = id
	self.t = THEMES[id]
	self:Render()
	task.defer(function()
		self.page.CanvasPosition = Vector2.new(0, previous)
	end)

	for _, listener in ipairs(self._themeListeners) do
		local ok, err = pcall(listener, id, THEMES[id])
		if not ok then warn("AetherUI theme listener failed: " .. tostring(err)) end
	end
	if self.config.OnThemeChanged then
		local ok, err = pcall(self.config.OnThemeChanged, id, THEMES[id])
		if not ok then warn("AetherUI OnThemeChanged failed: " .. tostring(err)) end
	end
end

function AetherUI:ShuffleTheme()
	local options = {}
	for _, id in ipairs(THEME_ORDER) do
		if id ~= self.themeId then table.insert(options, id) end
	end
	if #options == 0 then return end
	self:SetTheme(options[math.random(1, #options)])
end

function AetherUI:ScrollTo(section)
	local target = self.sections[section]
	if not target then return end
	local pageTop = self.page.AbsolutePosition.Y
	local offset = (target.AbsolutePosition.Y - pageTop) - (self.inset + NAV_HEIGHT * self.scale.Scale) + 20
	local goal = Vector2.new(0, math.max(0, offset))
	if self.options.Motion == false then
		self.page.CanvasPosition = goal
		return
	end
	TweenService:Create(
		self.page,
		TweenInfo.new(0.65, Enum.EasingStyle.Quint, Enum.EasingDirection.Out),
		{ CanvasPosition = goal }
	):Play()
end

function AetherUI:OpenLink(url)
	if type(url) ~= "string" or url == "" then
		self:Toast("No link configured for this action.")
		return
	end
	if self.config.OnLink then
		local ok, err = pcall(self.config.OnLink, url)
		if not ok then self:Toast(tostring(err)) end
		return
	end
	local ok = pcall(function()
		GuiService:OpenBrowserWindow(url)
	end)
	if not ok then self:Toast(url) end
end

function AetherUI:Toast(message)
	self._toastToken = self._toastToken + 1
	local token = self._toastToken
	self.toastText.Text = tostring(message)
	self.toast.Visible = true
	self.toast.GroupTransparency = 1

	TweenService:Create(
		self.toast,
		TweenInfo.new(0.28, Enum.EasingStyle.Quad, Enum.EasingDirection.Out),
		{ GroupTransparency = 0, Position = UDim2.new(1, -20, 1, -20) }
	):Play()

	task.delay(4.4, function()
		if self._toastToken ~= token then return end
		local tween = TweenService:Create(
			self.toast,
			TweenInfo.new(0.24, Enum.EasingStyle.Quad, Enum.EasingDirection.In),
			{ GroupTransparency = 1, Position = UDim2.new(1, -20, 1, 4) }
		)
		tween.Completed:Once(function()
			if self._toastToken == token then self.toast.Visible = false end
		end)
		tween:Play()
	end)
end

function AetherUI:Destroy()
	self:_disconnect()
	for _, connection in ipairs(self._permanentConnections) do
		if typeof(connection) == "RBXScriptConnection" then connection:Disconnect() end
	end
	self._permanentConnections = {}
	self._themeListeners = {}
	if self.gui then self.gui:Destroy() end
	self.gui = nil
end

-- ------------------------------------------------------------------ primitives
-- Public helpers. Everything returns a real Roblox GuiObject you can keep
-- styling yourself; style keys are applied before the object is parented.

function AetherUI:Frame(props)
	props = props or {}
	local frame = Instance.new("Frame")
	frame.Name = props.Name or "Frame"
	frame.BackgroundTransparency = 1
	frame.BorderSizePixel = 0
	frame.Size = props.Size or UDim2.new(1, 0, 0, 0)
	frame.Position = props.Position or UDim2.new(0, 0, 0, 0)
	frame.AnchorPoint = props.AnchorPoint or Vector2.new(0, 0)
	frame.ZIndex = props.ZIndex or 1
	frame.LayoutOrder = props.LayoutOrder or self:_nextOrder()
	frame.ClipsDescendants = props.ClipsDescendants or false
	frame.Visible = props.Visible ~= false
	frame.Rotation = props.Rotation or 0
	if props.AutomaticSize then frame.AutomaticSize = props.AutomaticSize end
	if props.Color then
		frame.BackgroundColor3 = self:Color(props.Color)
		frame.BackgroundTransparency = props.BackgroundTransparency or 0
	elseif props.BackgroundTransparency then
		frame.BackgroundTransparency = props.BackgroundTransparency
	end
	applyCorner(frame, props.Radius)
	if props.Stroke then
		applyStroke(frame, self:Color(props.Stroke), props.StrokeThickness, props.StrokeTransparency)
	end
	if props.Parent then frame.Parent = props.Parent end
	return frame
end

function AetherUI:Panel(props)
	props = props or {}
	props.Color = props.Color or "panel2"
	props.Stroke = props.Stroke or "line"
	props.Radius = props.Radius == nil and self.t.radius or props.Radius
	return self:Frame(props)
end

function AetherUI:Rule(props)
	props = props or {}
	local rule = Instance.new("Frame")
	rule.Name = props.Name or "Rule"
	rule.Size = props.Size or UDim2.new(1, 0, 0, 1)
	rule.BackgroundColor3 = self:Color(props.Color or "line")
	rule.BackgroundTransparency = props.Transparency or 0
	rule.BorderSizePixel = 0
	rule.Position = props.Position or UDim2.new(0, 0, 0, 0)
	rule.LayoutOrder = props.LayoutOrder or self:_nextOrder()
	rule.Parent = props.Parent
	return rule
end

function AetherUI:Spacer(height, parent)
	if type(height) == "table" then
		parent = height.Parent or parent
		height = height.Height or 0
	end
	return self:Frame({
		Parent = parent,
		Name = "Spacer",
		Size = UDim2.new(1, 0, 0, height or 0),
		BackgroundTransparency = 1,
	})
end

function AetherUI:Text(props)
	props = props or {}
	local style = self.styles[props.Style or "Body"] or self.styles.Body
	local label = Instance.new("TextLabel")
	label.Name = props.Name or "Text"
	label.BackgroundTransparency = 1
	label.BorderSizePixel = 0
	label.Font = self:Font(props.Font or style.font)
	label.TextSize = props.TextSize or style.size
	label.TextColor3 = self:Color(props.Color or style.color)
	label.TextXAlignment = props.Align or Enum.TextXAlignment.Left
	label.TextYAlignment = props.VerticalAlign or Enum.TextYAlignment.Top
	label.TextWrapped = props.Wrapped ~= false
	label.LineHeight = props.LineHeight or style.lineHeight or 1
	label.RichText = props.RichText or false
	label.Text = props.Text or ""
	if props.Upper and not props.RichText then
		label.Text = string.upper(label.Text)
	end
	label.TextTransparency = props.Transparency or 0
	label.Size = props.Size or UDim2.new(1, 0, 0, 0)
	label.AutomaticSize = props.AutomaticSize or Enum.AutomaticSize.Y
	label.LayoutOrder = props.LayoutOrder or self:_nextOrder()
	label.ZIndex = props.ZIndex or 1
	label.Parent = props.Parent
	return label
end

function AetherUI:Row(props)
	props = props or {}
	local frame = self:Frame({
		Parent = props.Parent,
		Name = props.Name or "Row",
		Size = props.Size or UDim2.new(1, 0, 0, props.Height or 0),
		Color = props.Color,
		BackgroundTransparency = props.BackgroundTransparency,
		Radius = props.Radius,
		Stroke = props.Stroke,
		ClipsDescendants = props.ClipsDescendants,
		ZIndex = props.ZIndex,
		LayoutOrder = props.LayoutOrder,
	})
	if props.Height == nil then
		frame.AutomaticSize = Enum.AutomaticSize.Y
	end
	local layout = Instance.new("UIListLayout")
	layout.FillDirection = Enum.FillDirection.Horizontal
	layout.Padding = UDim.new(0, props.Gap or 0)
	layout.SortOrder = Enum.SortOrder.LayoutOrder
	layout.VerticalAlignment = props.VerticalAlignment or Enum.VerticalAlignment.Top
	layout.HorizontalAlignment = props.HorizontalAlignment or Enum.HorizontalAlignment.Left
	layout.Parent = frame
	return frame
end

function AetherUI:Col(props)
	props = props or {}
	local frame = self:Frame({
		Parent = props.Parent,
		Name = props.Name or "Col",
		Size = props.Size or UDim2.new(1, 0, 0, props.Height or 0),
		Color = props.Color,
		BackgroundTransparency = props.BackgroundTransparency,
		Radius = props.Radius,
		Stroke = props.Stroke,
		ClipsDescendants = props.ClipsDescendants,
		ZIndex = props.ZIndex,
		LayoutOrder = props.LayoutOrder,
	})
	if props.Height == nil then
		frame.AutomaticSize = Enum.AutomaticSize.Y
	end
	local layout = Instance.new("UIListLayout")
	layout.FillDirection = Enum.FillDirection.Vertical
	layout.Padding = UDim.new(0, props.Gap or 0)
	layout.SortOrder = Enum.SortOrder.LayoutOrder
	layout.HorizontalAlignment = props.Align or Enum.HorizontalAlignment.Left
	layout.VerticalAlignment = props.VerticalAlign or Enum.VerticalAlignment.Top
	layout.Parent = frame
	return frame
end

function AetherUI:Grid(props)
	props = props or {}
	local columns = props.Columns or 2
	local gap = props.Gap or 24
	local cellHeight = props.CellHeight or 120
	local frame = self:Frame({
		Parent = props.Parent,
		Name = props.Name or "Grid",
		Size = UDim2.new(1, 0, 0, 0),
		AutomaticSize = Enum.AutomaticSize.Y,
		LayoutOrder = props.LayoutOrder,
	})
	local grid = Instance.new("UIGridLayout")
	grid.CellSize = UDim2.new(1 / columns, -(gap * (columns - 1)) / columns, 0, cellHeight)
	grid.CellPadding = UDim2.new(0, gap, 0, props.RowGap or 0)
	grid.SortOrder = Enum.SortOrder.LayoutOrder
	grid.FillDirectionMaxCells = columns
	grid.Parent = frame
	return frame
end

function AetherUI:Button(props)
	props = props or {}
	local variant = props.Variant or "Primary"
	local height = props.Height or 46
	local fontSize = props.TextSize or 13
	local font = self:Font(props.Font or "sansBold")
	local label = props.Text or ""
	local padding = props.Padding or 22
	local arrow = props.Arrow
	local arrowWidth = arrow and 22 or 0
	local width = props.Width or math.ceil(self:_measure(label, fontSize, font) + padding * 2 + arrowWidth)

	local base, fg, hoverBase, hoverFg, stroke
	if variant == "Primary" then
		base, fg, hoverBase, hoverFg, stroke = self.t.accent, self.t.accentInk, self.t.accentHi, self.t.accentInk, self.t.accent
	elseif variant == "Link" then
		fg, hoverFg = self.t.textMid, self.t.text
	else
		fg, hoverBase, hoverFg, stroke = self.t.text, self.t.panel, self.t.text, self.t.line
	end

	local button = Instance.new("TextButton")
	button.Name = props.Name or "Button"
	button.AutoButtonColor = false
	button.Size = UDim2.fromOffset(width, variant == "Link" and (height or 26) or height)
	button.BackgroundColor3 = base or self.t.panel
	button.BackgroundTransparency = base and 0 or 1
	button.BorderSizePixel = 0
	button.Font = font
	button.TextSize = fontSize
	button.TextColor3 = fg
	button.Text = label
	button.TextXAlignment = arrow and Enum.TextXAlignment.Left or Enum.TextXAlignment.Center
	button.TextYAlignment = Enum.TextYAlignment.Center
	button.LayoutOrder = props.LayoutOrder or self:_nextOrder()
	button.ZIndex = props.ZIndex or 1

	if variant ~= "Link" then
		applyCorner(button, self.t.buttonPill and "pill" or self.t.radiusSm)
		applyStroke(button, stroke or self.t.line)
		applyPadding(button, 0, padding, 0, padding)
	end

	local underline
	if variant == "Link" then
		underline = Instance.new("Frame")
		underline.Name = "Underline"
		underline.Size = UDim2.new(1, 0, 0, 1)
		underline.Position = UDim2.new(0, 0, 1, -1)
		underline.BackgroundColor3 = self.t.line
		underline.BorderSizePixel = 0
		underline.Parent = button
	end

	local glyph
	if arrow then
		glyph = Instance.new("TextLabel")
		glyph.BackgroundTransparency = 1
		glyph.Size = UDim2.fromOffset(14, 14)
		glyph.Position = UDim2.new(1, -padding, 0.5, 0)
		glyph.AnchorPoint = Vector2.new(1, 0.5)
		glyph.Font = FONTS.arrow
		glyph.TextSize = 14
		glyph.Text = arrow == "down" and "↓" or (arrow == "up" and "↑" or "↗")
		glyph.TextColor3 = fg
		glyph.Parent = button
	end

	local tweenInfo = TweenInfo.new(0.18, Enum.EasingStyle.Quad, Enum.EasingDirection.Out)
	button.MouseEnter:Connect(function()
		if hoverBase then TweenService:Create(button, tweenInfo, { BackgroundColor3 = hoverBase }):Play() end
		if hoverFg then TweenService:Create(button, tweenInfo, { TextColor3 = hoverFg }):Play() end
		if underline then TweenService:Create(underline, tweenInfo, { BackgroundColor3 = self.t.accent }):Play() end
		if glyph then TweenService:Create(glyph, tweenInfo, { TextColor3 = hoverFg or fg }):Play() end
	end)
	button.MouseLeave:Connect(function()
		if base then TweenService:Create(button, tweenInfo, { BackgroundColor3 = base }):Play() end
		if hoverFg then
			local target = variant == "Link" and self.t.textMid or (base and self.t.accentInk or self.t.text)
			TweenService:Create(button, tweenInfo, { TextColor3 = target }):Play()
		end
		if underline then TweenService:Create(underline, tweenInfo, { BackgroundColor3 = self.t.line }):Play() end
		if glyph then TweenService:Create(glyph, tweenInfo, { TextColor3 = fg }):Play() end
	end)
	button.Activated:Connect(function()
		if props.OnClick then props.OnClick() end
	end)
	button.Parent = props.Parent
	return button
end

function AetherUI:Link(props)
	props = props or {}
	props.Variant = "Link"
	props.Font = props.Font or "sans"
	props.TextSize = props.TextSize or 13
	props.Height = props.Height or 26
	if props.Arrow == nil then props.Arrow = "down" end
	return self:Button(props)
end

function AetherUI:Chip(props)
	props = props or {}
	local text = props.Text or ""
	local fontSize = props.TextSize or 10
	local width = props.Width or math.ceil(self:_measure(text, fontSize, FONTS.mono) + 16)
	local chip = self:Frame({
		Parent = props.Parent,
		Name = props.Name or "Chip",
		Size = UDim2.fromOffset(width, props.Height or 22),
		Color = props.Color or "panel",
		Stroke = props.Stroke or "line",
		Radius = 3,
		LayoutOrder = props.LayoutOrder,
	})
	local label = self:Text({
		Parent = chip,
		Text = text,
		Style = "Label",
		TextSize = fontSize,
		Color = props.TextColor or "accentHi",
		Align = Enum.TextXAlignment.Center,
		Size = UDim2.fromScale(1, 1),
		AutomaticSize = Enum.AutomaticSize.None,
		Wrapped = false,
		VerticalAlign = Enum.TextYAlignment.Center,
	})
	label.ZIndex = 2
	return chip
end

-- ------------------------------------------------------------------ content

local FEATURES = {
	{ "Accounts", "Save sessions, check them and launch as any of them. Keys are encrypted with Windows DPAPI and sent only to Roblox." },
	{ "Several clients", "A watcher holds the Roblox singleton while clients run — even after Aether is closed. Start the same place on every account." },
	{ "Exact versions", "Pin the client another tool expects, or take the newest. Older builds come from WEAO and Roblox's own deploy history." },
	{ "Honest FastFlags", "66 presets and 91 flags in seven groups. Every suggestion says whether Roblox still reads it — no guessing after a restart." },
	{ "A real overlay", "Six separate panels over the game for frame rate, graphics, notes, servers, anti-AFK and the menu. Drag them anywhere." },
	{ "Quiet by default", "Anti-AFK sends real input when Roblox is in front and a posted key tap when it is not. It never pulls the window forward." },
	{ "Vanilla switch", "Turn Aether off and the client is exactly as Roblox installed it — ready for Velocity, Madium or Volt." },
	{ "Nothing leaves", "Games, pinned favourites, notes, fonts and panel positions live under %LOCALAPPDATA%. No account data is uploaded." },
}

local PANELS = {
	{ "Frame rate", "Uncapped, fixed or your own value, with an honest \"apply & rejoin\"." },
	{ "Graphics", "Quality, textures, anti-aliasing and grass on the flags Roblox still reads." },
	{ "Notes", "The same notes file as the launcher, saved while you type." },
	{ "Server", "Leave and rejoin a fresh server without landing on the desktop." },
	{ "Anti-AFK", "A live countdown, a quiet background tap and a self-check that proves input works." },
	{ "Menu", "Every panel on or off, the key itself, opacity and a one-click layout reset." },
}

local TICKER = {
	"Accounts", "Any installed version", "91 FastFlags", "12 palettes",
	"6 overlay panels", "No installer", "DPAPI-encrypted keys",
}

local NAV_LINKS = {
	{ number = "01", label = "Toolbox", target = "features" },
	{ number = "02", label = "Overlay", target = "overlay" },
	{ number = "03", label = "Editions", target = "themes" },
	{ number = "04", label = "Download", target = "download" },
}

-- ------------------------------------------------------------------ bits

local function up(app, value)
	if app.t.upper then return string.upper(value) end
	return value
end

local function accentTag(app, value)
	return '<font color="' .. app.t.accent:ToHex() .. '">' .. value .. '</font>'
end

local function em(app, value)
	return "<i>" .. accentTag(app, value) .. "</i>"
end

local function kicker(app, parent, number, label)
	return app:Text({
		Parent = parent,
		Text = accentTag(app, number) .. "  " .. up(app, label),
		Style = "Label",
		RichText = true,
		Wrapped = false,
	})
end

local function section(app, id, top)
	local frame = app:Frame({
		Parent = app.stack,
		Name = id or "Section",
		Size = UDim2.new(1, 0, 0, 0),
		AutomaticSize = Enum.AutomaticSize.Y,
	})
	applyPadding(frame, top or app.t.sectionGap, GUTTER, 0, GUTTER)
	local col = app:Col({ Parent = frame })
	if id then app.sections[id] = frame end
	return frame, col
end

local function navLink(app, parent, number, label, onClick)
	local plain = (number and (number .. "   ") or "") .. string.upper(label)
	local width = math.ceil(app:_measure(plain, 12, FONTS.sans)) + 4
	local button = Instance.new("TextButton")
	button.Name = label
	button.AutoButtonColor = false
	button.BackgroundTransparency = 1
	button.BorderSizePixel = 0
	button.Size = UDim2.fromOffset(width, 30)
	button.Font = FONTS.sans
	button.TextSize = 12
	button.TextColor3 = app.t.textMid
	button.TextYAlignment = Enum.TextYAlignment.Center
	button.RichText = true
	if number then
		button.Text = '<font color="' .. app.t.textDim:ToHex() .. '">' .. number .. "</font>   " .. label
	else
		button.Text = label
	end
	button.Parent = parent
	button.MouseEnter:Connect(function()
		TweenService:Create(button, TweenInfo.new(0.2), { TextColor3 = app.t.text }):Play()
	end)
	button.MouseLeave:Connect(function()
		TweenService:Create(button, TweenInfo.new(0.2), { TextColor3 = app.t.textMid }):Play()
	end)
	button.Activated:Connect(onClick)
	return button
end

buildNav = function(app)
	local t = app.t
	local version = app.config.Version or "1.1.0"
	local nav = app:Frame({
		Parent = app.navInner,
		Name = "NavRow",
		Size = UDim2.new(1, 0, 1, 0),
		BackgroundTransparency = 1,
	})
	applyPadding(nav, 0, GUTTER, 0, GUTTER)

	local brand = Instance.new("TextButton")
	brand.Name = "Brand"
	brand.AutoButtonColor = false
	brand.BackgroundTransparency = 1
	brand.Text = ""
	brand.Size = UDim2.fromOffset(300, NAV_HEIGHT)
	brand.Parent = nav
	brand.Activated:Connect(function()
		app.page.CanvasPosition = Vector2.new(0, 0)
	end)

	local logo = app:Frame({
		Parent = brand,
		Size = UDim2.fromOffset(24, 24),
		Position = UDim2.fromOffset(0, 22),
		Color = "accent",
		Radius = 5,
	})
	app:Text({
		Parent = logo, Text = "A", Font = "sansBlack", TextSize = 13, Color = "accentInk",
		Align = Enum.TextXAlignment.Center, Size = UDim2.fromScale(1, 1),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	})

	local nameWidth = math.ceil(app:_measure("Aether", 20, t.display))
	app:Text({
		Parent = brand, Text = "Aether", Style = "H3", TextSize = 20, Font = t.display,
		Size = UDim2.fromOffset(nameWidth + 6, 26), Position = UDim2.fromOffset(33, 21),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	})
	app:Frame({
		Parent = brand, Size = UDim2.fromOffset(1, 14),
		Position = UDim2.fromOffset(33 + nameWidth + 8, 27), Color = "line",
	})
	app:Text({
		Parent = brand, Text = "v" .. version, Style = "Label",
		Size = UDim2.fromOffset(80, 26), Position = UDim2.fromOffset(33 + nameWidth + 16, 21),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	})

	local links = Instance.new("Frame")
	links.Name = "Links"
	links.BackgroundTransparency = 1
	links.AnchorPoint = Vector2.new(1, 0.5)
	links.Position = UDim2.new(1, 0, 0.5, 0)
	links.Size = UDim2.fromOffset(0, 34)
	links.AutomaticSize = Enum.AutomaticSize.X
	links.Parent = nav
	local linkLayout = Instance.new("UIListLayout")
	linkLayout.FillDirection = Enum.FillDirection.Horizontal
	linkLayout.Padding = UDim.new(0, 24)
	linkLayout.SortOrder = Enum.SortOrder.LayoutOrder
	linkLayout.VerticalAlignment = Enum.VerticalAlignment.Center
	linkLayout.Parent = links

	for _, item in ipairs(NAV_LINKS) do
		navLink(app, links, item.number, item.label, function()
			app:ScrollTo(item.target)
		end)
	end

	app:Frame({ Parent = links, Size = UDim2.fromOffset(1, 14), Color = "line" })
	navLink(app, links, nil, "GITHUB", function()
		app:OpenLink(app.config.GitHubUrl or "https://github.com")
	end)
end
local function download(app)
	if app.config.OnDownload then
		local ok, err = pcall(app.config.OnDownload)
		if not ok then app:Toast(tostring(err)) end
		return
	end
	if app.config.DownloadUrl then
		app:OpenLink(app.config.DownloadUrl)
		return
	end
	app:Toast("Hook Config.OnDownload or Config.DownloadUrl to wire this up.")
end

local function shotFrame(app, parent, opts)
	opts = opts or {}
	local offset = opts.offset or 0
	local radius = opts.radius or 0
	local shadow = app:Frame({
		Parent = parent,
		Name = "Shadow",
		Size = UDim2.new(1, 0, 1, 0),
		Position = UDim2.fromOffset(offset, offset),
		Color = "lineSoft",
		Radius = radius,
		ZIndex = 1,
	})
	if offset <= 0 then shadow.Visible = false end

	local main = app:Frame({
		Parent = parent,
		Name = "Shot",
		Size = UDim2.new(1, 0, 1, 0),
		Color = "panel",
		Stroke = "line",
		Radius = radius,
		Rotation = opts.angle or 0,
		ZIndex = 2,
	})
	applyPadding(main, 8, 8, 8, 8)

	app:Text({
		Parent = main,
		Text = opts.label or "",
		Style = "Label",
		Wrapped = false,
		Size = UDim2.new(1, 0, 0, 14),
		AutomaticSize = Enum.AutomaticSize.None,
	})
	app:Text({
		Parent = main,
		Text = opts.badge or "",
		Style = "Label",
		Color = "accent",
		Align = Enum.TextXAlignment.Right,
		Wrapped = false,
		Size = UDim2.new(1, 0, 0, 14),
		AutomaticSize = Enum.AutomaticSize.None,
	}).ZIndex = 3

	return app:Frame({
		Parent = main,
		Name = "ShotBody",
		Size = UDim2.new(1, 0, 1, -26),
		Position = UDim2.fromOffset(0, 22),
		Color = "bgDeep",
		Stroke = "lineSoft",
		ClipsDescendants = true,
		ZIndex = 2,
	})
end

local function mockLauncher(app, parent)
	local t = app.t
	local window = app:Frame({ Parent = parent, Size = UDim2.fromScale(1, 1), Color = "bgDeep" })

	local header = app:Frame({ Parent = window, Size = UDim2.new(1, 0, 0, 30), Color = "panel2" })
	applyPadding(header, 0, 10, 0, 10)
	for index = 1, 3 do
		app:Frame({
			Parent = header,
			Size = UDim2.fromOffset(6, 6),
			Position = UDim2.fromOffset((index - 1) * 12, 12),
			Color = index == 1 and "accent" or "line",
			Radius = "pill",
		})
	end
	app:Text({
		Parent = header, Text = "aether   /   home", Style = "Label",
		Size = UDim2.new(0, 220, 0, 30), Position = UDim2.fromOffset(44, 0),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		VerticalAlign = Enum.TextYAlignment.Center,
	})
	app:Text({
		Parent = header, Text = "LIVE", Style = "Label", Color = "accent",
		Align = Enum.TextXAlignment.Right, Size = UDim2.new(1, 0, 0, 30),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		VerticalAlign = Enum.TextYAlignment.Center,
	})

	local body = app:Frame({
		Parent = window,
		Size = UDim2.new(1, 0, 1, -30),
		Position = UDim2.fromOffset(0, 30),
		Color = "bgDeep",
	})
	local rail = app:Frame({ Parent = body, Size = UDim2.new(0, 46, 1, 0), Color = "panel" })
	for index = 1, 4 do
		app:Frame({
			Parent = rail,
			Size = UDim2.fromOffset(26, 26),
			Position = UDim2.fromOffset(10, 12 + (index - 1) * 34),
			Color = index == 1 and "accentSoft" or "panel2",
			Stroke = index == 1 and "accent" or nil,
			Radius = 4,
		})
	end

	local main = app:Frame({
		Parent = body,
		Size = UDim2.new(1, -46, 1, 0),
		Position = UDim2.fromOffset(46, 0),
	})
	applyPadding(main, 14, 16, 14, 16)
	local mainLayout = Instance.new("UIListLayout")
	mainLayout.SortOrder = Enum.SortOrder.LayoutOrder
	mainLayout.Padding = UDim.new(0, 0)
	mainLayout.Parent = main

	app:Text({ Parent = main, Text = "PINNED", Style = "Label", Wrapped = false })
	app:Spacer(8, main)

	local pinned = app:Frame({
		Parent = main,
		Size = UDim2.new(1, 0, 0, 104),
		Color = "accentSoft",
		Stroke = "accentDeep",
		Radius = math.max(t.radiusSm, 4),
	})
	applyPadding(pinned, 12, 14, 12, 14)
	local pinnedLayout = Instance.new("UIListLayout")
	pinnedLayout.SortOrder = Enum.SortOrder.LayoutOrder
	pinnedLayout.Parent = pinned
	app:Text({ Parent = pinned, Text = "AETHER / HOME", Style = "Label", Wrapped = false })
	app:Spacer(6, pinned)
	app:Text({ Parent = pinned, Text = "Your pinned place", Style = "H3", TextSize = 17, Wrapped = false })
	app:Text({ Parent = pinned, Text = "Pinned game   ·   last played 4m ago", Style = "Fine", Wrapped = false })
	local play = app:Frame({
		Parent = pinned,
		Size = UDim2.fromOffset(54, 22),
		Position = UDim2.new(1, 0, 0, 0),
		AnchorPoint = Vector2.new(1, 0),
		Color = "accent",
		Radius = 2,
	})
	app:Text({
		Parent = play, Text = "PLAY", Font = "mono", TextSize = 8, Color = "accentInk",
		Align = Enum.TextXAlignment.Center, Size = UDim2.fromScale(1, 1),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		VerticalAlign = Enum.TextYAlignment.Center,
	})

	app:Spacer(14, main)
	app:Text({ Parent = main, Text = "LIBRARY", Style = "Label", Wrapped = false })
	app:Spacer(2, main)
	for index = 1, 3 do
		local row = app:Frame({ Parent = main, Size = UDim2.new(1, 0, 0, 36) })
		app:Frame({
			Parent = row, Size = UDim2.fromOffset(22, 22), Position = UDim2.fromOffset(0, 7),
			Color = "panel2", Stroke = "lineSoft", Radius = 3,
		})
		app:Text({
			Parent = row, Text = "Saved place " .. index, Style = "Small", Color = "text",
			Size = UDim2.new(1, -70, 0, 36), Position = UDim2.fromOffset(32, 0),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
			VerticalAlign = Enum.TextYAlignment.Center,
		})
		app:Text({
			Parent = row, Text = "PLAY", Style = "Label", Color = "accent",
			Align = Enum.TextXAlignment.Right, Size = UDim2.fromOffset(40, 36),
			Position = UDim2.new(1, -40, 0, 0), AutomaticSize = Enum.AutomaticSize.None,
			Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
		})
		if index < 3 then
			app:Rule({
				Parent = row, Color = "lineSoft",
				Size = UDim2.new(1, -32, 0, 1), Position = UDim2.new(0, 32, 1, -1),
			})
		end
	end
end

local function buildCopy(app, parent, wide, size)
	local version = app.config.Version or "1.1.0"
	local copy = app:Col({
		Parent = parent,
		Size = size or UDim2.new(1, 0, 0, 0),
		Align = wide and Enum.HorizontalAlignment.Center or Enum.HorizontalAlignment.Left,
	})

	local eyebrow = app:Row({ Parent = copy, Gap = 9, VerticalAlignment = Enum.VerticalAlignment.Center })
	local mark = app:Frame({ Parent = eyebrow, Size = UDim2.fromOffset(25, 22), Stroke = "line", Radius = 2 })
	app:Text({
		Parent = mark, Text = "00", Style = "Label", Color = "accent",
		Align = Enum.TextXAlignment.Center, Size = UDim2.fromScale(1, 1),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		VerticalAlign = Enum.TextYAlignment.Center,
	})
	local eyebrowLabel = "A ROBLOX LAUNCHER FOR WINDOWS"
	app:Text({
		Parent = eyebrow, Text = eyebrowLabel, Style = "Label", Wrapped = false,
		Size = UDim2.fromOffset(math.ceil(app:_measure(eyebrowLabel, 9, FONTS.mono)) + 4, 22),
		AutomaticSize = Enum.AutomaticSize.None,
		VerticalAlign = Enum.TextYAlignment.Center,
	})

	app:Spacer(20, copy)
	app:Text({
		Parent = copy,
		Text = up(app, "Roblox, without") .. "\n" .. em(app, up(app, "the busywork.")),
		Style = "H1",
		RichText = true,
		Align = wide and Enum.TextXAlignment.Center or Enum.TextXAlignment.Left,
	})
	app:Spacer(24, copy)
	app:Text({
		Parent = copy,
		Style = "Lede",
		Text = "Accounts, installed versions, FastFlags and a real in-game overlay — kept in one quiet window. Aether runs on your PC, not in a browser tab, and never asks for an account of its own.",
		Size = UDim2.new(0, wide and 760 or 600, 0, 0),
		Align = wide and Enum.TextXAlignment.Center or Enum.TextXAlignment.Left,
	})
	app:Spacer(30, copy)

	local cta = app:Row({
		Parent = copy,
		Gap = 22,
		VerticalAlignment = Enum.VerticalAlignment.Center,
		HorizontalAlignment = wide and Enum.HorizontalAlignment.Center or Enum.HorizontalAlignment.Left,
	})
	app:Button({
		Parent = cta,
		Text = "Download for Windows",
		Arrow = "down",
		Variant = "Primary",
		OnClick = function() download(app) end,
	})
	app:Button({
		Parent = cta,
		Text = "See what it does",
		Arrow = "down",
		Variant = "Link",
		OnClick = function() app:ScrollTo("features") end,
	})

	app:Spacer(18, copy)
	app:Text({
		Parent = copy,
		Style = "Fine",
		Text = "v" .. version .. "   ·   Windows 10/11, x64   ·   MIT licence",
		Align = wide and Enum.TextXAlignment.Center or Enum.TextXAlignment.Left,
		Wrapped = false,
	})
end

local function buildVisual(app, parent, options)
	options = options or {}
	local wrap = app:Col({ Parent = parent, Size = options.Size or UDim2.new(1, 0, 0, 0) })
	local holder = app:Frame({ Parent = wrap, Size = UDim2.new(1, 0, 0, 466) })
	local target = holder
	if options.wide then
		target = app:Frame({
			Parent = holder,
			Size = UDim2.new(0.86, 0, 1, 0),
			Position = UDim2.new(0.5, 0, 0, 0),
			AnchorPoint = Vector2.new(0.5, 0),
		})
	end
	local content = shotFrame(app, target, {
		label = "AETHER.EXE / HOME",
		badge = "LIVE VIEW",
		angle = app.t.frameAngle,
		offset = app.t.frameOffset,
	})
	mockLauncher(app, content)

	local key = app:Frame({
		Parent = target,
		Size = UDim2.fromOffset(50, 44),
		Position = UDim2.new(0, -12, 1, -54),
		Color = "accent",
		Radius = 3,
		Rotation = -4,
		ZIndex = 5,
	})
	app:Text({
		Parent = key, Text = "DELETE", Font = "mono", TextSize = 10, Color = "accentInk",
		Align = Enum.TextXAlignment.Center, Size = UDim2.new(1, 0, 0, 22),
		Position = UDim2.fromOffset(0, 6), AutomaticSize = Enum.AutomaticSize.None,
		Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	}).ZIndex = 6
	app:Text({
		Parent = key, Text = "OVERLAY", Font = "mono", TextSize = 6, Color = "accentInk",
		Align = Enum.TextXAlignment.Center, Size = UDim2.new(1, 0, 0, 10),
		Position = UDim2.fromOffset(0, 26), AutomaticSize = Enum.AutomaticSize.None,
		Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	}).ZIndex = 6

	app:Spacer(15, wrap)
	local caption = app:Row({ Parent = wrap, Gap = 9 })
	app:Text({
		Parent = caption, Text = options.index or "01", Style = "Label", Color = "accent",
		Size = UDim2.fromOffset(20, 18), AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
	})
	app:Text({
		Parent = caption,
		Text = "The launcher at rest: a pinned game, your library and the last sessions.",
		Style = "Fine",
		Size = UDim2.new(1, -29, 0, 0),
	})
end

local function buildHero(app)
	local t = app.t
	local _, col = section(app, "top", 38)
	app:Rule({ Parent = col, Color = "line" })

	local meta = app:Frame({ Parent = col, Size = UDim2.new(1, 0, 0, 34) })
	app:Text({
		Parent = meta,
		Text = "WINDOWS 10 / 11     ·     64-BIT     ·     OPEN SOURCE     ·     NO INSTALLER",
		Style = "Label", Wrapped = false, Color = "textDim",
		Size = UDim2.new(0, 820, 0, 34), AutomaticSize = Enum.AutomaticSize.None,
		VerticalAlign = Enum.TextYAlignment.Center,
	})
	app:Text({
		Parent = meta, Text = "AETHER / 1.0", Style = "Label", Color = "accent",
		Align = Enum.TextXAlignment.Right, Wrapped = false,
		Size = UDim2.new(1, 0, 0, 34), AutomaticSize = Enum.AutomaticSize.None,
		VerticalAlign = Enum.TextYAlignment.Center,
	})
	app:Rule({ Parent = col, Color = "line" })
	app:Spacer(56, col)

	if t.heroCenter then
		buildCopy(app, col, true)
		app:Spacer(56, col)
		buildVisual(app, col, { Size = UDim2.new(1, 0, 0, 0), wide = true })
	else
		local row = app:Row({ Parent = col, Gap = 72, VerticalAlignment = Enum.VerticalAlignment.Bottom })
		local copySize = UDim2.new(0.56, 0, 0, 0)
		local visualSize = UDim2.new(0.44, -72, 0, 0)
		if t.heroMirror then
			buildVisual(app, row, { Size = visualSize })
			buildCopy(app, row, false, copySize)
		else
			buildCopy(app, row, false, copySize)
			buildVisual(app, row, { Size = visualSize })
		end
	end

	app:Spacer(72, col)
	app:Rule({ Parent = col, Color = "line" })
	local stats = app:Row({ Parent = col, Height = 76 })
	local version = app.config.Version or "1.1.0"
	local entries = {
		{ version, "current build" },
		{ "—", "release downloads" },
		{ "91", "FastFlags" },
		{ "12", "palettes" },
		{ "6", "overlay panels" },
	}
	for index, entry in ipairs(entries) do
		local cell = app:Frame({ Parent = stats, Size = UDim2.new(0.2, 0, 1, 0) })
		local inset = index > 1 and 14 or 0
		if index > 1 then
			app:Frame({
				Parent = cell, Size = UDim2.new(0, 1, 1, -24),
				Position = UDim2.fromOffset(0, 12), Color = "lineSoft",
			})
		end
		app:Text({
			Parent = cell, Text = entry[1], Style = "Display", TextSize = 30,
			Size = UDim2.new(1, -inset, 0, 34), Position = UDim2.fromOffset(inset, 12),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
			VerticalAlign = Enum.TextYAlignment.Center,
		})
		app:Text({
			Parent = cell, Text = string.upper(entry[2]), Style = "Label",
			Size = UDim2.new(1, -inset, 0, 20), Position = UDim2.fromOffset(inset, 46),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		})
	end
	app:Rule({ Parent = col, Color = "line" })
end

local function buildTicker(app)
	app:Spacer(54, app.stack)
	app:Rule({ Parent = app.stack, Color = "line" })
	local ticker = app:Frame({
		Parent = app.stack,
		Size = UDim2.new(1, 0, 0, 40),
		Color = "panel",
		ClipsDescendants = true,
	})
	app:Rule({ Parent = app.stack, Color = "line" })

	local track = app:Frame({ Parent = ticker, Size = UDim2.new(0, 0, 0, 40), BackgroundTransparency = 1 })
	track.AutomaticSize = Enum.AutomaticSize.X
	local trackLayout = Instance.new("UIListLayout")
	trackLayout.FillDirection = Enum.FillDirection.Horizontal
	trackLayout.SortOrder = Enum.SortOrder.LayoutOrder
	trackLayout.Parent = track

	local groups = {}
	for groupIndex = 1, 2 do
		local group = app:Frame({ Parent = track, Size = UDim2.new(0, 0, 0, 40), BackgroundTransparency = 1 })
		group.AutomaticSize = Enum.AutomaticSize.X
		applyPadding(group, 0, 14, 0, 14)
		local groupLayout = Instance.new("UIListLayout")
		groupLayout.FillDirection = Enum.FillDirection.Horizontal
		groupLayout.Padding = UDim.new(0, 28)
		groupLayout.SortOrder = Enum.SortOrder.LayoutOrder
		groupLayout.VerticalAlignment = Enum.VerticalAlignment.Center
		groupLayout.Parent = group
		for _, item in ipairs(TICKER) do
			local label = string.upper(item)
			app:Text({
				Parent = group, Text = label, Style = "Label", Wrapped = false,
				Size = UDim2.fromOffset(math.ceil(app:_measure(label, 9, FONTS.mono)) + 2, 40),
				AutomaticSize = Enum.AutomaticSize.None,
				VerticalAlign = Enum.TextYAlignment.Center,
			})
			app:Frame({ Parent = group, Size = UDim2.fromOffset(3, 3), Color = "accent", Radius = "pill" })
		end
		groups[groupIndex] = group
	end

	local offset = 0
	local groupWidth = 0
	app:_track(RunService.RenderStepped:Connect(function(delta)
		if app.options.Motion == false then return end
		if groupWidth <= 0 then
			local scale = app.scale.Scale
			groupWidth = groups[1].AbsoluteSize.X / (scale > 0 and scale or 1)
			if groupWidth <= 0 then return end
		end
		offset = (offset + delta * 38) % groupWidth
		track.Position = UDim2.fromOffset(-offset, 0)
	end))
end

local function sectionHead(app, parent, options)
	local head = app:Col({ Parent = parent })
	app:Rule({ Parent = head, Color = "line" })
	app:Spacer(18, head)
	kicker(app, head, options.index, options.kicker)
	app:Spacer(18, head)
	if app.t.heroCenter then
		app:Text({
			Parent = head, Text = options.title, Style = "H2", RichText = true,
			Align = Enum.TextXAlignment.Center,
		})
		app:Spacer(16, head)
		app:Text({
			Parent = head, Text = options.lede, Style = "Body", TextSize = 14,
			Align = Enum.TextXAlignment.Center,
		})
	else
		local row = app:Row({ Parent = head, Gap = 48, VerticalAlignment = Enum.VerticalAlignment.Bottom })
		app:Text({ Parent = row, Text = options.title, Style = "H2", RichText = true, Size = UDim2.new(0.56, 0, 0, 0) })
		app:Text({ Parent = row, Text = options.lede, Style = "Body", TextSize = 14, Size = UDim2.new(0.44, -48, 0, 0) })
	end
	app:Spacer(48, head)
	return head
end

local function buildFeatures(app)
	local _, col = section(app, "features")
	sectionHead(app, col, {
		index = "01",
		kicker = "The toolbox",
		title = up(app, "Everything that usually means ") .. em(app, up(app, "five programs.")),
		lede = "Aether is not a storefront or a dashboard. It is the short list of things a Roblox player actually reaches for, with the rough edges filed down.",
	})

	local grid = app:Grid({
		Parent = col,
		Columns = app.t.featureCols,
		Gap = 48,
		RowGap = 0,
		CellHeight = app.t.featureHeight + 12,
	})
	for index, item in ipairs(FEATURES) do
		local cell = app:Frame({ Parent = grid, Size = UDim2.fromScale(1, 1), Color = "panel" })
		cell.BackgroundTransparency = 1
		local bar = app:Frame({
			Parent = cell, Size = UDim2.new(0, 2, 1, 0), Color = "accent",
			BackgroundTransparency = 1,
		})
		app:Text({
			Parent = cell, Text = string.format("%02d", index), Style = "Label", Color = "accent",
			Size = UDim2.fromOffset(30, 18), Position = UDim2.fromOffset(14, 22),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		})
		local inner = app:Col({
			Parent = cell,
			Size = UDim2.new(1, -58, 0, 0),
			Position = UDim2.fromOffset(44, 20),
		})
		app:Text({ Parent = inner, Text = up(app, item[1]), Style = "H3", Wrapped = false })
		app:Spacer(4, inner)
		app:Text({ Parent = inner, Text = item[2], Style = "Small", TextSize = 13 })
		app:Rule({
			Parent = cell, Color = "lineSoft",
			Size = UDim2.new(1, 0, 0, 1), Position = UDim2.new(0, 0, 1, -1),
		})

		cell.MouseEnter:Connect(function()
			TweenService:Create(cell, TweenInfo.new(0.25), { BackgroundTransparency = 0 }):Play()
			TweenService:Create(bar, TweenInfo.new(0.25), { BackgroundTransparency = 0 }):Play()
		end)
		cell.MouseLeave:Connect(function()
			TweenService:Create(cell, TweenInfo.new(0.25), { BackgroundTransparency = 1 }):Play()
			TweenService:Create(bar, TweenInfo.new(0.25), { BackgroundTransparency = 1 }):Play()
		end)
	end
end

local function mockOverlay(app, parent)
	local backdrop = app:Frame({ Parent = parent, Size = UDim2.fromScale(1, 1), Color = "bgDeep" })
	app:Frame({
		Parent = backdrop, Size = UDim2.fromOffset(260, 260),
		Position = UDim2.new(0.55, 0, 0.4, 0), AnchorPoint = Vector2.new(0.5, 0.5),
		Color = "accent", Radius = "pill", BackgroundTransparency = 0.93,
	})

	local stage = app:Frame({
		Parent = backdrop,
		Size = UDim2.new(1, -28, 1, -28),
		Position = UDim2.fromOffset(14, 14),
	})
	local grid = Instance.new("UIGridLayout")
	grid.CellSize = UDim2.new(0.5, -5, 0, 100)
	grid.CellPadding = UDim2.new(0, 10, 0, 10)
	grid.SortOrder = Enum.SortOrder.LayoutOrder
	grid.Parent = stage

	local titles = { "MENU", "FRAME RATE", "GRAPHICS", "NOTES", "SERVER", "ANTI-AFK" }
	for index, title in ipairs(titles) do
		local panel = app:Frame({
			Parent = stage, Size = UDim2.fromScale(1, 1),
			Color = "panel", Stroke = "line", Radius = app.t.radiusSm,
		})
		applyPadding(panel, 10, 10, 10, 10)
		app:Text({
			Parent = panel, Text = title, Style = "Label", Color = "accent",
			Size = UDim2.new(1, 0, 0, 12), AutomaticSize = Enum.AutomaticSize.None,
			Wrapped = false,
		})
		if index == 2 then
			app:Text({
				Parent = panel, Text = "144", Style = "Display", TextSize = 30,
				Size = UDim2.new(1, 0, 0, 38), Position = UDim2.fromOffset(0, 18),
				AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
			})
			app:Text({
				Parent = panel, Text = "FPS  ·  UNCAPPED", Style = "Label",
				Size = UDim2.new(1, 0, 0, 14), Position = UDim2.fromOffset(0, 58),
				AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
			})
		elseif index == 3 then
			for rowIndex = 1, 3 do
				local track = app:Frame({
					Parent = panel, Size = UDim2.new(1, 0, 0, 3),
					Position = UDim2.fromOffset(0, 28 + rowIndex * 16), Color = "lineSoft",
				})
				app:Frame({
					Parent = track, Size = UDim2.new(0.35 + rowIndex * 0.18, 0, 1, 0),
					Color = "accent", Radius = "pill",
				})
			end
		elseif index == 4 then
			for rowIndex = 1, 3 do
				app:Frame({
					Parent = panel, Size = UDim2.new(0.9 - rowIndex * 0.12, 0, 0, 3),
					Position = UDim2.fromOffset(0, 26 + rowIndex * 12), Color = "textDim",
					BackgroundTransparency = 0.35, Radius = "pill",
				})
			end
		elseif index == 5 then
			local chip = app:Frame({
				Parent = panel, Size = UDim2.fromOffset(72, 24),
				Position = UDim2.fromOffset(0, 32), Color = "accentSoft",
				Stroke = "accentDeep", Radius = 2,
			})
			app:Text({
				Parent = chip, Text = "REJOIN", Font = "mono", TextSize = 8, Color = "accentHi",
				Align = Enum.TextXAlignment.Center, Size = UDim2.fromScale(1, 1),
				AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
				VerticalAlign = Enum.TextYAlignment.Center,
			})
		elseif index == 6 then
			app:Frame({ Parent = panel, Size = UDim2.fromOffset(5, 5), Position = UDim2.fromOffset(0, 30), Color = "ok", Radius = "pill" })
			app:Text({
				Parent = panel, Text = "12:04", Style = "Mono", TextSize = 16, Color = "ok",
				Size = UDim2.new(0, 80, 0, 22), Position = UDim2.fromOffset(12, 22),
				AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
			})
			app:Text({
				Parent = panel, Text = "UNTIL NEXT TAP", Style = "Label",
				Size = UDim2.new(1, 0, 0, 12), Position = UDim2.fromOffset(0, 52),
				AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
			})
		else
			for rowIndex = 1, 3 do
				app:Frame({
					Parent = panel, Size = UDim2.fromOffset(5, 5),
					Position = UDim2.fromOffset(0, 27 + rowIndex * 13), Color = "accent", Radius = "pill",
				})
				app:Frame({
					Parent = panel, Size = UDim2.new(0.62, 0, 0, 3),
					Position = UDim2.fromOffset(12, 28 + rowIndex * 13), Color = "textDim",
					BackgroundTransparency = 0.4, Radius = "pill",
				})
			end
		end
	end
end

local function buildOverlay(app)
	local t = app.t
	local _, col = section(app, "overlay")
	local row = app:Row({ Parent = col, Gap = 72, VerticalAlignment = Enum.VerticalAlignment.Center })

	local copy = app:Col({ Parent = row, Size = UDim2.new(0.5, 0, 0, 0) })
	local visual = app:Col({ Parent = row, Size = UDim2.new(0.5, -72, 0, 0) })
	if t.overlayVisualFirst then
		copy.LayoutOrder = 2
		visual.LayoutOrder = 1
	end

	kicker(app, copy, "02", "While you are in the game")
	app:Spacer(16, copy)
	app:Text({
		Parent = copy,
		Text = up(app, "Press ") .. accentTag(app, up(app, "Delete")) .. up(app, ". The desktop can wait."),
		Style = "H2", RichText = true,
	})
	app:Spacer(20, copy)
	app:Text({
		Parent = copy, Style = "Body", TextSize = 14, Wrapped = true,
		Text = "The overlay opens where you left it: six small panels, each with its own memory. Nothing is injected into the client, and nothing follows you out of the game.",
	})

	local list = app:Col({ Parent = copy })
	app:Spacer(30, list)
	app:Rule({ Parent = list, Color = "line" })
	for index, panel in ipairs(PANELS) do
		local item = app:Frame({ Parent = list, Size = UDim2.new(1, 0, 0, 0), AutomaticSize = Enum.AutomaticSize.Y })
		applyPadding(item, 13, 0, 13, 0)
		app:Text({
			Parent = item, Text = string.format("%02d", index), Style = "Label", Color = "accent",
			Size = UDim2.fromOffset(26, 18), Position = UDim2.fromOffset(0, 2),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		})
		local inner = app:Col({ Parent = item, Size = UDim2.new(1, -38, 0, 0), Position = UDim2.fromOffset(38, 0) })
		app:Text({ Parent = inner, Text = up(app, panel[1]), Style = "H4", TextSize = 16, Wrapped = false })
		app:Text({ Parent = inner, Text = panel[2], Style = "Small", TextSize = 12 })
		app:Rule({ Parent = list, Color = "lineSoft" })
	end
	app:Spacer(14, copy)

	local note = app:Row({ Parent = copy, Gap = 8, VerticalAlignment = Enum.VerticalAlignment.Center })
	app:Frame({ Parent = note, Size = UDim2.fromOffset(5, 5), Color = "ok", Radius = "pill" })
	app:Text({
		Parent = note, Text = "DRAWN ABOVE THE GAME. NO INJECTION, NO MEMORY READS.",
		Style = "Label", TextSize = 9, Wrapped = false,
		Size = UDim2.new(0, 430, 0, 16), AutomaticSize = Enum.AutomaticSize.None,
		VerticalAlign = Enum.TextYAlignment.Center,
	})

	local holder = app:Frame({ Parent = visual, Size = UDim2.new(1, 0, 0, 466) })
	local content = shotFrame(app, holder, {
		label = "AETHER / OVERLAY",
		badge = "6 PANELS",
		angle = -t.frameAngle,
		offset = t.frameOffset,
	})
	mockOverlay(app, content)
	app:Spacer(15, visual)
	local caption = app:Row({ Parent = visual, Gap = 9 })
	app:Text({
		Parent = caption, Text = "02", Style = "Label", Color = "accent",
		Size = UDim2.fromOffset(20, 18), AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
	})
	app:Text({
		Parent = caption, Text = "Six separate windows. Drag one, and it stays there next time.",
		Style = "Fine", Size = UDim2.new(1, -29, 0, 0),
	})
end

local function themeCard(app, parent, id)
	local theme = THEMES[id]
	local selected = app.themeId == id
	local card = Instance.new("TextButton")
	card.Name = theme.name
	card.AutoButtonColor = false
	card.Size = UDim2.fromScale(1, 1)
	card.BackgroundColor3 = selected and app.t.panel2 or app.t.panel
	card.BackgroundTransparency = selected and 0 or 1
	card.BorderSizePixel = 0
	card.Text = ""
	card.Parent = parent
	applyCorner(card, app.t.radiusSm)
	local border = applyStroke(card, selected and app.t.accent or app.t.lineSoft)
	if selected then
		app:Frame({ Parent = card, Size = UDim2.new(0, 3, 1, 0), Color = "accent" })
	end
	app:Frame({
		Parent = card, Size = UDim2.fromOffset(26, 26),
		Position = UDim2.new(0, 11, 0.5, 0), AnchorPoint = Vector2.new(0, 0.5),
		Color = theme.accent, Radius = "pill", Stroke = theme.panel2,
	})
	app:Text({
		Parent = card, Text = theme.name, Style = "H4", TextSize = 14,
		Size = UDim2.new(1, -96, 0, 18), Position = UDim2.fromOffset(48, 11),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
	})
	app:Text({
		Parent = card, Text = theme.meta, Style = "Label", TextSize = 8,
		Size = UDim2.new(1, -96, 0, 14), Position = UDim2.fromOffset(48, 31),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
	})
	if selected then
		app:Frame({
			Parent = card, Size = UDim2.fromOffset(5, 5),
			Position = UDim2.new(1, -14, 0.5, 0), AnchorPoint = Vector2.new(0.5, 0.5),
			Color = "accent", Radius = "pill",
		})
	end

	card.MouseEnter:Connect(function()
		border.Color = app.t.accent
		if not selected then
			TweenService:Create(card, TweenInfo.new(0.2), { BackgroundTransparency = 0 }):Play()
		end
	end)
	card.MouseLeave:Connect(function()
		border.Color = selected and app.t.accent or app.t.lineSoft
		if not selected then
			TweenService:Create(card, TweenInfo.new(0.2), { BackgroundTransparency = 1 }):Play()
		end
	end)
	card.Activated:Connect(function()
		app:SetTheme(id)
	end)
	return card
end

local function buildEditions(app)
	local t = app.t
	local _, col = section(app, "themes")
	sectionHead(app, col, {
		index = "03",
		kicker = "Editions",
		title = up(app, "Twelve palettes. ") .. em(app, up(app, "One launcher.")),
		lede = "The same colour worlds ship inside Aether. Change the edition here and the whole page follows — type, corners, spacing and the way the sections sit together.",
	})

	local layout = app:Row({ Parent = col, Gap = 56, VerticalAlignment = Enum.VerticalAlignment.Top })
	local galleryCol = app:Col({ Parent = layout, Size = UDim2.new(0.46, 0, 0, 0) })
	local preview = app:Col({ Parent = layout, Size = UDim2.new(0.54, -56, 0, 0) })

	local gallery = app:Grid({
		Parent = galleryCol,
		Columns = 2,
		Gap = 8,
		RowGap = 8,
		CellHeight = 58,
	})
	for _, id in ipairs(THEME_ORDER) do
		themeCard(app, gallery, id)
	end

	local sheetWrap = app:Panel({
		Parent = preview,
		Size = UDim2.new(1, 0, 0, 0),
		AutomaticSize = Enum.AutomaticSize.Y,
		Radius = t.radius,
	})
	applyPadding(sheetWrap, 14, 14, 14, 14)
	local sheet = app:Frame({
		Parent = sheetWrap,
		Size = UDim2.new(1, 0, 0, 0),
		AutomaticSize = Enum.AutomaticSize.Y,
		Color = "bgDeep",
		Stroke = "lineSoft",
		Radius = t.radiusSm,
	})
	applyPadding(sheet, 17, 19, 15, 19)
	local sheetCol = app:Col({ Parent = sheet })

	local head = app:Frame({ Parent = sheetCol, Size = UDim2.new(1, 0, 0, 26) })
	app:Text({
		Parent = head, Text = "AETHER / TYPE & COLOUR", Style = "Label",
		Size = UDim2.new(1, 0, 0, 26), AutomaticSize = Enum.AutomaticSize.None,
		Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	})
	app:Text({
		Parent = head, Text = "Edition " .. string.format("%02d", t.index), Style = "Label",
		Align = Enum.TextXAlignment.Right, Size = UDim2.new(1, 0, 0, 26),
		AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		VerticalAlign = Enum.TextYAlignment.Center,
	})
	app:Rule({ Parent = sheetCol, Color = "lineSoft" })
	app:Spacer(18, sheetCol)

	local stacked = t.previewDirection == "column"
	local body = app:Row({
		Parent = sheetCol,
		Gap = stacked and 4 or 32,
		VerticalAlignment = stacked and Enum.VerticalAlignment.Top or Enum.VerticalAlignment.Center,
	})
	if stacked then
		local bodyLayout = body:FindFirstChildOfClass("UIListLayout")
		if bodyLayout then bodyLayout.FillDirection = Enum.FillDirection.Vertical end
	end
	app:Text({
		Parent = body, Text = "Aa", Style = "Glyph", TextSize = 104,
		Size = UDim2.new(0, stacked and 240 or 220, 0, 0),
		Wrapped = false,
	})
	local spec = app:Col({ Parent = body, Size = UDim2.new(1, stacked and 0 or -252, 0, 0) })
	app:Text({ Parent = spec, Text = "COLOUR WORLD", Style = "Label", Color = "accent", Wrapped = false })
	app:Spacer(5, spec)
	app:Text({ Parent = spec, Text = t.name, Style = "H3", TextSize = 28, Wrapped = false })
	app:Spacer(4, spec)
	app:Text({ Parent = spec, Text = t.meta, Style = "Fine", TextSize = 9 })

	app:Spacer(20, sheetCol)
	app:Rule({ Parent = sheetCol, Color = "lineSoft" })
	app:Spacer(10, sheetCol)
	local foot = app:Frame({ Parent = sheetCol, Size = UDim2.new(1, 0, 0, 20) })
	local dots = app:Row({ Parent = foot, Gap = 4, Height = 20 })
	dots.Size = UDim2.fromOffset(0, 20)
	dots.AutomaticSize = Enum.AutomaticSize.X
	local palette = { "accent", "accentSoft", "panel2", "text" }
	for _, token in ipairs(palette) do
		app:Frame({ Parent = dots, Size = UDim2.fromOffset(13, 13), Color = token, Stroke = "line", Radius = 2 })
	end
	app:Text({
		Parent = foot, Text = t.spec, Style = "Label", Align = Enum.TextXAlignment.Right,
		Size = UDim2.new(1, 0, 0, 20), AutomaticSize = Enum.AutomaticSize.None,
		Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	})

	app:Spacer(14, preview)
	local meta = app:Row({ Parent = preview, Gap = 18, VerticalAlignment = Enum.VerticalAlignment.Center })
	app:Text({
		Parent = meta, Text = t.hint, Style = "Fine", TextSize = 11,
		Size = UDim2.new(1, -160, 0, 0),
	})
	app:Button({
		Parent = meta,
		Text = "Surprise me",
		Variant = "Ghost",
		Height = 34,
		TextSize = 11,
		Padding = 12,
		OnClick = function() app:ShuffleTheme() end,
	})
end

local function buildNumbers(app)
	local _, col = section(app, nil, app.t.sectionGap)
	app:Rule({ Parent = col, Color = "line" })
	local row = app:Row({ Parent = col, Height = 118 })
	local entries = {
		{ "66", "curated presets" },
		{ "91", "individual flags" },
		{ "7", "flag groups" },
		{ "0", "NuGet dependencies" },
	}
	for index, entry in ipairs(entries) do
		local cell = app:Frame({ Parent = row, Size = UDim2.new(0.25, 0, 1, 0) })
		if index > 1 then
			app:Frame({ Parent = cell, Size = UDim2.new(0, 1, 1, 0), Color = "lineSoft" })
		end
		local inset = index > 1 and 20 or 0
		app:Text({
			Parent = cell, Text = entry[1], Style = "Number", TextSize = 46,
			Size = UDim2.new(1, -inset, 0, 58), Position = UDim2.fromOffset(inset, 22),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		})
		app:Text({
			Parent = cell, Text = string.upper(entry[2]), Style = "Label",
			Size = UDim2.new(1, -inset, 0, 18), Position = UDim2.fromOffset(inset, 78),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
		})
	end
	app:Rule({ Parent = col, Color = "line" })
end

local function buildDownload(app)
	local t = app.t
	local version = app.config.Version or "1.1.0"
	local _, col = section(app, "download")
	sectionHead(app, col, {
		index = "04",
		kicker = "Download",
		title = up(app, "Take the folder. ") .. em(app, up(app, "Leave the installer.")),
		lede = "Windows 10 or 11, 64-bit. Download it, run AetherLauncher.exe and point it at your Roblox install. No background service, no account, no telemetry.",
	})

	local cardHeight = 424
	local card = app:Row({
		Parent = col,
		Height = cardHeight,
		Gap = 0,
		Color = "panel",
		Stroke = "line",
		Radius = t.radius,
		ClipsDescendants = true,
	})
	local main = app:Frame({ Parent = card, Size = UDim2.new(0.54, 0, 1, 0) })
	applyPadding(main, 40, 44, 32, 44)
	local mainCol = app:Col({ Parent = main })

	local cta = app:Row({ Parent = mainCol, Gap = 22, VerticalAlignment = Enum.VerticalAlignment.Center })
	app:Button({
		Parent = cta, Text = "Download latest release", Arrow = "down", Variant = "Primary",
		OnClick = function() download(app) end,
	})
	app:Button({
		Parent = cta, Text = "All releases", Arrow = "diag", Variant = "Link",
		OnClick = function() app:OpenLink(app.config.ReleasesUrl or app.config.GitHubUrl or "") end,
	})
	app:Spacer(16, mainCol)
	app:Text({
		Parent = mainCol, Style = "Fine",
		Text = "v" .. version .. "   ·   Windows 10/11, x64   ·   Download, run, point at your install.",
	})

	app:Spacer(32, mainCol)
	local summary = Instance.new("TextButton")
	summary.Name = "Requirements"
	summary.AutoButtonColor = false
	summary.BackgroundTransparency = 1
	summary.BorderSizePixel = 0
	summary.Size = UDim2.new(1, 0, 0, 20)
	summary.Font = FONTS.mono
	summary.TextSize = 9
	summary.TextColor3 = t.textMid
	summary.TextXAlignment = Enum.TextXAlignment.Left
	summary.Text = string.upper("Requirements & first start") .. "     +"
	summary.Parent = mainCol

	local requirements = app:Col({ Parent = mainCol, Visible = app.state.requirements })
	app:Spacer(8, requirements)
	local lines = {
		"Windows 10 or 11, 64-bit.",
		"No separate .NET install; the runtime is bundled.",
		"An installed Roblox client under %LOCALAPPDATA%\\Roblox.",
		"WebView2 installs automatically if missing. If Windows hesitates on the unsigned exe, choose Unblock.",
	}
	for _, line in ipairs(lines) do
		local item = app:Row({ Parent = requirements, Gap = 8 })
		app:Frame({ Parent = item, Size = UDim2.fromOffset(3, 3), Position = UDim2.fromOffset(2, 9), Color = "accent" })
		app:Text({ Parent = item, Text = line, Style = "Small", TextSize = 12, Size = UDim2.new(1, -12, 0, 0) })
	end
	summary.Activated:Connect(function()
		app.state.requirements = not app.state.requirements
		requirements.Visible = app.state.requirements
		summary.Text = string.upper("Requirements & first start") .. (app.state.requirements and "     –" or "     +")
	end)

	local release = app:Frame({ Parent = card, Size = UDim2.new(0.46, 0, 1, 0), Color = "bgDeep" })
	app:Rule({ Parent = release, Color = "line", Size = UDim2.new(0, 1, 1, 0) })
	applyPadding(release, 32, 34, 28, 34)
	local releaseCol = app:Col({ Parent = release })
	app:Text({ Parent = releaseCol, Text = "CURRENT RELEASE", Style = "Label", Color = "accent", Wrapped = false })
	app:Spacer(14, releaseCol)

	local data = {
		{ "Version", version },
		{ "Published", app.config.Published or "—" },
		{ "Size", app.config.Size or "—" },
		{ "Downloads", app.config.Downloads or "—" },
	}
	for _, entry in ipairs(data) do
		local row = app:Frame({ Parent = releaseCol, Size = UDim2.new(1, 0, 0, 36) })
		app:Text({
			Parent = row, Text = string.upper(entry[1]), Style = "Label",
			Size = UDim2.new(0, 110, 0, 36), AutomaticSize = Enum.AutomaticSize.None,
			Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
		})
		app:Text({
			Parent = row, Text = entry[2], Style = "Mono", TextSize = 11,
			Align = Enum.TextXAlignment.Right, Size = UDim2.new(1, -110, 0, 36),
			AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
			VerticalAlign = Enum.TextYAlignment.Center,
		})
		app:Rule({ Parent = row, Color = "lineSoft", Position = UDim2.new(0, 0, 1, -1) })
	end

	app:Spacer(18, releaseCol)
	local links = app:Row({ Parent = releaseCol, Gap = 16 })
	app:Button({
		Parent = links, Text = "Release notes", Variant = "Link", Height = 22, TextSize = 11,
		Arrow = "diag", OnClick = function() app:OpenLink(app.config.ReleasesUrl or app.config.GitHubUrl or "") end,
	})
	app:Button({
		Parent = links, Text = "Build from source", Variant = "Link", Height = 22, TextSize = 11,
		Arrow = "diag", OnClick = function() app:OpenLink(app.config.SourceUrl or app.config.GitHubUrl or "") end,
	})
	app:Spacer(8, releaseCol)
	app:Text({
		Parent = releaseCol, Style = "Fine",
		Text = "No account, no telemetry. A download click is counted only if you connect analytics; feedback is sent only when you press send.",
	})

	app:Spacer(18, col)
	app:Rule({ Parent = col, Color = "lineSoft" })
	app:Spacer(18, col)
	local source = app:Row({ Parent = col, Gap = 24, VerticalAlignment = Enum.VerticalAlignment.Center })
	local sourceCopy = app:Col({ Parent = source, Size = UDim2.new(0.32, 0, 0, 0) })
	kicker(app, sourceCopy, "", "Prefer to build it?")
	app:Spacer(6, sourceCopy)
	app:Text({ Parent = sourceCopy, Text = up(app, "Two commands, no tooling beyond the .NET SDK."), Style = "H4", TextSize = 16 })

	local command = "dotnet build src/AetherLauncher/AetherLauncher.csproj -c Release"
	local code = app:Frame({
		Parent = source,
		Size = UDim2.new(0.48, -48, 0, 42),
		Color = "bgDeep",
		Stroke = "lineSoft",
	})
	applyPadding(code, 0, 12, 0, 12)
	local codeLabel = app:Text({
		Parent = code, Text = command, Font = "mono", TextSize = 10, Color = "textMid",
		Size = UDim2.new(1, -54, 0, 42), AutomaticSize = Enum.AutomaticSize.None,
		Wrapped = false, VerticalAlign = Enum.TextYAlignment.Center,
	})
	codeLabel.TextTruncate = Enum.TextTruncate.AtEnd
	local copy = Instance.new("TextButton")
	copy.AutoButtonColor = false
	copy.BackgroundTransparency = 1
	copy.Size = UDim2.fromOffset(44, 42)
	copy.Position = UDim2.new(1, -44, 0, 0)
	copy.Font = FONTS.mono
	copy.TextSize = 8
	copy.TextColor3 = t.accent
	copy.Text = "COPY"
	copy.Parent = code
	copy.Activated:Connect(function()
		local ok = pcall(function() setclipboard(command) end)
		if ok then
			app:Toast("Build command copied.")
		else
			app:Toast(command)
		end
	end)
	app:Button({
		Parent = source, Text = "Open the repository", Variant = "Link", TextSize = 11,
		Arrow = "diag", OnClick = function() app:OpenLink(app.config.SourceUrl or app.config.GitHubUrl or "") end,
	})
end

local function buildFeedback(app)
	local t = app.t
	local _, col = section(app, "feedback")
	local row = app:Row({ Parent = col, Gap = 72, VerticalAlignment = Enum.VerticalAlignment.Top })
	local copy = app:Col({ Parent = row, Size = UDim2.new(0.38, 0, 0, 0) })
	kicker(app, copy, "05", "Feedback")
	app:Spacer(14, copy)
	app:Text({ Parent = copy, Text = up(app, "Tell us what broke."), Style = "H2" })
	app:Spacer(18, copy)
	app:Text({
		Parent = copy, Style = "Body", TextSize = 14,
		Text = "Bugs, missing flags, a theme that reads badly at night — write it down. No account needed. Pass Config.OnFeedback and it lands wherever you want it.",
	})

	local form = app:Col({ Parent = row, Size = UDim2.new(0.62, -72, 0, 0) })
	app:Rule({ Parent = form, Color = "line" })
	app:Spacer(18, form)

	local head = app:Row({ Parent = form, Height = 16 })
	app:Text({ Parent = head, Text = "MESSAGE", Style = "Label", Size = UDim2.new(1, -90, 1, 0) })
	local count = app:Text({
		Parent = head, Text = "0 / 2000", Style = "Label", Align = Enum.TextXAlignment.Right,
		Size = UDim2.fromOffset(80, 16), AutomaticSize = Enum.AutomaticSize.None, Wrapped = false,
	})
	count.Text = #app.state.feedback .. " / 2000"
	app:Spacer(8, form)

	local field = app:Frame({ Parent = form, Size = UDim2.new(1, 0, 0, 142) })
	local box = Instance.new("TextBox")
	box.BackgroundTransparency = 1
	box.BorderSizePixel = 0
	box.Size = UDim2.new(1, 0, 1, -1)
	box.Font = FONTS.sans
	box.TextSize = 14
	box.TextColor3 = t.text
	box.PlaceholderText = "The overlay key does not open while Roblox is in fullscreen..."
	box.PlaceholderColor3 = t.textDim
	box.TextXAlignment = Enum.TextXAlignment.Left
	box.TextYAlignment = Enum.TextYAlignment.Top
	box.TextWrapped = true
	box.MultiLine = true
	box.ClearTextOnFocus = false
	box.Text = app.state.feedback
	box.Parent = field
	local rule = app:Rule({ Parent = field, Color = "line", Position = UDim2.new(0, 0, 1, -1) })
	box.Focused:Connect(function() rule.BackgroundColor3 = t.accent end)
	box.FocusLost:Connect(function() rule.BackgroundColor3 = t.line end)
	box:GetPropertyChangedSignal("Text"):Connect(function()
		if #box.Text > 2000 then box.Text = string.sub(box.Text, 1, 2000) end
		app.state.feedback = box.Text
		count.Text = #box.Text .. " / 2000"
		count.TextColor3 = #box.Text > 1800 and t.danger or t.textDim
	end)

	app:Spacer(20, form)
	app:Text({ Parent = form, Text = "CONTACT   (OPTIONAL, SO WE CAN REPLY)", Style = "Label", Wrapped = false })
	app:Spacer(7, form)
	local contactField = app:Frame({ Parent = form, Size = UDim2.new(1, 0, 0, 40) })
	local contact = Instance.new("TextBox")
	contact.BackgroundTransparency = 1
	contact.BorderSizePixel = 0
	contact.Size = UDim2.new(1, 0, 1, -1)
	contact.Font = FONTS.sans
	contact.TextSize = 14
	contact.TextColor3 = t.text
	contact.PlaceholderText = "Discord or email"
	contact.PlaceholderColor3 = t.textDim
	contact.TextXAlignment = Enum.TextXAlignment.Left
	contact.TextYAlignment = Enum.TextYAlignment.Center
	contact.ClearTextOnFocus = false
	contact.Text = app.state.contact
	contact.Parent = contactField
	local contactRule = app:Rule({ Parent = contactField, Color = "line", Position = UDim2.new(0, 0, 1, -1) })
	contact.Focused:Connect(function() contactRule.BackgroundColor3 = t.accent end)
	contact.FocusLost:Connect(function() contactRule.BackgroundColor3 = t.line end)
	contact:GetPropertyChangedSignal("Text"):Connect(function()
		app.state.contact = contact.Text
	end)

	app:Spacer(24, form)
	local actions = app:Row({ Parent = form, Gap = 18, VerticalAlignment = Enum.VerticalAlignment.Center })
	local status = app:Text({
		Parent = actions, Text = "", Style = "Fine", Size = UDim2.new(1, -190, 0, 0),
	})
	app:Button({
		Parent = actions,
		Text = "Send feedback",
		Variant = "Primary",
		OnClick = function()
			local message = box.Text
			if #message < 3 then
				status.Text = "Write a little more first."
				status.TextColor3 = t.danger
				return
			end
			if app.config.OnFeedback then
				local ok, result = pcall(app.config.OnFeedback, message, contact.Text)
				if ok then
					status.Text = "Sent — thank you."
					status.TextColor3 = t.ok
					app:Toast("Feedback sent.")
				else
					status.Text = "Could not send: " .. tostring(result)
					status.TextColor3 = t.danger
				end
			else
				status.Text = "Demo mode — pass Config.OnFeedback to send this."
				status.TextColor3 = t.textDim
				app:Toast("Feedback needs Config.OnFeedback.")
			end
		end,
	})
	app:Spacer(16, form)
	app:Text({
		Parent = form, Style = "Fine",
		Text = "No account needed. Nothing leaves until you press send.",
	})
end

local function buildFooter(app)
	local _, col = section(app, "footer", app.t.sectionGap)
	app:Spacer(0, col)
	local word = app:Text({
		Parent = col, Text = "Aether", Style = "H1", TextSize = 180,
		Color = "lineSoft", Wrapped = false,
		Size = UDim2.new(1, 0, 0, 150), AutomaticSize = Enum.AutomaticSize.None,
	})
	word.TextYAlignment = Enum.TextYAlignment.Bottom
	app:Spacer(24, col)
	app:Rule({ Parent = col, Color = "lineSoft" })
	app:Spacer(18, col)

	local grid = app:Row({ Parent = col, Gap = 26, VerticalAlignment = Enum.VerticalAlignment.Center })
	local brand = app:Row({ Parent = grid, Gap = 8, VerticalAlignment = Enum.VerticalAlignment.Center })
	brand.AutomaticSize = Enum.AutomaticSize.X
	brand.Size = UDim2.fromOffset(0, 30)
	app:Frame({ Parent = brand, Size = UDim2.fromOffset(20, 20), Color = "accent", Radius = 4 })
	app:Text({ Parent = brand, Text = "Aether", Style = "H4", TextSize = 15, Wrapped = false, Size = UDim2.fromOffset(90, 30), AutomaticSize = Enum.AutomaticSize.None, VerticalAlign = Enum.TextYAlignment.Center })
	app:Text({ Parent = brand, Text = "v" .. (app.config.Version or "1.1.0"), Style = "Label", Wrapped = false, Size = UDim2.fromOffset(60, 30), AutomaticSize = Enum.AutomaticSize.None, VerticalAlign = Enum.TextYAlignment.Center })

	app:Text({
		Parent = grid,
		Text = "A local-first Windows launcher. Not affiliated with or endorsed by Roblox Corporation.",
		Style = "Fine", TextSize = 11, Size = UDim2.new(1, -520, 0, 0),
	})

	local links = app:Row({ Parent = grid, Gap = 16, VerticalAlignment = Enum.VerticalAlignment.Center })
	links.Size = UDim2.fromOffset(0, 24)
	links.AutomaticSize = Enum.AutomaticSize.X
	local function footerLink(label, onClick)
		local button = Instance.new("TextButton")
		button.AutoButtonColor = false
		button.BackgroundTransparency = 1
		button.Font = FONTS.mono
		button.TextSize = 8
		button.TextColor3 = app.t.textDim
		button.Text = string.upper(label)
		button.Size = UDim2.fromOffset(math.ceil(app:_measure(string.upper(label), 8, FONTS.mono)) + 6, 24)
		button.Parent = links
		button.MouseEnter:Connect(function() button.TextColor3 = app.t.accent end)
		button.MouseLeave:Connect(function() button.TextColor3 = app.t.textDim end)
		button.Activated:Connect(onClick)
	end
	footerLink("GitHub", function() app:OpenLink(app.config.GitHubUrl or "https://github.com") end)
	footerLink("Releases", function() app:OpenLink(app.config.ReleasesUrl or app.config.GitHubUrl or "") end)
	footerLink("Feedback", function() app:ScrollTo("feedback") end)
	footerLink("Top", function() app.page.CanvasPosition = Vector2.new(0, 0) end)

	app:Spacer(42, col)
end

buildPage = function(app)
	buildHero(app)
	buildTicker(app)
	buildFeatures(app)
	buildOverlay(app)
	buildEditions(app)
	buildNumbers(app)
	buildDownload(app)
	buildFeedback(app)
	buildFooter(app)
end


return AetherUI

end)()

-- ============================================================================
--  Everything above this divider is the AetherUI library.
--  Everything below is the site wiring.
-- ============================================================================

local Players = game:GetService("Players")
local UserInputService = game:GetService("UserInputService")
local GuiService = game:GetService("GuiService")
local HttpService = game:GetService("HttpService")

local player = Players.LocalPlayer
local playerGui = player:WaitForChild("PlayerGui")

-- The live site. Every URL below is one the website itself uses.
local SITE = "https://get-aether.de"
local REPO = "https://github.com/Wiiffies/Aether-Website"
local RELEASES = REPO .. "/releases"
local SOURCE = REPO .. "/tree/programm"
local INSTALLER = RELEASES .. "/download/v1.2.0/AetherLauncher-1.2.0-win-x64.exe"

-- Release box values. readRelease() replaces them from the GitHub API; these
-- strings are what the box shows when HTTP is unavailable.
local release = {
	version = "1.2.0",
	published = "—",
	size = "—",
	downloads = "—",
}

local MONTHS = {
	"Jan", "Feb", "Mar", "Apr", "May", "Jun",
	"Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
}

-- Executors expose game:HttpGet. Studio has HttpService instead, and both are
-- allowed to fail: a site window with dashes is better than no window.
local function get(url)
	local ok, body = pcall(function()
		return game:HttpGet(url, true)
	end)
	if ok and type(body) == "string" and #body > 0 then
		return body
	end

	local okAsync, asyncBody = pcall(function()
		return HttpService:GetAsync(url)
	end)
	if okAsync and type(asyncBody) == "string" then
		return asyncBody
	end

	return nil
end

local function formatDate(iso)
	local year, month, day = string.match(iso, "^(%d%d%d%d)%-(%d%d)%-(%d%d)")
	local name = month and MONTHS[tonumber(month)]
	if not name then
		return nil
	end
	return string.format("%s %d, %s", name, tonumber(day), year)
end

local function formatCount(value)
	local grouped = string.gsub(string.reverse(tostring(value)), "(%d%d%d)", "%1,")
	return (string.gsub(string.reverse(grouped), "^,", ""))
end

local function readRelease()
	local body = get("https://api.github.com/repos/Wiiffies/Aether-Website/releases/latest")
	if not body then
		return
	end

	local tag = string.match(body, '"tag_name"%s*:%s*"([^"]+)"')
	local published = string.match(body, '"published_at"%s*:%s*"([^"]+)"')
	local bytes = tonumber(string.match(body, '"size"%s*:%s*(%d+)'))
	local count = tonumber(string.match(body, '"download_count"%s*:%s*(%d+)'))

	if tag then
		release.version = (string.gsub(tag, "^v", ""))
	end

	local date = published and formatDate(published)
	if date then
		release.published = date
	end

	if bytes and bytes > 0 then
		release.size = string.format("%.1f MB", bytes / 1048576)
	end

	if count then
		release.downloads = formatCount(count)
	end
end

local app

local function notify(message)
	if app then
		app:Toast(message)
	end
end

local function copyText(text)
	return pcall(function()
		setclipboard(text)
	end)
end

-- The download has to happen in a browser, the way it does on the site. If the
-- client refuses to open one, the URL goes to the clipboard instead.
local function openLink(url)
	if type(url) ~= "string" or url == "" then
		return false
	end

	local opened = pcall(function()
		GuiService:OpenBrowserWindow(url)
	end)
	if opened then
		return true
	end

	return copyText(url)
end

local function onDownload()
	if not openLink(INSTALLER) then
		notify("Could not open the browser — downloads live at " .. SITE .. "/download.html")
	end
end

local function onLink(url)
	if not openLink(url) then
		notify(url)
	end
end

local function onFeedback(message, contact)
	local text = message
	if contact and contact ~= "" then
		text = text .. "\n\nContact: " .. contact
	end

	if copyText(text) then
		notify("Feedback copied — paste it on get-aether.de.")
	else
		notify("Could not copy the feedback.")
	end
end

local function build()
	app = AetherUI.new({
		Parent = playerGui,
		Name = "AetherSite",
		Theme = "midnight",
		Page = true,

		Config = {
			Version = release.version,
			GitHubUrl = REPO,
			ReleasesUrl = RELEASES,
			SourceUrl = SOURCE,
			DownloadUrl = INSTALLER,

			Published = release.published,
			Size = release.size,
			Downloads = release.downloads,

			OnDownload = onDownload,
			OnFeedback = onFeedback,
			OnLink = onLink,
		},
	})

	app:Toast("get-aether.de — Right Ctrl hides it.")
end

local function close()
	if app then
		app:Destroy()
		app = nil
	end
end

readRelease()
build()

UserInputService.InputBegan:Connect(function(input, processed)
	if processed then
		return
	end

	if input.KeyCode == Enum.KeyCode.RightControl then
		if app then
			close()
		else
			build()
		end
	end
end)
