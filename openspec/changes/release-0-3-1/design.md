# Release design

Use patch 0.3.1 for additive named invocation methods and internal constant refactoring. Keep all core packages aligned under the exact compatibility policy. The public API diff adds only McpMethods with ListTools and CallTool; existing literal calls remain supported. Constant values are compile-time strings. Existing independent wire fixtures remain unchanged.

Public NuGet 0.3.0 is immutable. Publish only fresh qualified 0.3.1 artifacts through the normal protected workflow. Remove article transition language after public availability is verified; retain the 0.3.0 benchmark attribution because no new performance claim is made.
