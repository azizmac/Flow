namespace Flow.Domain.Entities.GitIntegration;

/// <summary>
/// Профиль бота интеграции (docs/TZ_scm_integration.md, принятые решения): от его имени пишутся автопереходы, когда
/// автор PR не сопоставлен или не может править задачу. Учётной записи в Auth-модуле нет — войти им нельзя; профиль
/// сразу деактивирован, поэтому его не назначить исполнителем и не упомянуть. Журнал ссылается на него FK — профиль
/// создаётся при первой надобности.
/// </summary>
public static class GitIntegrationBot
{
    public static readonly Guid Id = new("00000000-0000-0000-0000-00000000f10b");

    public const string Username = "flow-bot";

    public static User Create()
    {
        var bot = User.CreateWithId(Id, Username, "flow-bot@flow.local", "Flow", "Bot");
        bot.Deactivate();
        return bot;
    }
}
