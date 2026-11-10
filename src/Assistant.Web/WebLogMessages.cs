namespace Assistant.Web;

/// <summary>
/// Failures are logged with the exception's details masked (Assistant.Privacy), never the raw exception: its
/// message may quote what the customer or the model wrote, and the console log has no scrubber.
/// </summary>
internal static partial class WebLogMessages
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Chat turn failed: {Details}")]
    public static partial void ChatTurnFailed(ILogger logger, string details);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading demo customers failed: {Details}")]
    public static partial void CustomersFailed(ILogger logger, string details);
}
