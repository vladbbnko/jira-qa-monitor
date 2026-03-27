using System.Text.Json.Serialization;

namespace JiraQaMonitor.Models;

public record StatusDuration(string Status, TimeSpan Duration);

public record JiraTicket(
    string Key,
    string Summary,
    string Assignee,
    string AssigneeEmail,
    string Url,
    IReadOnlyList<StatusDuration> StatusHistory
);

public class MonitorState
{
    public List<string> ReadyForQaIds { get; set; } = [];
    public List<string> VerifiedIds   { get; set; } = [];
    public List<string> ClosedIds     { get; set; } = [];
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
    public string Summary { get; set; } = string.Empty;
    public JiraAssignee? Assignee { get; set; }
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
