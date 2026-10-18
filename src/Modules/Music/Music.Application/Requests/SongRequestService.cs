using Common.Application.Customers;
using Common.Application.Displays;
using Common.Application.EventCatalog;
using Common.Application.Ticketing;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Microsoft.Extensions.Options;
using Music.Application.Abstractions;
using Music.Application.Catalog;
using Music.Domain.Common;
using Music.Domain.Requests;

namespace Music.Application.Requests;

/// <summary>
///     Song requests: ticket holders search the catalogue (or type a song) and ask the DJs for it; asking for a song
///     already on the list adds a vote. The DJ console opens and closes requests and works the queue.
/// </summary>
public sealed class SongRequestService(
    IRequestBoardRepository boards,
    ISongRequestRepository requests,
    IMusicUnitOfWork unitOfWork,
    IMusicCatalog catalog,
    IEventCatalog events,
    ICustomerDirectory customers,
    ITicketHolders ticketHolders,
    IOptions<MusicOptions> options,
    TimeProvider time,
    IEnumerable<IDisplayBoardSignal> screens)
{
    /// <summary>Screens showing the DJ booth refresh (best effort; they also poll).</summary>
    private async Task NotifyScreensAsync(Guid eventId)
    {
        foreach (var screen in screens)
        {
            try
            {
                await screen.MusicChangedAsync(eventId, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A screen push must never fail the request.
            }
        }
    }

    /// <summary>Songs played from the list shown under "Recently played".</summary>
    public const int RecentlyPlayedShown = 10;

    private MusicOptions Settings => options.Value;

    // ---- Public / customer --------------------------------------------------------------------------------------

    public async Task<Result<RequestBoardPublicView>> GetPublicAsync(string slug, CancellationToken cancellationToken)
    {
        var snapshot = await FindPublishedAsync(slug, cancellationToken);
        if (snapshot is null)
        {
            return MusicErrors.EventNotFound;
        }

        var board = await boards.FindForEventAsync(snapshot.EventId, cancellationToken);
        var list = await requests.ListForEventAsync(snapshot.EventId, cancellationToken);

        return new RequestBoardPublicView(
            TakingRequests(board, snapshot),
            board?.MaxPerPerson ?? Settings.DefaultMaxPerPerson,
            board?.Note,
            [.. Queue(list).Select(PublicSongRequestView.From)],
            [.. list.Where(x => x.Status == SongRequestStatus.Played)
                .OrderByDescending(x => x.HandledAt)
                .Take(RecentlyPlayedShown)
                .Select(PublicSongRequestView.From)]);
    }

    public async Task<Result<MySongRequestsView>> GetMineAsync(string slug, ApplicationUserId userId, CancellationToken cancellationToken)
    {
        var snapshot = await FindPublishedAsync(slug, cancellationToken);
        if (snapshot is null)
        {
            return MusicErrors.EventNotFound;
        }

        var board = await boards.FindForEventAsync(snapshot.EventId, cancellationToken);
        var max = board?.MaxPerPerson ?? Settings.DefaultMaxPerPerson;
        var used = await requests.CountVotesAsync(snapshot.EventId, userId.Value, cancellationToken);
        var mine = await requests.ListVotedRequestIdsAsync(snapshot.EventId, userId.Value, cancellationToken);
        var eligibility = await EligibilityAsync(snapshot, board, userId, used, cancellationToken);

        return new MySongRequestsView(eligibility.Eligibility, used, max, mine);
    }

    public async Task<Result<IReadOnlyList<CatalogTrackView>>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        var text = string.Join(' ', (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (text.Length < Settings.MinQueryLength)
        {
            return Result<IReadOnlyList<CatalogTrackView>>.Success([]);
        }

        if (text.Length > Track.TypedTitleMaxLength + Track.TypedArtistMaxLength)
        {
            return MusicErrors.Invalid("SearchInvalid", ["Query: Keep it shorter."]);
        }

        var found = await catalog.SearchAsync(text, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        return Result<IReadOnlyList<CatalogTrackView>>.Success([.. found.Value!.Select(CatalogTrackView.From)]);
    }

    public async Task<Result<SongRequestResultView>> RequestAsync(
        string slug,
        ApplicationUserId userId,
        CreateSongRequestRequest input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var snapshot = await FindPublishedAsync(slug, cancellationToken);
        if (snapshot is null)
        {
            return MusicErrors.EventNotFound;
        }

        var ensured = await EnsureBoardAsync(snapshot.EventId, cancellationToken);
        if (ensured.IsFailure)
        {
            return ensured.Error!;
        }

        var board = ensured.Value!;
        var used = await requests.CountVotesAsync(snapshot.EventId, userId.Value, cancellationToken);
        var eligibility = await EligibilityAsync(snapshot, board, userId, used, cancellationToken);

        var refused = Refusal(eligibility.Eligibility, board);
        if (refused is not null)
        {
            return refused;
        }

        var track = await ResolveTrackAsync(input, cancellationToken);
        if (track.IsFailure)
        {
            return track.Error!;
        }

        var name = eligibility.Profile!.DisplayName;

        // Two fans asking for a new song at the same moment: the second insert loses on the unique index and is
        // retried once as a vote on the request the first one created.
        var outcome = await AddOrSecondAsync(snapshot.EventId, track.Value!, userId, name, cancellationToken);
        if (outcome.IsFailure && outcome.Error!.Key is MusicUnitOfWorkErrors.DuplicateOpenRequestKey or MusicErrors.ConcurrentUpdateKey)
        {
            unitOfWork.Reset();
            outcome = await AddOrSecondAsync(snapshot.EventId, track.Value!, userId, name, cancellationToken);
        }

        if (outcome.IsFailure)
        {
            return outcome.Error!;
        }

        var (request, seconded) = outcome.Value;
        await NotifyScreensAsync(snapshot.EventId);

        return new SongRequestResultView(PublicSongRequestView.From(request), seconded, used + 1, board.MaxPerPerson);
    }

    /// <summary>"Me too" on a song already on the list. Counts against the fan's limit like a new request.</summary>
    public async Task<Result<SongRequestResultView>> VoteAsync(
        string slug,
        ApplicationUserId userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var snapshot = await FindPublishedAsync(slug, cancellationToken);
        if (snapshot is null)
        {
            return MusicErrors.EventNotFound;
        }

        var ensured = await EnsureBoardAsync(snapshot.EventId, cancellationToken);
        if (ensured.IsFailure)
        {
            return ensured.Error!;
        }

        var board = ensured.Value!;
        var used = await requests.CountVotesAsync(snapshot.EventId, userId.Value, cancellationToken);
        var eligibility = await EligibilityAsync(snapshot, board, userId, used, cancellationToken);
        var refused = Refusal(eligibility.Eligibility, board);
        if (refused is not null)
        {
            return refused;
        }

        var request = await requests.FindAsync(new SongRequestId(requestId), cancellationToken);
        if (request is null || request.EventId != snapshot.EventId)
        {
            return MusicErrors.RequestNotFound;
        }

        var now = time.GetUtcNow();
        var second = request.Second(new SongRequestVoteId(Guid.CreateVersion7(now)), userId.Value, eligibility.Profile!.DisplayName, now);
        if (second.IsFailure)
        {
            return second.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await NotifyScreensAsync(snapshot.EventId);
        return new SongRequestResultView(PublicSongRequestView.From(request), true, used + 1, board.MaxPerPerson);
    }

    // ---- DJ console --------------------------------------------------------------------------------------------

    public async Task<Result<DjConsoleView>> GetConsoleAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var board = await EnsureBoardAsync(eventId, cancellationToken);
        if (board.IsFailure)
        {
            return board.Error!;
        }

        var list = await requests.ListForEventAsync(eventId, cancellationToken);

        return new DjConsoleView(
            RequestBoardView.From(board.Value!),
            [.. Queue(list)
                .Concat(list.Where(x => !x.IsOpen).OrderByDescending(x => x.HandledAt))
                .Select(DjSongRequestView.From)]);
    }

    public async Task<Result<RequestBoardView>> ConfigureBoardAsync(Guid eventId, ConfigureBoardRequest input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var board = await EnsureBoardAsync(eventId, cancellationToken);
        if (board.IsFailure)
        {
            return board.Error!;
        }

        var configured = board.Value!.Configure(input.Open, input.MaxPerPerson, input.Note, time.GetUtcNow());
        if (configured.IsFailure)
        {
            return configured.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await NotifyScreensAsync(eventId);
        return RequestBoardView.From(board.Value);
    }

    public async Task<Result<DjSongRequestView>> MoveAsync(
        Guid requestId,
        MoveSongRequestRequest input,
        ApplicationUserId staffUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var request = await requests.FindAsync(new SongRequestId(requestId), cancellationToken);
        if (request is null)
        {
            return MusicErrors.RequestNotFound;
        }

        var moved = request.MoveTo(input.Status, staffUserId.Value, time.GetUtcNow());
        if (moved.IsFailure)
        {
            return moved.Error!;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await NotifyScreensAsync(request.EventId);
        return DjSongRequestView.From(request);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------------

    /// <summary>Up next first, then the most asked-for, then the oldest.</summary>
    private static IEnumerable<SongRequest> Queue(IEnumerable<SongRequest> list) =>
        list.Where(x => x.IsOpen)
            .OrderBy(x => x.Status == SongRequestStatus.UpNext ? 0 : 1)
            .ThenByDescending(x => x.VoteCount)
            .ThenBy(x => x.RequestedAt);

    private async Task<EventSnapshot?> FindPublishedAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var snapshot = await events.FindBySlugAsync(slug, cancellationToken);

        return snapshot is { Status: EventLifecycle.Published } ? snapshot : null;
    }

    /// <summary>
    ///     Requests are taken from publication until the event ends: the board is open (or, before the DJ console
    ///     ever touched it, <see cref="MusicOptions.OpenByDefault" />) and the last date has not ended.
    /// </summary>
    private bool TakingRequests(RequestBoard? board, EventSnapshot snapshot)
    {
        var open = board?.IsOpen ?? Settings.OpenByDefault;
        return open &&
               (snapshot.LastEndsAt is not { } endsAt || time.GetUtcNow() < endsAt.AddMinutes(Settings.CloseAfterEventMinutes));
    }

    private async Task<(RequestEligibility Eligibility, CustomerProfile? Profile)> EligibilityAsync(
        EventSnapshot snapshot,
        RequestBoard? board,
        ApplicationUserId userId,
        int used,
        CancellationToken cancellationToken)
    {
        var eventId = snapshot.EventId;
        var max = board?.MaxPerPerson ?? Settings.DefaultMaxPerPerson;

        if (!TakingRequests(board, snapshot))
        {
            return (RequestEligibility.RequestsClosed, null);
        }

        var profile = await customers.FindAsync(userId, cancellationToken);
        if (profile?.Phone is not { Length: > 0 } phone)
        {
            return (RequestEligibility.PhoneRequired, profile);
        }

        if (!await ticketHolders.HoldsTicketAsync(phone, eventId, cancellationToken))
        {
            return (RequestEligibility.TicketRequired, profile);
        }

        return used >= max
            ? (RequestEligibility.LimitReached, profile)
            : (RequestEligibility.CanRequest, profile);
    }

    private static Error? Refusal(RequestEligibility eligibility, RequestBoard? board) =>
        eligibility switch
        {
            RequestEligibility.CanRequest => null,
            RequestEligibility.PhoneRequired => MusicErrors.PhoneRequired,
            RequestEligibility.TicketRequired => MusicErrors.TicketRequired,
            RequestEligibility.LimitReached => MusicErrors.LimitReached(board?.MaxPerPerson ?? 0),
            _ => MusicErrors.RequestsClosed
        };

    private async Task<Result<Track>> ResolveTrackAsync(CreateSongRequestRequest input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.TrackId))
        {
            return Track.Typed(input.Title, input.Artist);
        }

        if (!Guid.TryParse(input.TrackId, out _))
        {
            return MusicErrors.TrackNotFound;
        }

        // The details come from the catalogue, never from the request body.
        var found = await catalog.GetAsync(input.TrackId, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        return found.Value is { } t
            ? Track.FromCatalog(t.Id, t.Title, t.Artist, t.Release, t.ReleaseId, t.Year, t.DurationSeconds)
            : MusicErrors.TrackNotFound;
    }

    private async Task<Result<(SongRequest Request, bool Seconded)>> AddOrSecondAsync(
        Guid eventId,
        Track track,
        ApplicationUserId userId,
        string name,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var existing = await requests.FindOpenByKeyAsync(eventId, track.Key, cancellationToken);
        SongRequest request;
        bool seconded;

        if (existing is not null)
        {
            var second = existing.Second(new SongRequestVoteId(Guid.CreateVersion7(now)), userId.Value, name, now);
            if (second.IsFailure)
            {
                return second.Error!;
            }

            request = existing;
            seconded = true;
        }
        else
        {
            request = SongRequest.Create(
                new SongRequestId(Guid.CreateVersion7(now)),
                eventId,
                track,
                new SongRequestVoteId(Guid.CreateVersion7(now)),
                userId.Value,
                name,
                now);
            requests.Add(request);
            seconded = false;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);

        return saved.IsFailure ? saved.Error! : (request, seconded);
    }

    /// <summary>
    ///     The event's board, created with the default limit the first time the console or a fan's request needs it:
    ///     open when <see cref="MusicOptions.OpenByDefault" />, so requests work before the night.
    /// </summary>
    private async Task<Result<RequestBoard>> EnsureBoardAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var board = await boards.FindForEventAsync(eventId, cancellationToken);
        if (board is not null)
        {
            return board;
        }

        var sale = eventId == Guid.Empty ? null : await events.GetSaleSnapshotAsync(eventId, cancellationToken);
        if (sale is null)
        {
            return MusicErrors.EventNotFound;
        }

        var now = time.GetUtcNow();
        board = RequestBoard.Create(new RequestBoardId(Guid.CreateVersion7(now)), eventId, Settings.DefaultMaxPerPerson);
        if (Settings.OpenByDefault)
        {
            board.Configure(true, board.MaxPerPerson, null, now);
        }

        boards.Add(board);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            // Two consoles opened the event at once: the other one created the board.
            unitOfWork.Reset();
            return await boards.FindForEventAsync(eventId, cancellationToken) is { } created ? created : saved.Error!;
        }

        return board;
    }
}
