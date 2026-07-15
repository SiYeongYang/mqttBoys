---
name: mqttBoys
description: A calm, precise desktop workspace for high-volume MQTT inspection.
colors:
  accent: "#176E63"
  accent-hover: "#105B52"
  accent-soft: "#E3F0ED"
  chrome: "#183B38"
  canvas: "#EEF3F2"
  surface: "#FFFFFF"
  surface-alt: "#F5F8F7"
  border: "#D5DFDD"
  border-strong: "#B8C8C5"
  ink: "#14201E"
  muted: "#536763"
  danger: "#B13B3B"
typography:
  headline:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "18px"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "0"
  title:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "15px"
    fontWeight: 600
    lineHeight: 1.35
    letterSpacing: "0"
  body:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "0"
  label:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "12px"
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: "0"
rounded:
  control: "5px"
  panel: "6px"
  modal: "8px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
components:
  button-primary:
    backgroundColor: "{colors.accent}"
    textColor: "{colors.surface}"
    rounded: "{rounded.control}"
    padding: "6px 14px"
    height: "32px"
  button-secondary:
    backgroundColor: "{colors.surface-alt}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "6px 12px"
    height: "32px"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "5px 9px"
    height: "32px"
---

# Design System: mqttBoys

## Overview

**Creative North Star: "The Quiet Control Desk"**

mqttBoys should resemble a well-maintained engineering console in a bright operations room. Information is dense but ordered, with live data occupying the most useful space and controls receding until needed. The system rejects rough forms where every element has equal weight and avoids decorative SaaS dashboard styling.

**Key Characteristics:**

- Restrained teal identity with neutral working surfaces.
- Compact, consistent controls with clear focus and disabled states.
- Tonal grouping and spacing instead of stacked decorative cards.
- No decorative animation or effects on the live message path.

## Colors

The palette uses a deep green-teal chrome, a single operational accent, and neutral surfaces with enough contrast for long monitoring sessions.

### Primary

- **Signal Teal:** Primary commands, focus, and current operational state only.
- **Deep Console:** Application chrome and high-confidence navigation surfaces.

### Neutral

- **Clear Surface:** Payload viewers, forms, and active work areas.
- **Quiet Canvas:** Separates major work regions without heavy borders.
- **Structural Line:** Dividers, field outlines, and table rules.
- **Console Ink:** Primary text and data labels.
- **Measured Gray:** Secondary metadata that remains readable.

**The One Signal Rule.** Accent color is reserved for primary actions, focus, selection, and live state. It is never decoration.

## Typography

**Display Font:** Segoe UI (sans-serif fallback)
**Body Font:** Segoe UI (sans-serif fallback)
**Label/Mono Font:** Consolas for payloads and measurements

**Character:** A single familiar UI family keeps labels predictable; monospace is reserved for machine data where alignment matters.

### Hierarchy

- **Headline** (600, 18px, 1.3): Modal and major workspace titles.
- **Title** (600, 15px, 1.35): Value, Selected, History, Publish, and settings groups.
- **Body** (400, 13px, 1.4): Controls, topic rows, and supporting information.
- **Label** (600, 12px, 1.3): Form labels and compact table headers.

**The Data Type Rule.** Payloads use Consolas; buttons, labels, navigation, and descriptions never do.

## Elevation

The interface is flat by default. Depth comes from surface tone and dividers; only modal overlays may use a restrained shadow to establish temporary hierarchy.

**The Flat Working Surface Rule.** Panels at rest never float. If every section appears raised, the hierarchy has failed.

## Components

### Buttons

- **Shape:** Compact gently curved edges (5px radius), stable 32px minimum height.
- **Primary:** Signal Teal with white text, reserved for Connect and Publish.
- **Hover / Focus:** Darker teal on hover; a visible border or focus visual for keyboard use.
- **Secondary:** Quiet neutral fill and structural border; destructive commands use red text without a saturated red block.

### Cards / Containers

- **Corner Style:** Subtle panel corners (6px); modal corners use 8px.
- **Background:** White active surfaces against the quiet canvas.
- **Shadow Strategy:** None for normal panels; modal only.
- **Border:** One structural line where separation is required.
- **Internal Padding:** 12px for work panels, 16 to 24px for modal sections.

### Inputs / Fields

- **Style:** White background, 1px structural outline, 5px radius, 32px stable height.
- **Focus:** Signal Teal outline, never a glow.
- **Error / Disabled:** Explicit danger color for errors; disabled controls retain shape and use reduced contrast.

### Navigation

The top toolbar uses Deep Console with restrained light controls. Connection folders and brokers remain a hierarchical tree with clear selection, hover, and drag targets.

## Do's and Don'ts

### Do:

- **Do** prioritize Value and Selected payload space over decorative chrome.
- **Do** group Connections into Broker, Connection, Session, Credentials, and SSH sections.
- **Do** keep controls at stable dimensions so live data cannot shift the layout.
- **Do** preserve virtualization, bounded history, and batched UI updates.

### Don't:

- **Don't** recreate rough forms where every label, field, button, and border has equal visual weight.
- **Don't** add decorative SaaS dashboards, oversized cards, gradients, neon accents, or glass effects.
- **Don't** use low-contrast text, cramped controls, or ambiguous selected and disabled states.
- **Don't** add animation, shadows, or formatting work to the MQTT receive path.
