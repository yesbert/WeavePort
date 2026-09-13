# Internal quality candidate qualification — 2026-09-12

Source: `2c20723a6308d9f8cb2a5547e533cfd7b4c3bc52`. A fresh detached checkout and fresh NuGet/npm/pip caches qualified four core packages at `0.1.0-internal.2` plus the original MIT-licensed Microsoft logging dependency closure at `10.0.11`. Python and TypeScript author SDK versions remain `0.1.0`.

All stages passed: 398 assertion executions, comprising 78 source-host checks, the same 78 checks against packed Hosting, five gateway/client lifecycle scenarios, three documentation-gate scenarios, 144 application assertions, 12 SDK version assertions, 12 native crash/recovery assertions and 66 package/API/installation checks. Repeated source/packed assertions are deliberately counted as executions, not unique behaviors. The code formatting/size gate also passed with no exceptions.

135 artifacts were frozen and unchanged through verification. The negative changed-package control was refused. Two tracked Decision Room lockfiles refreshed content hashes only; dependency topology remained unchanged. Runtime executable hashes remained unchanged. The API baseline adds only the optional logger constructor and preserves all previous signatures.

Scope: native macOS arm64, SDK 10.0.401, shared .NET runtime 10.0.12, Python 3.14.7 and Node 26.8.2. This run does not qualify Docker behavior, Windows or escaped native descendants. Docker services and sibling applications were not changed. Distribution installation is a subsequent check with its own evidence.
