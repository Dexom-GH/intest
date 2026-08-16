---
name: Bug report
about: Something InTest does that it should not, or fails to do
title: ''
labels: bug
---

<!--
InTest is at design stage — there is no released code yet. If you are reporting a problem in
the design rather than in software, use the "Spec issue" template instead.
-->

## What happened

## What you expected

## Reproduction

Smallest OpenAPI document that shows the problem, if you can share one. A redacted or minimised
spec is fine and usually more useful than a large real one.

```yaml
# spec fragment
```

Command run:

```bash
intest ...
```

## Output

<!-- Paste the actual message. If it is long, the shortest decisive part is better than all of it. -->

```
```

## Environment

| | |
|---|---|
| InTest CLI version | |
| `InTest.Runtime` version | |
| .NET SDK (`dotnet --version`) | |
| OS | |
| Spec producer | Swashbuckle / built-in `Microsoft.AspNetCore.OpenApi` / NSwag / other |
| OpenAPI version | 3.0 / 3.1 / other |

## Anything else

<!--
If it involves a failing generated test, please say whether the test is wrong or the API is
wrong. Both are useful; they are different bugs.
-->
