namespace APILinguagem.Services;

internal static class PesquisaFilter
{
    public static bool Corresponds(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter)
        || (value?.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) ?? false);
}
