namespace Flow.Shared.Contracts.Users;

/// <summary>Тип ссылки идёт в маршруте (PUT /users/{id}/links/{type}), в теле — только URL.</summary>
public sealed record SetUserLinkRequest(string Url);
