using Microsoft.Extensions.Logging;

namespace Assistant.Core.Audit;

internal static partial class AuditLogMessages
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit:PseudonymKey is not set; using a random key, so audit pseudonyms will change on restart.")]
    public static partial void PseudonymKeyMissing(ILogger logger);

    // Only the action, the outcome and the exception type: never the row's data or the exception's message.
    [LoggerMessage(Level = LogLevel.Error, Message = "Audit row could not be written: {Action} {Outcome} ({ExceptionType})")]
    public static partial void WriteFailed(ILogger logger, string action, AuditOutcome outcome, string exceptionType);
}
