using GitHalls.Core.Jira;
using Xunit;

namespace GitHalls.Core.Tests.Jira;

public class JiraWorkflowTests
{
    private static JiraTransition To(string name, string status, string category) =>
        new("1", name, status, category);

    [Fact]
    public void StartWork_PicksTheOnlyMoveIntoAnInProgressStatus()
    {
        var transitions = new[]
        {
            To("Done", "Done", "done"),
            To("Begin", "In Development", "indeterminate")
        };

        var choice = JiraWorkflow.StartWork(transitions, "new");

        Assert.Equal(JiraStartWork.Move, choice.Outcome);
        Assert.Equal("In Development", choice.Transition!.ToStatus);
    }

    [Fact]
    public void StartWork_PrefersTheOneNamedLikeStartingWorkWhenSeveralQualify()
    {
        var transitions = new[]
        {
            To("Send to review", "In Review", "indeterminate"),
            To("Start progress", "In Progress", "indeterminate")
        };

        var choice = JiraWorkflow.StartWork(transitions, "new");

        Assert.Equal("In Progress", choice.Transition!.ToStatus);
    }

    [Fact]
    public void StartWork_RecognisesThePortugueseNameToo()
    {
        var transitions = new[]
        {
            To("Enviar para revisão", "Em Revisão", "indeterminate"),
            To("Iniciar", "Em Andamento", "indeterminate")
        };

        var choice = JiraWorkflow.StartWork(transitions, "new");

        Assert.Equal("Em Andamento", choice.Transition!.ToStatus);
    }

    [Fact]
    public void StartWork_FallsBackToWorkflowOrderWhenNothingElseDecides()
    {
        var transitions = new[]
        {
            To("Triage", "Triaged", "indeterminate"),
            To("Escalate", "Escalated", "indeterminate")
        };

        var choice = JiraWorkflow.StartWork(transitions, "new");

        Assert.Equal(JiraStartWork.Move, choice.Outcome);
        Assert.Equal("Triaged", choice.Transition!.ToStatus);
    }

    [Fact]
    public void StartWork_SaysTheIssueIsAlreadyInProgressRatherThanMovingItAgain()
    {
        var transitions = new[] { To("Restart", "In Progress", "indeterminate") };

        var choice = JiraWorkflow.StartWork(transitions, "indeterminate");

        Assert.Equal(JiraStartWork.AlreadyInProgress, choice.Outcome);
        Assert.Null(choice.Transition);
    }

    [Fact]
    public void StartWork_SaysThereIsNoCandidateWhenTheWorkflowOffersNone()
    {
        var transitions = new[] { To("Close", "Done", "done") };

        var choice = JiraWorkflow.StartWork(transitions, "new");

        Assert.Equal(JiraStartWork.NoCandidate, choice.Outcome);
        Assert.Null(choice.Transition);
    }

    [Fact]
    public void StartWork_SaysThereIsNoCandidateWhenTheWorkflowIsEmpty()
    {
        var choice = JiraWorkflow.StartWork(Array.Empty<JiraTransition>(), "new");

        Assert.Equal(JiraStartWork.NoCandidate, choice.Outcome);
    }

    [Fact]
    public void Label_NamesTheTargetStatusBecauseThatIsTheColumnPeopleSee()
    {
        var move = To("Start progress", "In Progress", "indeterminate");
        var all = new[] { move, To("Close", "Done", "done") };

        Assert.Equal("In Progress", JiraWorkflow.Label(move, all));
    }

    [Fact]
    public void Label_AddsTheTransitionNameWhenTwoMovesLandOnTheSameStatus()
    {
        var reopen = To("Reopen", "To Do", "new");
        var all = new[] { reopen, To("Send back", "To Do", "new") };

        Assert.Equal("To Do (Reopen)", JiraWorkflow.Label(reopen, all));
    }
}
