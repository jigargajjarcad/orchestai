# P2-M1A — Shell Prototype — Completion Report (Go/No-Go)

> **Status: prototype complete, NOT merged, NOT pushed. STOP at the P2-M1A Go/No-Go.** Branch
> `worktree-p2-m1a-shell-prototype`, base local `main` @ `984cc6b`, tip `c5f9af3` (6 commits). Product
> `main` and `origin/main` untouched (`origin/main` = `829546e`). No P2-M1B, no product-page migration,
> no merge, no push. Recommendation: **GO** — awaiting explicit approval after your live review.

---

## 1. What was built (isolated, additive — no product surface touched)
| Task | Deliverable | Commit |
|---|---|---|
| T1 | Additive `contentWidth` + `breakpoints` tokens + `useBreakpoint` hook (on existing `useViewportWidth`) | `acd62a0` |
| T2 | `AppShell` (responsive grid + `useAppShell()` context + accessible mobile drawer) + `PageContainer` | `a877463` |
| T3 | `Sidebar` + **additive** `NavItem` icon/collapsed/tooltip + vertical `Nav` (App.jsx top-nav byte-identical) | `75e46ca` |
| T4 | `TopBar` (single `<header>`, `<h1>`, mobile menu button → drawer) | `89abb7f` |
| T5 | `shell-demo` separate Vite entry (5 views: dashboard/dense/readable/empty/long) | `7a70f66` |
| T6/T7 | Controller live-CDP verification + the `#root` fix + this report | `c5f9af3` |

Each task ran subagent-driven (fresh implementer + reviewer). T1–T4 reviews: Approved (T3 caught+fixed a
real `aria-label` ordering bug protecting App.jsx). Final whole-branch review (opus): **Go, 0 Critical/Important.**

## 2. Live verification — the 6-viewport matrix (MEASURED, via CDP on the running shell-demo)
| Viewport | Result |
|---|---|
| **390 (mobile)** | Sidebar → **drawer** (menu button in TopBar); `#root`=390, no horizontal scroll (scrollW==winW). Drawer = accessible dialog: `role=dialog`+`aria-modal=true`, focus moves in (to ×), **focus trap** held across real Tab, **Escape** closes, **focus returns** to the menu button, **body scroll locked/restored**. |
| **768 (tablet)** | Collapsed **icon rail** in-grid; no menu button; no h-scroll. |
| **1024 (desktop)** | Expanded sidebar in-grid; content fluid; no h-scroll. |
| **1440 (desktop)** | Expanded sidebar 239px; collapse → 55px rail (content **reflows to use freed width**); readable view caps 770px, wide 1600, full fills. Focus ring **traceBlue 2px** on real Tab; collapsed-item **tooltip visible on keyboard focus**. |
| **1920 (wide)** | Content fills; soft caps engage; no dead-zone on data surfaces. |
| **2560 (ultra-wide)** | After the `#root` fix: `#root`=2560, **`main`=2320** (fills right of sidebar), dashboard(`full`) spreads, **dense table uses the `wide` 1600 cap and fits** (was overflowing an 884px box), readable capped 770; **no horizontal scroll**. Feels like a deliberate instrument panel, not a site in a void. |

Fonts: **IBM Plex Sans (UI face)** + **JetBrains Mono (data face)** both render (h1/labels proportional,
IDs/costs/timestamps mono). (`document.fonts.check("16px 'IBM Plex Sans'")` reads false only because the
unused weight-400 face is lazy under `font-display:swap`; weights 500/600/700 actually used are loaded.)

## 3. Finding + fix during verification — the legacy `#root` cap
Live verification found the shell visually capped at **1126px** (`main`=884 at 2560). Cause: `index.css`
`#root { width: 1126px; margin: 0 auto; text-align: center; border-inline: 1px solid … }` — a hardcoded
centered column. **This is also the actual mechanism constraining the current PRODUCT to a centered
~1126px column** (the "constrained centered layout" P2-M1B is chartered to replace). A child can't un-cap
a fixed-width ancestor, so `AppShell` could not demonstrate its full-bleed design.
**Fix (`c5f9af3`, demo-scoped):** `shellDemoMain.jsx` sets INLINE styles on the demo's own `#root` mount
element (inline wins over the stylesheet). `index.css` and the product entry (`main.jsx`/`index.html`) are
**unchanged** — removing the cap for the product is explicit **P2-M1B** work.

