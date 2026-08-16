---
name: Spec issue
about: A contradiction, wrong claim, or gap in the design specification
title: ''
labels: spec
---

<!--
This is the most valuable kind of issue while InTest is at design stage. Previous reviews have
caught a build-breaking interaction between two documented MSTest mechanisms, an identifier
that collapsed to one value across every data-driven test row, and a validator gap that would
have passed invalid responses silently.
-->

## Where

Section and, if you can, line — for example `§11`, or `§9:733`.

## What kind

- [ ] **Contradiction** — two parts of the spec disagree
- [ ] **Wrong claim** — a factual statement about a library, framework or platform that is not true
- [ ] **Gap** — behaviour that is unspecified and will have to be invented during implementation
- [ ] **Single-org assumption** — something that only makes sense for the maintainers' own setup

## The problem

## Evidence

<!--
For a wrong claim, this matters most. The spec distinguishes what was read from documentation
and what was verified by running code — claims marked *measured* were built and executed. If
you have run something, please include it; if you are going from documentation, a link is fine.
Both are welcome, they are just different weights of evidence.
-->

## Suggested resolution

<!-- Optional. "This is wrong" is a useful issue on its own. -->
