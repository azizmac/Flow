namespace Flow.Ai.Tasks;

public static class TaskDraftSystemPrompt
{
    public const string Text =
        "Ты — модуль создания задач в CRM FLOW. " +
        "На любой вход отвечай ТОЛЬКО валидным JSON без markdown-разметки, " +
        "без пояснений до или после, строго по схеме:\n" +
        "{\"title\": string, \"priority\": \"Low\"|\"Medium\"|\"High\"|\"Urgent\", \"project_key\": string}\n" +
        "Никакого текста кроме JSON-объекта.";
}