## 4. Diff-scope audit — protected product surfaces BYTE-UNCHANGED
Confirmed **ZERO DIFF** (`984cc6b..c5f9af3`): `App.jsx`, `EvalsPage.jsx`, `ObservabilityPage.jsx` + other
Observability sub-views, `ApiKeyPrompt.jsx`, `main.jsx`, `index.html`, **`index.css`**, all `observability/*`,
all existing primitives except `NavItem`, and `package.json`/lockfile (**no new dependency**). The branch is
exactly: `tokens.js` (+51 additive), `useBreakpoint.js` (new), `components/shell/{AppShell,PageContainer,
Sidebar,TopBar}.jsx` (new), `NavItem.jsx` (+141 **additive**, App.jsx output byte-identical), and the 3
`shell-demo` files. 10 files, +1881/−5.

## 5. Build / lint
`npm run build` OK (gzip 180.14 kB — shell-demo is dev-server-only, not in the product build, per the R1
gallery precedent). `npm run lint` = exactly the 5 pre-existing baseline warnings, zero new.

## 6. P2-M1A Go/No-Go criteria — all met
1. ✅ Renders correctly at all 6 viewports (expanded/collapsed/drawer). 2. ✅ Ultra-wide intentional
(data uses space, text soft-capped, no void). 3. ✅ Mobile is a real drawer, no h-scroll, 44px touch.
4. ✅ A11y: keyboard-operable sidebar+drawer (trap/Escape/return), visible focus, accessible collapsed
labels/tooltip, single landmarks. 5. ✅ Width+breakpoint tokens additive + documented. 6. ✅ NavItem
additive/backward-compatible (App.jsx byte-identical). 7. ✅ Build + 5-baseline lint. 8. ✅ No product
page/backend/data touched; demo is a separate entry, zero product blast-radius. 9. ✅ Whole-branch review
clean (Go). 10. §J decisions applied (below).

## 7. §J decisions applied (from the approved plan's recommendations)
Mobile = **drawer** (your explicit approval); widths **readable 68ch / standard 1440 / wide 1600 / full
100%**; breakpoints **tablet 640 / desktop 1024 / wide 1440**; shell dir **`components/shell/`**;
collapsed-state persisted to **localStorage**; **Button API deferred to P2-M1B**. All re-confirmable — flag
any you'd change before P2-M1B.

## 8. Accepted minors / carry-forwards (documented, non-blocking)
- AppShell resize-close **one-render timing gap** (self-healing; detached-node focus is a no-op).
- `aria-modal` without `inert` background (accepted pattern; the trap + scrim guard it; same as R1 DetailPanel).
- Tablet-collapse = **starting-point-not-floor** (controller-confirmed intended: respect the user's toggle).
- `NavItem` collapsed with composite (non-string) children needs an explicit `label` (future-consumer
  footgun; Sidebar always passes string labels).

## 9. Forward findings for P2-M1B (NOT done here — flagged for the next plan)
- **Remove the product `#root` cap.** The `index.css` `#root { width:1126px; margin:0 auto; text-align:center;
  border-inline }` must be neutralized/replaced for the product when pages mount into `AppShell`, so the
  product gains the shell's full-bleed + per-surface width behavior. (P2-M1A left it untouched by design.)
- **Long-nav scroll strategy:** `Sidebar` has no internal scroll region — fine for ≤~6 items; revisit if the
  nav grows.
- The deferred **Button inline/size/icon API** and **`isNarrow` → `useBreakpoint`** migration land in P2-M1B.

## 10. STOP
P2-M1A stops here at the Go/No-Go. The shell-demo is live for your review (see §11). No merge, no push, no
P2-M1B. Recommendation: **GO**. Awaiting your explicit approval (and any §J adjustments) before merging the
prototype and starting P2-M1B.

## 11. Review surface
Live shell prototype: **`http://localhost:5173/shell-demo.html`** (dev server left running). Switch views via
the sidebar; toggle the sidebar collapse; resize to 390/768/1024/1440/1920/2560; on mobile open the drawer
from the top-bar menu button. Screenshots captured at every viewport/state are in the session scratchpad.

*Prototype complete. Not merged, not pushed. Awaiting review + explicit authorization.*
