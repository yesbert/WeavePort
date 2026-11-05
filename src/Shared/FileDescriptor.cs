namespace WeavePort.Internal;
// Shared validation for function file names and media types; names are identifiers, never paths.
internal static class FileDescriptor
{
    internal static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > ProtocolLimits.FileNameMaximumLength || name[0] == '.')
        {
            return false;
        }

        return name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    internal static bool IsValidMediaType(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType) || mediaType.Length > ProtocolLimits.FileMediaTypeMaximumLength)
        {
            return false;
        }

        return mediaType.All(character => character is >= '!' and <= '~');
    }

    internal static string Name(string? name, string parameter) => IsValidName(name) ? name! : throw new ArgumentException("File names use 1-128 characters from A-Z, a-z, 0-9, '.', '_' and '-' and do not start with '.'.", parameter);
    internal static string MediaType(string? mediaType, string parameter) => IsValidMediaType(mediaType) ? mediaType! : throw new ArgumentException("Media types use 1-255 visible ASCII characters without spaces.", parameter);
}
