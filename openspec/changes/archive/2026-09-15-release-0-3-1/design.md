# Release design

Use patch 0.3.1 for additive named invocation methods and internal constant refactoring. Keep all core packages aligned under the exact compatibility policy. The public API diff adds only McpMethods with ListTools and CallTool; existing literal calls remain supported. Constant values are compile-time strings. Existing independent wire fixtures remain unchanged.

Public NuGet 0.3.0 is immutable. Publish only fresh qualified 0.3.1 artifacts through the normal protected workflow. Remove article transition language after public availability is verified; retain the 0.3.0 benchmark attribution because no new performance claim is made.

## Verification

PR 22 passed normal required gates and merged as 23d6dbb119c845fa913b05fc20a62267c4ac6bc6. Release workflow 34955711213 qualified 1,088 assertions and 139 artifacts, published all four original packages and sibling symbols, and created v0.3.1. All public package payloads match the originals except the added repository signature. Durable evidence lives in reports/release/0.3.1 and release assets. The DEV draft now references the released example; its 0.3.0 benchmark attribution is preserved.
