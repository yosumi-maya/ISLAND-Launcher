# Product Design visual QA — 2026-11-01

## Target and evidence

- Source visual truth: `C:/Users/keaxi/.codex/generated_images/01a1965c-5e02-7ea2-aa25-1f971876dacd/exec-f0e6f604-e88c-4bcf-8101-d61902f6abce.png` (the second displayed concept chosen by the user).
- Implementation: `http://127.0.0.1:4183/`.
- Source and implementation: 1024 × 1536 pixels, CSS viewport 1024 × 1536, deviceScaleFactor 1, no density resampling. Chinese; light screenshot; menu/dialog closed; top of page.
- Full-view comparison: `F:/整合包下载器/artifacts/website-redesign/comparison-final.png` (reference left, implementation right).
- Focused type/actions comparison: `F:/整合包下载器/artifacts/website-redesign/hero-detail.png`.
- Implementation captures: `desktop-1024.png`, `desktop-1440.png`, `full-desktop.png`, `mobile-390.png`, `mobile-english.png`, `privacy-mobile.png`, under `F:/整合包下载器/artifacts/website-redesign/`.
- In-app Browser failed with `Cannot read properties of undefined (reading 'constraints')`. User explicitly authorized direct local headless browser verification. All evidence is browser-rendered using Playwright with installed Chrome.

## Findings and comparison history

Initial side-by-side evidence: `comparison.png`.

1. [P2, fixed] Display typography smaller than selected reference, most noticeably the showcase heading. Increased hero type from 7.5vw to 8.1vw and showcase type from 3.5vw to 4.2vw. The final full-view comparison confirms the original two-line wrapping and comparable display hierarchy.
2. [P2, fixed] Showcase appeared approximately 60 px too early and lacked the selected concept's shoreline backdrop. Increased hero height and added matching generated misty shoreline imagery. Final comparison shows the screenshot section at approximately the reference's vertical position with shore framing its bottom edge.
3. [P2, fixed] English mobile copy takes more lines, putting platform text over trees. Increased English mobile hero height; fresh `mobile-english.png` confirms readable text on the pale surface. Geometry check: at 390 px the text ends at y=561 and scene begins at y=626; at 320 px the text ends at y=589.5 and scene begins at y=766.

No actionable P0/P1/P2 issues remain in the final reviewed states.

## Required fidelity surfaces

- Fonts/typography: Chinese system sans serif and existing Trebuchet MS English fallback; dark pine heavy two-line headline; visible, readable UI copy. Closest system font treatment retained without remote dependencies. Display hierarchy and wrapping reviewed in focused comparison.
- Spacing/layout: full-width asymmetric hero, left-aligned copy, prominent paired actions, smaller introduction alongside large product screenshot. Desktop and phone views reviewed. Responsive overflow checks passed at 1440, 1024, 768, 760, 390 and 320 CSS px in Chinese and English.
- Colors/tokens: cool white #F5F7F7, pine #183B38, teal #176B68 and mist #EAF0EF. Pale surfaces, sea-green illustrations and readable dark foreground match the chosen direction. Focus rings remain visible.
- Image quality: individual generated raster hero and shoreline assets match the reference's voxel pine/rock/cabin/sea imagery. WebP delivery totals approximately 232 KiB. Original product screenshots remain byte-for-byte intact with native proportions, which intentionally avoids the generated reference's slight screenshot distortion. Screenshot zoom uses the originals.
- Copy/content: selected headline and brand story retained, complete Chinese/English coverage, accurate Light/Glass labels rather than the mock's inaccurate dark-theme label. Existing download/version/service statements preserved. No invented metrics, user reviews or platform promises.

## Interaction and technical verification

- Mobile menu opens and closes after navigation.
- Light/Glass switching updates selected state and loads the correct actual screenshot.
- Both main screenshot and appearance screenshot dialogs open and close; Escape works.
- FAQ expands.
- Language switch updates text and document language; privacy navigation retains English.
- Console/page errors: none.
- Build passed; all 29 existing static website checks passed; app.js syntax check passed.
- Screenshot SHA-256 matches prior owner originals: light `7ab308c930e22a55e3d7372863a80e3ec51b0d1ea448b941eb973bdd0c23160d`; glass `0222626398c9bb847b1677c6b0417e1248d5b7c2d8ecd52b8b423364f7e2e52d`.
- Live external release availability is outside this visual redesign; existing project configuration was retained. No cloud deployment performed.

## Follow-up polish

P3: the selected mock's compass/cartographic annotations and wave ribbon were omitted; the actual scene provides the primary visual. The original download icon is retained. These do not affect core layout or behavior.

## Implementation checklist

- [x] Selected visual direction implemented in existing site.
- [x] Generated assets optimized and packaged.
- [x] Desktop/mobile visual evidence compared and issues corrected.
- [x] Primary interactions and console verified.
- [x] Local preview available; no public deployment.

final result: passed
