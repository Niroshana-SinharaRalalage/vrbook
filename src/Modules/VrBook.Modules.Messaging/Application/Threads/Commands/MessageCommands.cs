using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Messaging.Domain;
using VrBook.Modules.Messaging.Infrastructure.Persistence;

namespace VrBook.Modules.Messaging.Application.Threads.Commands;

public sealed record SendMessageCommand(Guid ThreadId, string Body) : IRequest<MessageDto>;

public sealed record MarkReadCommand(Guid ThreadId, Guid UpToMessageId) : IRequest;

internal sealed class SendMessageHandler(MessagingDbContext db, ICurrentUser currentUser)
    : IRequestHandler<SendMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(SendMessageCommand cmd, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous users cannot send messages.");
        }
        var me = currentUser.UserId.Value;
        var thread = await db.Threads.FirstOrDefaultAsync(t => t.Id == cmd.ThreadId, cancellationToken)
            ?? throw new NotFoundException("MessageThread", cmd.ThreadId);
        // Slice OPS.M.10.2 F7 (audit #14) — same-tenant non-participants
        // (a second guest sharing the tenant, a tenant Admin not on the
        // thread) would otherwise be able to spoof messages because RLS
        // gates cross-tenant, NOT cross-participant. Sibling MarkReadHandler
        // already has this check (line 57-60); SendMessage was missed.
        if (!thread.IsParticipant(me))
        {
            throw new ForbiddenException("Not a participant in this thread.");
        }
        // Sender display name — pulled from whichever side me is.
        var displayName = me == thread.GuestUserId ? thread.GuestDisplayName : thread.OwnerDisplayName;

        var message = Message.Send(thread, me, displayName, cmd.Body);
        db.Messages.Add(message);
        thread.RecordSent(
            message.Body.Length > 80 ? string.Concat(message.Body.AsSpan(0, 77), "…") : message.Body,
            message.SentAt);
        await db.SaveChangesAsync(cancellationToken);

        return new MessageDto(message.Id, message.ThreadId, message.SenderUserId, message.SenderDisplayName,
            message.Body, Array.Empty<MessageAttachmentDto>(),
            message.SentAt, message.ReadAt);
    }
}

internal sealed class MarkReadHandler(MessagingDbContext db, ICurrentUser currentUser)
    : IRequestHandler<MarkReadCommand>
{
    public async Task Handle(MarkReadCommand cmd, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous users cannot read messages.");
        }
        var me = currentUser.UserId.Value;

        var thread = await db.Threads.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == cmd.ThreadId, cancellationToken)
            ?? throw new NotFoundException("MessageThread", cmd.ThreadId);
        if (!thread.IsParticipant(me))
        {
            throw new ForbiddenException("Not a participant in this thread.");
        }

        var upTo = await db.Messages.AsNoTracking()
            .Where(m => m.Id == cmd.UpToMessageId && m.ThreadId == cmd.ThreadId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Message", cmd.UpToMessageId);

        // Mark every unread message addressed to me at or before that point.
        var unread = await db.Messages
            .Where(m => m.ThreadId == cmd.ThreadId
                     && m.RecipientUserId == me
                     && m.ReadAt == null
                     && m.SentAt <= upTo.SentAt)
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var m in unread)
        {
            m.MarkRead(me, now);
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
