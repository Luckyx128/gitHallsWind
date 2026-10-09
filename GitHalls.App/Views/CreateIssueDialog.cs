using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GitHalls.App.ViewModels;
using GitHalls.Core.Jira;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GitHalls.App.Views;

/// <summary>
/// The Create Issue form. Project and type are picked from what Jira offers,
/// and the type decides the rest: every field createmeta marks required gets a
/// row and is checked before anything is sent — a project like SWEB requires
/// Description, Team and Original Estimate, and a form that only asked for a
/// summary failed there. The new issue goes into the active sprint unless the
/// user picks Backlog. Built in code, like <see cref="QueryDialog"/>: its rows
/// depend on what Jira answers.
/// </summary>
internal sealed class CreateIssueDialog
{
    /// <summary>One required field beyond the form's own: how to read it and what is wrong with it.</summary>
    private sealed record FieldRow(JiraCreateField Field, Func<JsonElement?> Value, Func<string?> Problem);

    /// <summary>The Sprint box's entries; no sprint means the backlog.</summary>
    private sealed record SprintChoice(JiraSprint? Sprint)
    {
        public override string ToString() => Sprint?.ToString() ?? "Backlog";
    }

    private readonly JiraViewModel _viewModel;
    private readonly string? _preferredProjectKey;

    private readonly ComboBox _projectBox = new() { Header = "Project", PlaceholderText = "Loading…", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _typeBox = new() { Header = "Issue type", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _summaryBox = new() { Header = "Summary *" };
    private readonly TextBox _descriptionBox = new() { Header = "Description", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96 };
    private readonly ComboBox _sprintBox = new() { Header = "Sprint", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _extraPanel = new() { Spacing = 10 };
    private readonly ProgressRing _loading = new() { IsActive = false, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _errorText = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

    private IReadOnlyList<JiraCreateField> _fields = Array.Empty<JiraCreateField>();
    private readonly List<FieldRow> _rows = new();

    /// <summary>
    /// Bumped on every project change, and on every type change: an answer for
    /// an older choice is dropped. Two counters, because clearing the type list
    /// for a new project fires a type change of its own, which must not void
    /// the project's load.
    /// </summary>
    private int _projectToken;
    private int _typeToken;

    private CreateIssueDialog(JiraViewModel viewModel, string? preferredProjectKey)
    {
        _viewModel = viewModel;
        _preferredProjectKey = preferredProjectKey;
        _errorText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
    }

    /// <summary>The new issue's key, or null when the user cancelled.</summary>
    public static Task<string?> ShowAsync(XamlRoot xamlRoot, JiraViewModel viewModel, string? preferredProjectKey) =>
        new CreateIssueDialog(viewModel, preferredProjectKey).RunAsync(xamlRoot);

    private async Task<string?> RunAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Create issue",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 560,
                Content = new StackPanel
                {
                    Spacing = 10,
                    MinWidth = 440,
                    Children = { _projectBox, _typeBox, _summaryBox, _descriptionBox, _sprintBox, _extraPanel, _loading, _errorText }
                }
            }
        };

        string? createdKey = null;

        _projectBox.SelectionChanged += async (_, _) => await ProjectChangedAsync();
        _typeBox.SelectionChanged += async (_, _) => await TypeChangedAsync();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // The dialog closes when this handler returns unless told not to;
            // the deferral keeps it open while Jira answers.
            var deferral = args.GetDeferral();
            try
            {
                createdKey = await SubmitAsync();
                args.Cancel = createdKey == null;
            }
            finally
            {
                deferral.Complete();
            }
        };

        _ = LoadProjectsAsync();

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? createdKey : null;
    }

    // MARK: - Loading

    private async Task LoadProjectsAsync()
    {
        await RunLoadAsync(async () =>
        {
            var projects = await _viewModel.ProjectsAsync();
            _projectBox.ItemsSource = projects;
            _projectBox.PlaceholderText = projects.Count == 0 ? "No projects" : "Choose a project";
            _projectBox.SelectedItem = projects.FirstOrDefault(p => string.Equals(p.Key, _preferredProjectKey, StringComparison.OrdinalIgnoreCase))
                                       ?? projects.FirstOrDefault();
        });
    }

