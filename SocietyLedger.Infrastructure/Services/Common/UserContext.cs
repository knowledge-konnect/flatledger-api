using Microsoft.Extensions.Caching.Memory;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;

namespace SocietyLedger.Infrastructure.Services.Common
{
    /// <summary>
    /// Provides user context for multi-tenant operations.
    /// Fix #14: results cached for 30 seconds to avoid a DB round-trip on every service method call
    /// within the same request. Only positive results are cached — a deleted/deactivated user
    /// will be re-checked from DB after the TTL expires.
    /// </summary>
    public class UserContext : IUserContext
    {
        private readonly IUserRepository _userRepository;
        private readonly IMemoryCache _cache;

        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        public UserContext(IUserRepository userRepository, IMemoryCache cache)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<(User User, long SocietyId)> GetUserContextAsync(long userId)
        {
            var cacheKey = $"userctx_{userId}";

            if (_cache.TryGetValue(cacheKey, out (User User, long SocietyId) cached))
                return cached;

            var user = await _userRepository.GetByIdAsync(userId)
                ?? throw new NotFoundException("User", userId.ToString());

            var result = (user, user.SocietyId);
            _cache.Set(cacheKey, result, CacheTtl);
            return result;
        }

        public async Task<long> GetSocietyIdAsync(long userId)
        {
            var (_, societyId) = await GetUserContextAsync(userId);
            return societyId;
        }
    }
}
