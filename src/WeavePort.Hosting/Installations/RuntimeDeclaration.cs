using System.Text.Json;
using Chasm.SemanticVersioning.Ranges;
using Tomlyn;
using Tomlyn.Model;

namespace WeavePort.Hosting;
internal sealed record RuntimeDeclaration(string Ecosystem, string Source, string Requirement, string? Sha256 = null);