    private async Task ProjectChangedAsync()
    {
        var token = ++_projectToken;
        _typeBox.ItemsSource = null;
        _sprintBox.ItemsSource = null;
        ClearFields();
        if (_projectBox.SelectedItem is not JiraProject project) return;

        await RunLoadAsync(async () =>
        {
            var typesTask = _viewModel.IssueTypesAsync(project.Key);
            var sprintsTask = LoadSprintsOrNoneAsync(project.Key);
            var types = await typesTask;
            var sprints = await sprintsTask;
            if (token != _projectToken) return;

            var choices = new List<SprintChoice> { new(null) };
            choices.AddRange(sprints.Select(sprint => new SprintChoice(sprint)));
            _sprintBox.ItemsSource = choices;
            var active = JiraSprintChoice.DefaultSprint(sprints);
            _sprintBox.SelectedItem = choices.FirstOrDefault(choice => choice.Sprint == active) ?? choices[0];

            // A plain task first: a sub-task needs a parent this form doesn't ask for.
            var creatable = types.Where(type => !type.IsSubtask).ToList();
            _typeBox.ItemsSource = creatable;
            _typeBox.SelectedItem = creatable.FirstOrDefault(type => type.Name is "Task" or "Tarefa") ?? creatable.FirstOrDefault();
        });
    }

    /// <summary>A project without a scrum board has no sprints; that is not an error worth stopping the form for.</summary>
    private async Task<IReadOnlyList<JiraSprint>> LoadSprintsOrNoneAsync(string projectKey)
    {
        try
        {
            return await _viewModel.OpenSprintsAsync(projectKey);
        }
        catch (JiraException)
        {
            return Array.Empty<JiraSprint>();
        }
    }

    private async Task TypeChangedAsync()
    {
        var token = ++_typeToken;
        ClearFields();
        if (_projectBox.SelectedItem is not JiraProject project || _typeBox.SelectedItem is not JiraIssueType type) return;

        await RunLoadAsync(async () =>
        {
            var fields = await _viewModel.CreateFieldsAsync(project.Key, type.Id);
            if (token != _typeToken || !ReferenceEquals(_projectBox.SelectedItem, project)) return;

            _fields = fields;
            _descriptionBox.Header = IsRequired("description") ? "Description *" : "Description";
            foreach (var field in JiraCreateFieldValue.DynamicFields(fields))
            {
                AddRow(field);
            }
        });
    }

    private async Task RunLoadAsync(Func<Task> load)
    {
        _loading.IsActive = true;
        ShowError(null);
        try
        {
            await load();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _loading.IsActive = false;
        }
    }

    private void ClearFields()
    {
        _fields = Array.Empty<JiraCreateField>();
        _rows.Clear();
        _extraPanel.Children.Clear();
        _descriptionBox.Header = "Description";
    }

    private bool IsRequired(string key) => _fields.Any(field => field.Key == key && field.NeedsInput);

    // MARK: - Required field rows

