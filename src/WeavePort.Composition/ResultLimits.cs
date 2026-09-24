using System.Security.Cryptography;

namespace WeavePort.Composition;
/// <summary>Limits retained result bytes and object count within one trusted product request.</summary>
public sealed record ResultLimits(long MaximumObjectBytes = 256L * 1024 * 1024, long MaximumScopeBytes = 1024L * 1024 * 1024, int MaximumObjects = 32);
