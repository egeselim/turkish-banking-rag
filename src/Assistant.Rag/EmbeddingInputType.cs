using Microsoft.Extensions.AI;

namespace Assistant.Rag;

/// <summary>
/// Asymmetric embedding models encode search queries and indexed passages differently.
/// Every embedding call declares which side it is on via <see cref="EmbeddingGenerationOptions.AdditionalProperties"/>.
/// </summary>
public static class EmbeddingInputType
{
    public const string PropertyName = "input_type";
    public const string Query = "query";
    public const string Passage = "passage";

    public static EmbeddingGenerationOptions ForQuery() => Create(Query);

    public static EmbeddingGenerationOptions ForPassage() => Create(Passage);

    private static EmbeddingGenerationOptions Create(string inputType) =>
        new() { AdditionalProperties = new() { [PropertyName] = inputType } };
}
