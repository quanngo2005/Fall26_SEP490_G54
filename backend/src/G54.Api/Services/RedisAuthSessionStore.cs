using G54.BLL.Services;
using StackExchange.Redis;

namespace G54.Api.Services;

public sealed class RedisAuthSessionStore(IConnectionMultiplexer connectionMultiplexer) : IAuthSessionStore
{
    private const string SessionKeyPrefix = "auth:session:";
    private const string RevokeScript = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then
            return 0
        end
        return redis.call('DEL', KEYS[1])
        """;
    private const string RotateScript = """
        if redis.call('GET', KEYS[1]) ~= ARGV[1] then
            return 0
        end
        if redis.call('EXISTS', KEYS[2]) ~= 0 then
            return 0
        end
        redis.call('SET', KEYS[2], ARGV[2], 'PX', ARGV[3])
        redis.call('DEL', KEYS[1])
        return 1
        """;

    public async Task<bool> StoreAsync(
        Guid accountId,
        Guid refreshTokenId,
        string refreshTokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var ttl = expiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await connectionMultiplexer.GetDatabase()
            .StringSetAsync(SessionKey(accountId, refreshTokenId), refreshTokenHash, ttl, When.NotExists)
            .WaitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> RotateAsync(
        Guid accountId,
        Guid currentTokenId,
        string currentTokenHash,
        Guid nextTokenId,
        string nextTokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var ttlMilliseconds = (long)Math.Ceiling((expiresAt - DateTimeOffset.UtcNow).TotalMilliseconds);
        if (ttlMilliseconds <= 0)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await connectionMultiplexer.GetDatabase().ScriptEvaluateAsync(
            RotateScript,
            [
                SessionKey(accountId, currentTokenId),
                SessionKey(accountId, nextTokenId),
            ],
            [currentTokenHash, nextTokenHash, ttlMilliseconds])
            .WaitAsync(cancellationToken);
        return (int)result == 1;
    }

    public async Task<bool> RevokeAsync(
        Guid accountId,
        Guid refreshTokenId,
        string refreshTokenHash,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await connectionMultiplexer.GetDatabase().ScriptEvaluateAsync(
            RevokeScript,
            [SessionKey(accountId, refreshTokenId)],
            [refreshTokenHash])
            .WaitAsync(cancellationToken);
        return (int)result == 1;
    }

    public async Task<int> RevokeAllAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var pattern = $"{SessionKeyPrefix}{accountId}:*";
        var database = connectionMultiplexer.GetDatabase();
        var removed = 0;
        // SCAN every primary; KEYS would block Redis. An account holds a handful of sessions, so one DEL per key is fine.
        foreach (var endpoint in connectionMultiplexer.GetEndPoints())
        {
            var server = connectionMultiplexer.GetServer(endpoint);
            if (server.IsReplica)
            {
                continue;
            }

            await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
            {
                if (await database.KeyDeleteAsync(key))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    private static RedisKey SessionKey(Guid accountId, Guid tokenId) =>
        $"{SessionKeyPrefix}{accountId}:{tokenId}";
}
