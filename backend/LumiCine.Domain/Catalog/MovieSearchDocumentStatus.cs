namespace LumiCine.Domain.Catalog;

/// <summary>Lifecycle of a movie search document on the embedding queue.</summary>
public enum MovieSearchDocumentStatus
{
    Pending,
    Indexed,
    Failed
}
