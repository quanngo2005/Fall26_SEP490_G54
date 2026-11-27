namespace G54.BLL.Dtos.Queue;

public sealed record LobbyStaff(Guid Id, string Name, string Position);
public sealed record LobbyDesk(int Counter, string Position, Guid? Employee, bool Locked, int Revision, string[] Services, string State, string? Number, string? Service, DateTimeOffset? CalledAt);
public sealed record LobbyWaiting(string Number, string Service, DateTimeOffset IssuedAt);
public sealed record LobbyDaily(Guid Employee, int Count, double WaitSeconds, double ServiceSeconds, int Ratings, double? Score);
public sealed record LobbyState(DateTimeOffset ServerTime, LobbyDesk[] Desks, LobbyWaiting[] Waiting, LobbyDaily[] Daily, int IssuedToday, LobbyStaff[]? Staff = null, int WaitLimitMinutes = 15, int ServiceLimitMinutes = 20);
public sealed record LobbyUpdate(Guid? Employee, string[] Services, bool Locked, int Revision);
