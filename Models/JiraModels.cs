using System.Text.Json.Serialization;

namespace JiraQaMonitor.Models;

public record StatusDuration(string Status, TimeSpan Duration);

public record JiraTicket(
    string Key,
    string Summary,
    string Assignee,
    string AssigneeEmail,
    string Url,
    IReadOnlyList<StatusDuration> StatusHistory,
    double?  StoryPoints,
    DateTime? CurrentStatusEnteredAt
);

public class ResolvedEntry
{
    public string    Key              { get; set; } = string.Empty;
    public DateTime  EnteredAt        { get; set; }
    public bool      InitialNotified  { get; set; }
    public DateTime? LastReminderAt   { get; set; }
}

public class MonitorState
{
    public List<string>        ReadyForQaIds    { get; set; } = [];
    public List<ResolvedEntry> ResolvedEntries  { get; set; } = [];
    public List<string>        VerifiedIds      { get; set; } = [];
    public List<string>        ClosedIds        { get; set; } = [];
}

public class AppSettings
{
    public ReviewReminderSettings ReviewReminder { get; set; } = new();
}

public class ReviewReminderSettings
{
    public double ThresholdHours { get; set; } = 4;
    public double IntervalHours  { get; set; } = 2;
}

public class TeamConfig
{
    public List<TeamDefinition> Teams    { get; set; } = [];
    public TeamWebhooks FallbackWebhooks { get; set; } = new();
}

public class TeamDefinition
{
    public string            Name     { get; set; } = string.Empty;
    public List<string>      Members  { get; set; } = [];
    public TeamWebhooks      Webhooks { get; set; } = new();
}

public class TeamWebhooks
{
    public string? Resolved   { get; set; }
    public string? ReadyForQa { get; set; }
    public string? Verified   { get; set; }
    public string? Closed     { get; set; }
}

public class JiraSearchResponse
{
    public List<JiraIssue> Issues { get; set; } = [];
}

public class JiraIssue
{
    public string       Key       { get; set; } = string.Empty;
    public JiraFields   Fields    { get; set; } = new();
    public JiraChangelog? Changelog { get; set; }
}

public class JiraFields
{
    public string       Summary     { get; set; } = string.Empty;
    public JiraAssignee? Assignee   { get; set; }

    [JsonPropertyName("customfield_10004")]
    public double? StoryPoints { get; set; }
}

public class JiraAssignee
{
    public string DisplayName  { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
}

public class JiraChangelog
{
    public List<JiraHistory> Histories { get; set; } = [];
}

public class JiraHistory
{
    public string Created { get; set; } = string.Empty;
    public List<JiraHistoryItem> Items { get; set; } = [];
}

public class JiraHistoryItem
{
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("fromString")]
    public string? FromStatus { get; set; }

    [JsonPropertyName("toString")]
    public string? ToStatus { get; set; }
}
