// RetroEye app API, contract v1: notifications a client asks RetroEye to show as the era's own tray balloon.
#nullable enable
using System;

namespace RetroEye.Api
{
    /// <summary>The icon a notification shows beside its title.</summary>
    public enum NotificationIcon
    {
        /// <summary>No icon.</summary>
        None,
        /// <summary>The information icon (an "i").</summary>
        Info,
        /// <summary>The warning icon (a "!").</summary>
        Warning,
        /// <summary>The error icon (an "X").</summary>
        Error,
    }

    /// <summary>
    /// A notification for RetroEye to show as the shown Windows version's own tray balloon. It is plain text only:
    /// nothing in it is run, opened or followed. Titles over 63 and texts over 255 characters are cut short.
    /// </summary>
    public sealed class Notification
    {
        /// <summary>The balloon's bold title (at most 63 characters).</summary>
        public string Title { get; set; } = "";

        /// <summary>The balloon's text (at most 255 characters).</summary>
        public string Text { get; set; } = "";

        /// <summary>The icon beside the title.</summary>
        public NotificationIcon Icon { get; set; } = NotificationIcon.Info;

        /// <summary>The app's name, shown where the version showed one (at most 64 characters); the client's name when null.</summary>
        public string? AppName { get; set; }

        /// <summary>How long it stays up, in milliseconds, 2,000 to 30,000; 10,000 when null.</summary>
        public int? TimeoutMs { get; set; }

        /// <summary>Any short text of the client's own (at most 64 characters), given back with the click and close events.</summary>
        public string? Tag { get; set; }

        /// <summary>Returns the notify message's JSON as contract v1 has it.</summary>
        public ApiJson ToJson()
        {
            var message = ApiJson.NewObject()
                .Set("type", "notify")
                .Set("title", Title ?? "")
                .Set("text", Text ?? "")
                .Set("icon", Icon switch { NotificationIcon.Warning => "warning", NotificationIcon.Error => "error", NotificationIcon.None => "none", _ => "info" });
            if (AppName != null) message.Set("app", AppName);
            if (TimeoutMs is int timeout) message.Set("timeoutMs", timeout);
            if (Tag != null) message.Set("tag", Tag);
            return message;
        }
    }

    /// <summary>What happened to a notification: clicked, or closed and why.</summary>
    public sealed class NotificationEventArgs : EventArgs
    {
        /// <summary>Makes the event's details.</summary>
        public NotificationEventArgs(string id, string? tag, string reason)
        {
            Id = id;
            Tag = tag;
            Reason = reason;
        }

        /// <summary>The id RetroEye gave the notification when it queued it.</summary>
        public string Id { get; }

        /// <summary>The tag the client gave it, if any.</summary>
        public string? Tag { get; }

        /// <summary>"clicked", or why it closed: "timeout", "dismissed", "clicked", "dropped" or "shuttingDown".</summary>
        public string Reason { get; }
    }

    /// <summary>An event RetroEye pushed: its name and the whole message.</summary>
    public sealed class RetroEyeEventArgs : EventArgs
    {
        /// <summary>Makes the event's details.</summary>
        public RetroEyeEventArgs(string name, ApiJson message, ThemeState? state)
        {
            Name = name;
            Message = message;
            State = state;
        }

        /// <summary>
        /// The event's name: "themeChanged", "versionChanged", "dpiChanged", "taskbarMoved", "started", "shuttingDown",
        /// "notificationClicked" or "notificationClosed" (and any a later contract adds).
        /// </summary>
        public string Name { get; }

        /// <summary>The whole event message as RetroEye sent it.</summary>
        public ApiJson Message { get; }

        /// <summary>The state after the change, for the events that carry one; otherwise null.</summary>
        public ThemeState? State { get; }
    }

    /// <summary>An error RetroEye answered a request with.</summary>
    public sealed class RetroEyeApiException : Exception
    {
        /// <summary>Makes the error from its code and message.</summary>
        public RetroEyeApiException(string code, string message) : base($"{code}: {message}") => Code = code;

        /// <summary>
        /// The error's code: "badJson", "tooLarge", "unknownType", "badArgument", "rateLimited", "queueFull",
        /// "helloFirst", "disabled" or "tooManyClients".
        /// </summary>
        public string Code { get; }
    }
}
