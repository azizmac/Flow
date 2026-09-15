namespace Flow.Application.Abstractions;

/// <summary>Что сделать с источником: перестроить чанки или убрать их из индекса.</summary>
public enum SearchIndexOperation
{
    Upsert = 1,
    Delete = 2
}