    private void AddRow(JiraCreateField field)
    {
        var header = field.Name + " *";

        switch (JiraCreateFieldValue.InputFor(field))
        {
            case JiraFieldInput.Select:
            {
                var box = new ComboBox { Header = header, ItemsSource = field.Allowed, HorizontalAlignment = HorizontalAlignment.Stretch };
                _extraPanel.Children.Add(box);
                string? Id() => (box.SelectedItem as JiraFieldOption)?.Id;
                _rows.Add(new FieldRow(field, () => JiraCreateFieldValue.FromText(field, Id()), () => JiraCreateFieldValue.Problem(field, Id())));
                break;
            }

            case JiraFieldInput.MultiSelect:
            {
                var list = new ListView { Header = header, ItemsSource = field.Allowed, SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 160 };
                _extraPanel.Children.Add(list);
                IEnumerable<string> Ids() => list.SelectedItems.OfType<JiraFieldOption>().Select(option => option.Id);
                _rows.Add(new FieldRow(field, () => JiraCreateFieldValue.FromIds(Ids()), () => Ids().Any() ? null : "Required."));
                break;
            }

            case JiraFieldInput.Team:
            {
                JiraFieldOption? chosen = null;
                var box = new AutoSuggestBox { Header = header, PlaceholderText = "Type to search teams" };
                box.TextChanged += async (sender, args) =>
                {
                    if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInputChange) return;
                    if (chosen != null && sender.Text != chosen.Label) chosen = null;

                    var query = sender.Text;
                    try
                    {
                        var teams = await _viewModel.FindTeamsAsync(query, field.AutoCompleteUrl);
                        // Typing went on while Jira answered: this list is for older text.
                        if (sender.Text == query) sender.ItemsSource = teams;
                    }
                    catch (JiraException ex)
                    {
                        ShowError(ex.Message);
                    }
                };
                box.SuggestionChosen += (sender, args) =>
                {
                    chosen = args.SelectedItem as JiraFieldOption;
                    if (chosen != null) sender.Text = chosen.Label;
                };
                _extraPanel.Children.Add(box);
                _rows.Add(new FieldRow(field, () => JiraCreateFieldValue.FromText(field, chosen?.Id),
                                       () => chosen == null ? "Choose a team from the list." : null));
                break;
            }

            case JiraFieldInput.Unsupported:
            {
                // Not blocked here: Jira says what it wants, and that message
                // is shown, rather than a form that can never be submitted.
                _extraPanel.Children.Add(new TextBlock
                {
                    Text = $"{field.Name} is required, and this form can't fill it yet — Jira may refuse the issue without it.",
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["GHBodyMutedTextStyle"]
                });
                break;
            }

            default:
            {
                var input = JiraCreateFieldValue.InputFor(field);
                var box = new TextBox
                {
                    Header = header,
                    PlaceholderText = input switch
                    {
                        JiraFieldInput.Duration => "2h 30m",
                        JiraFieldInput.Date => "yyyy-MM-dd",
                        JiraFieldInput.DateTime => "yyyy-MM-ddTHH:mm:ss.000-0300",
                        JiraFieldInput.Labels => "Separated by commas or spaces",
                        JiraFieldInput.Number => "0",
                        _ => string.Empty
                    },
                    AcceptsReturn = input == JiraFieldInput.Adf,
                    TextWrapping = input == JiraFieldInput.Adf ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MinHeight = input == JiraFieldInput.Adf ? 72 : 0
                };
                _extraPanel.Children.Add(box);
                _rows.Add(new FieldRow(field, () => JiraCreateFieldValue.FromText(field, box.Text),
                                       () => JiraCreateFieldValue.Problem(field, box.Text)));
                break;
            }
        }
    }

    // MARK: - Submitting

    /// <summary>The new key, or null with the reason on screen.</summary>
    private async Task<string?> SubmitAsync()
    {
        if (_projectBox.SelectedItem is not JiraProject project || _typeBox.SelectedItem is not JiraIssueType type)
        {
            ShowError("Choose a project and an issue type.");
            return null;
        }

        var problems = new List<string>();
        var summary = _summaryBox.Text.Trim();
        if (summary.Length == 0) problems.Add("Summary: Required.");

        var description = _descriptionBox.Text.Trim();
        if (description.Length == 0 && IsRequired("description")) problems.Add("Description: Required.");

        var extra = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var row in _rows)
        {
            if (row.Problem() is { } problem)
            {
                problems.Add($"{row.Field.Name}: {problem}");
                continue;
            }
            if (row.Value() is { } value) extra[row.Field.Key] = value;
        }

        if (problems.Count > 0)
        {
            ShowError(string.Join("\n", problems));
            return null;
        }

        var parameters = new JiraIssueCreateParameters(project.Key, summary, type.Name)
        {
            IssueTypeId = type.Id,
            Description = description.Length == 0 ? null : JiraAdf.FromPlainText(description),
            ExtraFields = extra.Count == 0 ? null : extra
        };

        _loading.IsActive = true;
        ShowError(null);
        try
        {
            return await _viewModel.CreateIssueAsync(parameters, (_sprintBox.SelectedItem as SprintChoice)?.Sprint);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return null;
        }
        finally
        {
            _loading.IsActive = false;
        }
    }

    private void ShowError(string? message)
    {
        _errorText.Text = message ?? string.Empty;
        _errorText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }
}
