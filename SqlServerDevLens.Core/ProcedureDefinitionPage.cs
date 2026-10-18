namespace SqlServerDevLens.Core;

public sealed record ProcedureDefinitionPage(string Schema, string Name, string Definition, string Revision,
    bool AnsiNulls, bool QuotedIdentifier, IReadOnlyList<ProcedureParameterInfo> Parameters,
    int TotalCharacters, int Offset, int? NextOffset, bool Complete)
{
    public static void Validate(int offset, int maxCharacters, string? expectedRevision)
    {
        if (offset < 0 || maxCharacters is < 256 or > 128000)
            throw new ArgumentException("offset must be nonnegative; maxCharacters must be 256–128,000.");
        if (offset > 0 && string.IsNullOrWhiteSpace(expectedRevision))
            throw new ArgumentException("Continuation requires expectedRevision from the first page.");
    }

    public static ProcedureDefinitionPage Create(ProcedureDefinition source, int offset = 0,
        int maxCharacters = 32000, string? expectedRevision = null)
    {
        Validate(offset, maxCharacters, expectedRevision);
        if (expectedRevision is not null && expectedRevision != source.Revision)
            throw new InvalidOperationException("Procedure revision changed. Discard previously read pages and restart at offset 0.");
        var text = source.Definition;
        if (offset > text.Length || (offset > 0 && offset < text.Length &&
            char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1])))
            throw new ArgumentException("Invalid definition offset. Use the returned nextOffset.");
        var count = Math.Min(maxCharacters, text.Length - offset);
        if (offset + count < text.Length && count > 0 && char.IsHighSurrogate(text[offset + count - 1]) &&
            char.IsLowSurrogate(text[offset + count])) count--;
        var end = offset + count;
        return new(source.Schema, source.Name, text.Substring(offset, count), source.Revision,
            source.AnsiNulls, source.QuotedIdentifier, source.Parameters, text.Length, offset,
            end < text.Length ? end : null, end == text.Length);
    }
}
