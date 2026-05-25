namespace VrBook.Contracts.Enums;

public enum NotificationChannel
{
    Email = 0,
    InApp = 1,
    Sms = 2,   // reserved for Phase 2
    Push = 3,   // reserved for Phase 2
}

public enum NotificationStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
    DeadLetter = 3,
}
