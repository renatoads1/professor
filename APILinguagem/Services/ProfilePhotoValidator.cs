namespace APILinguagem.Services;

internal static class ProfilePhotoValidator
{
    public const int MaxFileBytes = 2 * 1024 * 1024;

    public static bool TryValidate(byte[]? photo, string? contentType, out string? error)
    {
        if (photo is null)
        {
            error = string.IsNullOrWhiteSpace(contentType)
                ? null
                : "O tipo da imagem foi informado sem o arquivo da foto.";
            return error is null;
        }

        if (photo.Length is 0 or > MaxFileBytes || string.IsNullOrWhiteSpace(contentType))
        {
            error = "A foto deve ter até 2 MiB e ser JPEG, PNG ou WebP.";
            return false;
        }

        var validImage = contentType switch
        {
            "image/jpeg" => IsJpeg(photo),
            "image/png" => IsPng(photo),
            "image/webp" => IsWebP(photo),
            _ => false
        };

        error = validImage ? null : "A foto deve ser um arquivo JPEG, PNG ou WebP válido.";
        return validImage;
    }

    private static bool IsJpeg(byte[] photo) =>
        photo.Length >= 3
        && photo[0] == 0xFF
        && photo[1] == 0xD8
        && photo[2] == 0xFF;

    private static bool IsPng(byte[] photo) =>
        photo.Length >= 8
        && photo[0] == 0x89
        && photo[1] == 0x50
        && photo[2] == 0x4E
        && photo[3] == 0x47
        && photo[4] == 0x0D
        && photo[5] == 0x0A
        && photo[6] == 0x1A
        && photo[7] == 0x0A;

    private static bool IsWebP(byte[] photo) =>
        photo.Length >= 12
        && photo[0] == (byte)'R'
        && photo[1] == (byte)'I'
        && photo[2] == (byte)'F'
        && photo[3] == (byte)'F'
        && photo[8] == (byte)'W'
        && photo[9] == (byte)'E'
        && photo[10] == (byte)'B'
        && photo[11] == (byte)'P';
}
