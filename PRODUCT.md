# Product

## Register

product

## Users

mqttBoys is used by developers and operators who inspect MQTT brokers, trace topic hierarchies, compare live payloads with history, publish test messages, and measure message periods. They often work with high-volume streams where responsiveness and scanability matter more than decoration.

## Product Purpose

Provide a fast desktop workspace for managing broker connections and understanding MQTT traffic without forcing users to manually subscribe to and arrange every topic. Success means large message volumes remain responsive, current and historical payloads are easy to compare, and connection settings stay manageable as the broker list grows.

## Brand Personality

Calm, precise, fast. The interface should feel like a dependable engineering instrument: familiar at first glance, dense where the work requires it, and quiet everywhere else.

## Anti-references

- Rough forms where every label, field, button, and border has equal visual weight.
- Decorative SaaS dashboards, oversized cards, gradients, neon accents, and motion that competes with live data.
- Low-contrast text, cramped controls, and ambiguous selected or disabled states.
- Layout changes that reduce payload space or add work to the MQTT receive path.

## Design Principles

- Data first: live payloads, selected history, and topic state receive the strongest visual hierarchy.
- Familiar operations: preserve the MQTT Explorer-inspired tree and direct workflows while clarifying controls and grouping.
- Quiet structure: use spacing, tonal surfaces, and typography before adding borders or decoration.
- Explicit state: connection, selection, focus, disabled, and destructive states must be immediately distinguishable.
- Performance is a feature: visual polish must not add animation, expensive effects, or work per incoming message.

## Accessibility & Inclusion

Maintain readable contrast for text and controls, visible keyboard focus, clear disabled states, and layouts that remain usable at the application's 1100 x 720 minimum window. Do not rely on color alone for status or selection. Motion is limited to native control-state feedback.
