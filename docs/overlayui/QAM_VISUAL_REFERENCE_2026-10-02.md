# Steam QAM Visual Reference — 2026-10-02

This is a read-only measurement of the current Steam Quick Access Menu used to ground Overlay PR A. Values below are separated by evidence level; unavailable states are not represented as measured.

## Capture context

| Item | Observation |
| --- | --- |
| Capture date | 2026-10-02 |
| Steam build | 1790721607 |
| CEF target | QuickAccess_uid17 |
| Target page | Steam Performance QAM page, #quickaccess_content_5 |
| CEF viewport | 855 × 682 CSS px |
| devicePixelRatio | 1.4199999570846558 (approximately 1.42) |
| Desktop screenshot | 1920 × 1080 px |
| Windows display scaling | Not independently verified |
| Hardware reference device | MSI Claw at 1920 × 1200 / 150% was not available for this capture |
| Method | Read-only Chromium DevTools Protocol Runtime.evaluate, getComputedStyle, getBoundingClientRect, and CSS.getPlatformFontsForNode against the visible QuickAccess target. No DOM/style mutation was performed. |

The numeric geometry below is in CSS pixels in Steam's CEF target. It is not a WinUI DIP measurement and should not be copied as Overlay window geometry.

## Measured

| Selector / context | Property | Computed value |
| --- | --- | --- |
| #QuickAccess-Menu | rect / display | 854 × 682; display:flex |
| #QuickAccess-Menu | background | rgb(14, 20, 27) / #0E141B |
| #QuickAccess-Menu | base text | rgb(150, 150, 150) / #969696; 16px, weight 400 |
| QAM rail container | width / height | 48 × 682 |
| rail item boxes | width / height / interval | 48 × 64; 64px vertical interval; no gap |
| rail SVG icon | rendered dimensions | 24 × 24 |
| selected #quickaccess_tab_5 | rect / fill | 48 × 64; rgb(35, 38, 46) / #23262E |
| selected and unselected rail icon | color | rgb(139, 146, 154) / #8B929A |
| active #quickaccess_content_5 | rect / padding | x=48, width=300; top padding 16px |
| page title 성능 | typography | Motiva Sans, Helvetica, sans-serif; 22px, weight 700, line-height 28px; white |
| ordinary row label FPS 고대비 | typography | Motiva Sans, Arial, Helvetica, sans-serif; 16px, weight 400, line-height 20px; rgb(220,222,223) / #DCDEDF |
| focused row wrapper | fill / radius | rgba(255,255,255,0.15) (approximately #26FFFFFF); 2px radius; no visible outline |
| ordinary toggle row | rect / padding | height 42px; 10px 16px; horizontal margin 0 -16px; 2px radius |
| QAM content section | background / bottom spacing | transparent background; computed bottom margin 24px |

For the Korean label 모서리에 FPS 표시, DevTools reported platform fonts Motiva Sans (MotivaSans-Regular, 5 glyphs) and Malgun Gothic (MalgunGothic, 6 glyphs). This is direct evidence that the Korean glyphs were supplied by Malgun Gothic in this capture.

## Observed (non-numeric)

- The visible screen capture showed the Steam Performance page with the icon rail on the left and content pane on the right.
- The rail uses glyph icons and a full-item selected block; no separate selected edge indicator was visible.
- The measured rail/content backgrounds matched the root surface; no distinct separator was visible in the inspected elements.
- The only tested Korean row label rendered through the CSS Motiva family plus platform fallback listed above.

## Inferred / provisional Overlay choices

| Resource choice | Evidence boundary |
| --- | --- |
| Segoe UI Variable as the WinUI family | The HKLM and HKCU installed-font registry keys contain no Motiva Sans; HKLM lists Segoe UI Variable and Malgun Gothic. Steam's private font container was not copied, extracted, installed, or parsed. WinUI's actual Korean fallback still requires hardware/render verification. |
| Caption size 14px / line-height 18px | A caption/status role was not observable in this captured page. Centralized provisional values only. |
| Disabled/error text uses the measured muted #969696 | No disabled or error row was visible. This reuses an observed neutral text color provisionally; it is not a measurement of those states. |
| Hover/pressed fill uses the measured focused-row fill | Hover and pressed states were not captured. The resource values are provisional and centralized. |
| Section header spacing 4px | The selected page did not expose a comparable labeled section header; provisional. |
| Shortcut/Profile tile surface and selected fill | Steam QAM has no equivalent product tile in this capture. Overlay uses the measured surface and selection palette provisionally. |
| Neutral QAM accent resource | No distinct blue accent or selected indicator was visible. The resource reuses the measured neutral rail-icon color (#8B929A), not the Windows accent. |
| Shortcut disabled opacity 0.48 | No disabled tile was visible in QAM; the Overlay retains its existing disabled-state opacity centrally. |

The Overlay intentionally retains the work-order's 416 DIP surface and 52 DIP structural rail. Only the internal visual treatment uses the measurements above; the 48 CSS px Steam rail does not change Overlay geometry.

## Deferred to PR B or hardware acceptance

- ToggleSwitch track/thumb, numeric slider, discrete selector, and stepper-button templates/states.
- Steam disabled, hover, pressed, warning/error, and distinct caption roles.
- Text letter-spacing/opacity, ordinary value typography, bottom content padding, and labeled section-header-to-row distance were not separately measured in this capture.
- 1920 × 1200 / 150% MSI Claw side-by-side visual acceptance, including WinUI's rendered Korean fallback and the effect of the expected per-monitor scaling.
- Any Profile/Shortcut tile parity claim; these are Overlay-specific surfaces.
