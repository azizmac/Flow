namespace Flow.Client.Data;

public record TaskItem(
    string Title,
    string Project,
    string Due,
    string Color,
    string Initials,
    int Comments = 0,
    string Checklist = "0/0");

public record ActivityItem(string Name, string Initials, string Text, string Time = "");

public record ChecklistEntry(string Label, bool Done);

public record CommentItem(string Name, string Initials, string Text);

public record TeamSummary(string Name, int Size, string[] Initials, int Load, int ActiveTasks);

public record TeamMember(string Name, string Role, string Initials);

public record TeamTaskDetail(string Title, string Due, string Color, string? Pr, string Owner, string Initials);

public record StatusBreakdown(string Label, int Count, int Pct, string Color);

public record Deadline(string Title, string Due, string Color);

public record SettingsNavEntry(string Label, string Icon, string Href);

public record IntegrationEntry(string Name, string Desc, string Icon, string Action, string Variant);

public record MdLine(int N, string Text, string Color);
