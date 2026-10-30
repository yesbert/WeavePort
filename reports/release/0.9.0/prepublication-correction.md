# Prepublication quality correction

The main-only SonarQube workflow after PR #48 reported six maintainability findings in run [41022457954](https://github.com/yesbert/WeavePort/actions/runs/41022457954). No release tag or package had been published at that point.

[PR #49](https://github.com/yesbert/WeavePort/pull/49) removed an unused runtime-selection parameter and local, replaced a dictionary Values.Contains call with ContainsValue, renamed outer internal signature helpers to avoid shadowing serialized envelope properties, and cached test-only JSON options. No wire fields, public APIs, dependencies or policy behavior changed. Existing hosting, reflection-disabled sealing, publisher/worker interoperability and C# style checks passed; PR CI repeated complete qualification.

The correction merged as `f94c45d394ac50ca558c6437400a5c15fb3bec38`. Main analysis [41024601964](https://github.com/yesbert/WeavePort/actions/runs/41024601964) passed with zero open issues and zero unreviewed hotspots. Coverage was 81.7% overall and 82.4% on new code; duplication was 0.0%. The complete [analysis export](analysis/summary.md) is retained here. No suppressions, threshold reductions or protection bypasses were used.

The annotated v0.9.0 tag identifies this corrected commit. The earlier preparation qualification remains historical evidence for its own recorded commit, not evidence for these final package bytes.
