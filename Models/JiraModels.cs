namespace JiraQaMonitor.Models;

public record JiraTicket(
    string Key,
    string Summary,
    string Assignee,
    string AssigneeEmail,
    string Url
);

public class MonitorState
{
    public List<string> ReadyForQaIds { get; set; } = [];
    public List<string> VerifiedIds   { get; set; } = [];
}

public class JiraSearchResponse
{
    public List<JiraIssue> Issues { get; set; } = [];
}

public class JiraIssue
{
    public string Key { get; set; } = string.Empty;
    public JiraFields Fields { get; set; } = new();
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
