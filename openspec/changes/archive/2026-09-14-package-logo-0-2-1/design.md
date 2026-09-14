## Decision

Reuse website/assets/logo.png without editing or duplicating artwork. Its PNG format and 665452-byte size meet NuGet icon requirements. Shared source package props include it at logo.png. Require exact icon metadata and bytes during release export to prevent future branding omissions.

NuGet versions are immutable; publish 0.2.1 rather than replacing 0.2.0. Retain the existing release allowlist, Trusted Publishing gate and qualification process. Python/npm versions and historical evidence remain unchanged.

## References

https://learn.microsoft.com/en-us/nuget/reference/nuspec#icon
