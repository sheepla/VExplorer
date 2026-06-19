using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Logging;
using VExplorer.Core.State;

namespace VExplorer.App.Diagnostics;

/// <summary>
/// The origin of an error, deciding both its log level and how it surfaces to the
/// user. See <see cref="ErrorReporter"/> for the mapping.
/// </summary>
public enum ErrorCategory
{
    /// <summary>The user cancelled an operation (e.g. dismissed an OS dialog). Silent.</summary>
    Cancellation,

    /// <summary>The user's input was rejected (bad argument, missing path). Shown as a warning.</summary>
    UserInput,

    /// <summary>An external/environment failure (IO, access denied, shell HRESULT). Shown as an error.</summary>
    Environment,

    /// <summary>An unexpected failure indicating a bug. Shown generically; details go to the log.</summary>
    ProgrammingBug,
}

/// <summary>
/// Central place that turns a caught exception into a structured log entry and a
/// status-bar notification, classified by <see cref="ErrorCategory"/>. App-layer
/// orchestrators (command and file-op execution) route their failures through here
/// instead of handling logging and status messages ad hoc.
/// </summary>
public sealed class ErrorReporter(ILogger<ErrorReporter> logger)
{
    /// <summary>Classifies an exception by its origin.</summary>
    public static ErrorCategory Categorize(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => ErrorCategory.Cancellation,
            // FileNotFoundException/DirectoryNotFoundException derive from IOException.
            UnauthorizedAccessException
            or IOException
            or SecurityException
            or COMException
            or Win32Exception => ErrorCategory.Environment,
            ArgumentException or FormatException => ErrorCategory.UserInput,
            _ => ErrorCategory.ProgrammingBug,
        };
    }

    /// <summary>
    /// Logs <paramref name="ex"/> at the level its category warrants and notifies
    /// <paramref name="tab"/> with an appropriate severity. <paramref name="context"/>
    /// pairs are attached to the log entry as structured properties.
    /// </summary>
    public void Report(
        TabState tab,
        string operation,
        Exception ex,
        params (string Key, object? Value)[] context
    )
    {
        ErrorCategory category = Categorize(ex);
        using IDisposable? scope = BeginContextScope(context);

        switch (category)
        {
            case ErrorCategory.Cancellation:
                logger.LogDebug(ex, "{Operation} cancelled", operation);
                break;

            case ErrorCategory.UserInput:
                logger.LogDebug(ex, "{Operation} rejected: {Reason}", operation, ex.Message);
                tab.SetStatusMessage(ex.Message, StatusSeverity.Warning);
                break;

            case ErrorCategory.Environment:
                logger.LogWarning(ex, "{Operation} failed: {Reason}", operation, ex.Message);
                tab.SetStatusMessage(ex.Message, StatusSeverity.Error);
                break;

            default:
                logger.LogError(ex, "{Operation} failed unexpectedly", operation);
                tab.SetStatusMessage("Unexpected error — see log", StatusSeverity.Error);
                break;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/>, routing any exception through <see cref="Report"/>.
    /// Returns true on success, false if an exception was caught and reported.
    /// </summary>
    public async Task<bool> TryRunAsync(
        TabState tab,
        string operation,
        Func<Task> work,
        params (string Key, object? Value)[] context
    )
    {
        try
        {
            await work();
            return true;
        }
        catch (Exception ex)
        {
            Report(tab, operation, ex, context);
            return false;
        }
    }

    private IDisposable? BeginContextScope((string Key, object? Value)[] context)
    {
        if (context.Length == 0)
        {
            return null;
        }
        Dictionary<string, object> state = new(context.Length);
        foreach ((string key, object? value) in context)
        {
            state[key] = value ?? "(null)";
        }
        return logger.BeginScope(state);
    }
}
