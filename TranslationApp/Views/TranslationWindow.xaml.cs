using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TranslationApp.Models;
using TranslationApp.ViewModels;

namespace TranslationApp.Views;

public partial class TranslationWindow : Window
{
    public TranslationWindow(TranslationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    public TranslationViewModel ViewModel => (TranslationViewModel)DataContext;

    private void CandidateList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CandidateList.SelectedItem is TranslationCandidate candidate)
        {
            ViewModel.SelectCandidate(candidate);
            ResultTextBox.Focus();
            ResultTextBox.CaretIndex = ResultTextBox.Text.Length;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e) => ViewModel.Dispose();
}
