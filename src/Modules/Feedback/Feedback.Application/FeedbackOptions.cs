using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Feedback.Application;

/// <summary><c>Feedback</c>: the rating window and the de-duplication secret. Validated at start-up.</summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>Development / Testing only. Production must set its own (losing it lets each guest rate once more).</summary>
    public const string DevelopmentSecret = "development-feedback-dedupe-secret-not-for-production";

    /// <summary>
    ///     Server secret in the one-way rating hash (BR-027). At least 32 characters. Keep it in the password manager:
    ///     without it nobody, admins included, can tell which guest wrote a rating.
    /// </summary>
    public string DedupeSecret { get; set; } = string.Empty;

    /// <summary>Guests can rate a date from their check-in until this many days after the date starts.</summary>
    public int WindowDays { get; init; } = 7;

    /// <summary>Comments one admin page returns at most.</summary>
    public int MaxPageSize { get; init; } = 200;
}

public sealed class FeedbackOptionsValidator(IHostEnvironment environment) : IValidateOptions<FeedbackOptions>
{
    public const int MinSecretLength = 32;

    public ValidateOptionsResult Validate(string? name, FeedbackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var local = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        if (options.DedupeSecret.Length < MinSecretLength && !local)
        {
            failures.Add($"Feedback:DedupeSecret is required, at least {MinSecretLength} characters (set it as an environment variable).");
        }

        if (!local && options.DedupeSecret == FeedbackOptions.DevelopmentSecret)
        {
            failures.Add("Feedback:DedupeSecret must not be the development secret outside Development.");
        }

        if (options.WindowDays is < 1 or > 60)
        {
            failures.Add("Feedback:WindowDays must be 1-60.");
        }

        if (options.MaxPageSize is < 10 or > 1000)
        {
            failures.Add("Feedback:MaxPageSize must be 10-1000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Development and Testing get the development secret when none is configured.</summary>
public sealed class FeedbackDevelopmentSecret(IHostEnvironment environment) : IPostConfigureOptions<FeedbackOptions>
{
    public void PostConfigure(string? name, FeedbackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.DedupeSecret) && (environment.IsDevelopment() || environment.IsEnvironment("Testing")))
        {
            options.DedupeSecret = FeedbackOptions.DevelopmentSecret;
        }
    }
}
