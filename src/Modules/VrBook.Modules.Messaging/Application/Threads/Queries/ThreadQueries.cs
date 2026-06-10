using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Messaging.Infrastructure.Persistence;

namespace VrBook.Modules.Messaging.Application.Threads.Queries;

public sealed record ListMyThreadsQuery : IRequest<IReadOnlyList<ThreadDto>>;

public sealed record GetThreadQuery(Guid ThreadId) : IRequest<ThreadDto>;

public sealed record ListMessagesQuery(Guid ThreadId) : IRequest<IReadOnlyList<MessageDto>>;

internal sealed class ListMyThreadsHandler(MessagingDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListMyThreadsQuery, IReadOnlyList<ThreadDto>>
{
    public async Task<IReadOnlyList<ThreadDto>> Handle(ListMyThreadsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous users have no message threads.");
        }
        var me = currentUser.UserId.Value;

        var threads = await db.Threads
            .AsNoTracking()
            .Where(t => t.GuestUserId == me || t.OwnerUserId == me)
            .OrderByDescending(t => t.LastMessageAt ?? t.CreatedAt)
            .ToListAsync(cancellationToken);

        if (threads.Count == 0)
        {
            return Array.Empty<ThreadDto>();
        }

        var threadIds = threads.Select(t => t.Id).ToArray();
        var unreadCounts = await db.Messages
            .AsNoTracking()
            .Where(m => threadIds.Contains(m.ThreadId) && m.RecipientUserId == me && m.ReadAt == null)
            .GroupBy(m => m.ThreadId)
            .Select(g => new { ThreadId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var unreadByThread = unreadCounts.ToDictionary(x => x.ThreadId, x => x.Count);

        return threads.Select(t => new ThreadDto(
            t.Id,
            t.BookingId,
            t.BookingReference,
            t.GuestUserId,
            t.GuestDisplayName,
            t.OwnerUserId,
            t.OwnerDisplayName,
            unreadByThread.TryGetValue(t.Id, out var c) ? c : 0,
            t.LastMessageAt,
            t.LastMessagePreview)).ToArray();
    }
}

internal sealed class GetThreadHandler(MessagingDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetThreadQuery, ThreadDto>
{
    public async Task<ThreadDto> Handle(GetThreadQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous users cannot view threads.");
        }
        var me = currentUser.UserId.Value;
        var t = await db.Threads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ThreadId, cancellationToken)
            ?? throw new NotFoundException("MessageThread", request.ThreadId);
        if (!t.IsParticipant(me))
        {
            throw new ForbiddenException("Not a participant in this thread.");
        }
        var unread = await db.Messages.AsNoTracking()
            .CountAsync(m => m.ThreadId == t.Id && m.RecipientUserId == me && m.ReadAt == null, cancellationToken);
        return new ThreadDto(
            t.Id, t.BookingId, t.BookingReference,
            t.GuestUserId, t.GuestDisplayName,
            t.OwnerUserId, t.OwnerDisplayName,
            unread, t.LastMessageAt, t.LastMessagePreview);
    }
}

internal sealed class ListMessagesHandler(MessagingDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListMessagesQuery, IReadOnlyList<MessageDto>>
{
    public async Task<IReadOnlyList<MessageDto>> Handle(ListMessagesQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous users cannot view messages.");
        }
        var me = currentUser.UserId.Value;
        var t = await db.Threads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ThreadId, cancellationToken)
            ?? throw new NotFoundException("MessageThread", request.ThreadId);
        if (!t.IsParticipant(me))
        {
            throw new ForbiddenException("Not a participant in this thread.");
        }

        var msgs = await db.Messages.AsNoTracking()
            .Where(m => m.ThreadId == request.ThreadId)
            .OrderBy(m => m.SentAt)
            .ToListAsync(cancellationToken);

        return msgs.Select(m => new MessageDto(
            m.Id, m.ThreadId, m.SenderUserId, m.SenderDisplayName,
            m.Body, Array.Empty<MessageAttachmentDto>(),
            m.SentAt, m.ReadAt)).ToArray();
    }
}
