using Flow.Ai.Agents;
using Flow.Ai.DependencyInjection;
using Flow.Ai.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlowAi(builder.Configuration);

var app = builder.Build();

app.MapGet("/", () => "Flow.Api");

// Демонстрационный эндпойнт: свободный текст -> черновик задачи (F14, TZ_ai_local_assistant.md).
// Ничего не пишет в БД — только возвращает провалидированный черновик,
// создание TaskItem и подтверждение пользователем — на стороне вызывающего UI.
app.MapPost("/ai/tasks/draft", async (DraftTaskRequest request, GeneratorAgent agent) =>
{
    var result = await agent.RunAsync(TaskDraftSystemPrompt.Text, request.Text, new TaskDraftValidator());

    if (!result.Success)
        return Results.UnprocessableEntity(new { result.Reason, result.RawOutput, result.Attempts });

    var draft = TaskDraftValidator.Parse(result.RawOutput);
    return Results.Ok(new { draft, result.Attempts });
});

app.Run();

internal sealed record DraftTaskRequest(string Text);
