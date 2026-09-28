namespace Flow.Shared.Contracts.Tasks;

/// <summary>Обе оценки задаются вместе, null — снять. StoryPoints 0…999.9 с шагом 0.1, EstimateMinutes ≥ 0.</summary>
public sealed record SetTaskEstimateRequest(decimal? StoryPoints, int? EstimateMinutes);
