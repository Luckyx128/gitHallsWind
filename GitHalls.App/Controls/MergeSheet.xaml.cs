using GitHalls.App.ViewModels;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitHalls.App.Controls;

/// <summary>
/// Merge dialog content: the same branch picker the switcher uses, plus an
/// explicit statement of which branch is about to change.
/// Port of MergeSheetView.swift.
/// </summary>
public sealed partial class MergeSheet : UserControl
{
    private RepositoryViewModel? _viewModel;

    /// <summary>Raised when the selection becomes valid or invalid, so the dialog can enable its button.</summary>
    public event EventHandler<bool>? SelectionValidChanged;

    public Branch? SelectedBranch => Picker.SelectedBranch;

    public MergeSheet()
    {
        InitializeComponent();

        Picker.Mode = BranchPickerMode.Select;
        // Recency says nothing about what you want to merge, and merging a
        // branch into itself is not a thing.
        Picker.ShowRecent = false;
        Picker.IncludeCurrent = false;
        Picker.SelectedBranchChanged += Picker_SelectedBranchChanged;
    }

    public void Load(RepositoryViewModel viewModel)
    {
        _viewModel = viewModel;

        TargetText.Text = viewModel.CurrentBranch?.Name ?? "?";
        Picker.Load(viewModel);
        UpdateForSelection(null);
        Picker.FocusFilter();
    }

    private void Picker_SelectedBranchChanged(object? sender, Branch? branch) => UpdateForSelection(branch);

    private void UpdateForSelection(Branch? branch)
    {
        var target = _viewModel?.CurrentBranch?.Name ?? "?";

        SourceText.Text = branch?.Name ?? "Select a branch";

        if (branch == null)
        {
            WarningBar.IsOpen = false;
        }
        else
        {
            WarningBar.Title = $"\"{target}\" will be updated";
            WarningBar.Message = $"Commits from \"{branch.Name}\" will be added to \"{target}\". \"{branch.Name}\" itself won't change.";
            WarningBar.IsOpen = true;
        }

        SelectionValidChanged?.Invoke(this, branch != null);
    }
}
