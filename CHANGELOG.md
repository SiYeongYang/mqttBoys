# Changelog

## [0.0.10] - 2026-07-27

### Added

- Added Raw and Diff modes to Value. Diff compares the latest Value with the History message shown in Selected.
- Added per-field chart actions to Selected for numeric and boolean JSON values.
- Added detailed release notes sourced from this changelog.

### Changed

- Increased normal and hover chart point sizes for easier inspection.
- Moved JSON comparison work off the UI thread and bounded large comparisons to protect responsiveness.
- Added repository safeguards for local profiles, credentials, certificates, and private keys.

## [0.0.9] - 2026-07-23

### Added

- Added a separate live chart window that supports multiple numeric and boolean series.
- Added per-series pause, resume, remove, latest-value display, and point hover details.
- Added clickable chart actions beside compatible fields in Value.
- Added a collapsible JSON structure tree to JSON Formatter.

### Changed

- Reworked JSON rendering and scalar extraction for structured inspection.
- Improved chart window wrapping and sizing at desktop and laptop resolutions.

## [0.0.8] - 2026-07-23

### Added

- Added JSON Formatter with format, compact, validate, copy, and clear actions.
- Added the first scalar history chart workflow for numeric and boolean payload fields.

### Changed

- Batched topic count and preview notifications to reduce UI work under high message rates.
- Reduced repeated payload formatting and tightened live update scheduling.

## [0.0.7] - 2026-07-23

### Changed

- Refined the inspection layout so Value, Selected, History, and Publish remain visible together.
- Moved pause control into Value and unified Value and History pause behavior.
- Reduced header height and visual noise, and reorganized broker status, search, and tools.
- Improved thin scrollbars, splitters, and nested wheel handling.
- Reduced Value refresh latency and flicker during rapid updates.

## [0.0.6] - 2026-07-23

### Added

- Added topic search that keeps only matching topic paths.
- Added Publish topic autocomplete from observed topics.

### Changed

- Narrowed and virtualized topic browsing for faster navigation.
- Batched high-rate topic visual updates while keeping Value and History responsive.
- Reduced payload retention and formatting overhead for lower memory use.

## [0.0.5] - 2026-07-23

### Added

- Added configurable period checks for 10 seconds, 30 seconds, 1 minute, and custom durations.
- Added period-check cancellation with a result calculated from samples collected before cancellation.
- Added in-session period-check history.

### Changed

- Kept the active broker connection running while Connections is open.
- Reworked the main inspection layout with Value and Selected side by side.
- Added Shift+wheel horizontal scrolling and isolated nested payload scrolling.
- Improved live payload rendering and scroll-position preservation.
