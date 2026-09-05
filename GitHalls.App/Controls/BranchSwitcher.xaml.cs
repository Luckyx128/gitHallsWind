using GitHalls.App.ViewModels;
using GitHalls.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace GitHalls.App.Controls;

/// <summary>
/// Branch picker plus branch creation, shown from the toolbar.
/// Port of BranchSwitcherView.swift.
/// </summary>
public sealed partial class BranchSwitcher : UserControl
{
    private RepositoryViewModel? _viewModel;

    /// <summary>Raised once an action was taken, so the hosting flyout can close.</summary>
    public event EventHandler? ActionCompleted;

    public BranchSwitcher()
    {
        InitializeComponent();

        Picker.Mode = BranchPickerMode.Invoke;
        Picker.BranchInvoked += Picker_BranchInvoked;
    }

    /// <summary>Called by the host every time the flyout opens.</summary>
    public void Load(RepositoryViewModel viewModel)
    {
        _viewModel = viewModel;

        NewBranchTextBox.Text = string.Empty;
        CreateHintText.Visibility = Visibility.Collapsed;
        CreateBranchButton.IsEnabled = false;

        Picker.Load(viewModel);
        Picker.FocusFilter();
    }

    private async void Picker_BranchInvoked(object? sender, Branch branch)
    {
        if (_viewModel == null) return;

        ActionCompleted?.Invoke(this, EventArgs.Empty);

        // CheckoutName, not Name: checking out "origin/main" literally detaches
        // HEAD instead of creating a local branch that tracks it.
        await _viewModel.SwitchBranchAsync(branch.CheckoutName);
    }

    // MARK: - Creating

    private void NewBranchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateNewBranchName();

    private void NewBranchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        if (CreateBranchButton.IsEnabled) CreateBranch();
    }

    private void CreateBranchButton_Click(object sender, RoutedEventArgs e) => CreateBranch();

    /// <summary>
    /// Catches the name problems worth catching before git does: an empty name,
    /// one that already exists, and the characters git refuses outright.
    /// </summary>
    private void ValidateNewBranchName()
    {
        var name = NewBranchTextBox.Text.Trim();

        string? problem = null;
        if (name.Length == 0)
        {
            problem = null; // nothing typed yet — no complaint, just disabled
        }
        else if (_viewModel != null && _viewModel.Branches.Any(b => !b.IsRemote && b.Name == name))
        {
            problem = $"'{name}' already exists.";
        }
        else if (name.IndexOfAny(new[] { ' ', '~', '^', ':', '?', '*', '[', '\\' }) >= 0)
        {
            problem = "A branch name can't contain spaces or ~ ^ : ? * [ \\";
        }
        else if (name.StartsWith('-') || name.EndsWith('/') || name.EndsWith(".lock") || name.Contains(".."))
        {
            problem = "Invalid branch name.";
        }

        CreateBranchButton.IsEnabled = name.Length > 0 && problem == null;
        CreateHintText.Text = problem ?? string.Empty;
        CreateHintText.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void CreateBranch()
    {
        if (_viewModel == null) return;

        var name = NewBranchTextBox.Text.Trim();
        if (name.Length == 0) return;

        NewBranchTextBox.Text = string.Empty;
        ActionCompleted?.Invoke(this, EventArgs.Empty);
        await _viewModel.CreateBranchAsync(name);
    }
}
