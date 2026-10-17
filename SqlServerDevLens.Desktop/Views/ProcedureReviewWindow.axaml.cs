using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SqlServerDevLens.Core;

namespace SqlServerDevLens.Desktop.Views;

public partial class ProcedureReviewWindow : Window
{
    private readonly DateTimeOffset _expires;
    public ProcedureReviewWindow() { InitializeComponent(); }
    public ProcedureReviewWindow(ProcedureReview review) : this()
    {
        _expires = review.ExpiresAt;
        Heading.Text = $"Review {review.Action}: {review.Schema}.{review.Name}";
        Scope.Text = $"Database: {review.Database}. Expires: {review.ExpiresAt.ToLocalTime():HH:mm:ss}. " +
            (review.Action == "execute"
                ? "Executing this procedure may modify data, call other procedures or have external effects. Approval permits this exact parameter set once."
                : "This changes a database procedure. Review every change and its dependencies. ALTER requires a private local backup before application.") +
            " SQL Server permissions apply. External changes can race with the final revision check.";
        ReviewText.Text = review.Action == "execute" ? review.Parameters : review.Diff;
        BeforeText.Text = review.Before; AfterText.Text = review.Proposed;
    }
    private void Reject_Click(object? sender, RoutedEventArgs e) => Close(false);
    private void Approve_Click(object? sender, RoutedEventArgs e) => Close(DateTimeOffset.UtcNow < _expires);
}
