using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SocietyLedger.Application.DTOs;
using SocietyLedger.Application.DTOs.Auth;
using SocietyLedger.Application.DTOs.User;
using SocietyLedger.Application.Interfaces.Repositories;
using SocietyLedger.Application.Interfaces.Services;
using SocietyLedger.Domain.Constants;
using SocietyLedger.Domain.Entities;
using SocietyLedger.Domain.Exceptions;
using SocietyLedger.Infrastructure.Persistence.Contexts;

namespace SocietyLedger.Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepo;
        private readonly IRoleRepository _roleRepo;
        private readonly PasswordHasher _hasher;
        private readonly ILogger<UserService> _logger;
        private readonly AppDbContext _db;

        public UserService(
            IUserRepository userRepo,
            IRoleRepository roleRepo,
            PasswordHasher hasher,
            ILogger<UserService> logger,
            AppDbContext db)
        {
            _userRepo = userRepo;
            _roleRepo = roleRepo;
            _hasher = hasher;
            _logger = logger;
            _db = db;
        }

        /// <summary>
        /// Returns user details by ID.
        /// </summary>
        public async Task<UserResponseDto?> GetUserByIdAsync(long userId)
        {
            var user = await GetActiveUserAsync(userId);
            if (user == null)
                return null;
            var role = await _roleRepo.GetByIdAsync(user.RoleId);
            return new UserResponseDto(
                PublicId: user.PublicId,
                Name: user.Name,
                Email: user.Email,
                Mobile: user.Mobile,
                RoleDisplayName: role?.DisplayName ?? string.Empty,
                IsActive: user.IsActive,
                ForcePasswordChange: user.ForcePasswordChange,
                LastLogin: user.LastLogin,
                CreatedAt: user.CreatedAt,
                UpdatedAt: user.UpdatedAt,
                SocietyPublicId: user.SocietyPublicId,
                Role: role?.Code,
                Roles: role != null
                    ? new[] { new RoleDto { Id = role.Id, Code = role.Code, DisplayName = role.DisplayName } }
                    : null
            );
        }

        /// <summary>
        /// Updates user profile (self-service, only mobile allowed).
        /// </summary>
        public async Task<ProfileResponse> UpdateProfileAsync(long userId, UpdateProfileRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !user.IsActive)
                throw new NotFoundException("User", userId.ToString());

            // Partial update: only apply fields that were provided
            if (request.Mobile != null)
            {
                // Check uniqueness within the same society
                if (user.Mobile != request.Mobile)
                {
                    var conflict = await _userRepo.GetByMobileAndSocietyAsync(request.Mobile, user.SocietyId);
                    if (conflict != null)
                        throw new DuplicateException("user", "mobile number");
                }
                user.Mobile = request.Mobile;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _userRepo.UpdateAsync(user);
            await _userRepo.SaveChangesAsync();

            _logger.LogInformation("User {UserId} updated own profile", userId);

            var role = await _roleRepo.GetByIdAsync(user.RoleId);
            return new ProfileResponse
            {
                PublicId      = user.PublicId,
                Name          = user.Name,
                Email         = user.Email,
                Mobile        = user.Mobile,
                Role          = role?.Code,
                RoleDisplayName = role?.DisplayName
            };
        }

        // Helper: Get user by ID and check active
        private async Task<User?> GetActiveUserAsync(long userId)
        {
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || !user.IsActive)
                return null;
            return user;
        }

        // Helper: Check admin
        private static bool IsAdmin(User user) => user.Role?.Code == RoleCodes.SocietyAdmin;

        // Helper: Validate admin user and return or throw
        private async Task<User> ValidateAdminUserAsync(long authUserId, string operation)
        {
            var authUser = await GetActiveUserAsync(authUserId);
            if (authUser == null)
            {
                _logger.LogWarning("Unauthorized {Operation} request - invalid user ID {UserId}", operation, authUserId);
                throw new AuthenticationException("Invalid or missing authentication token");
            }
            if (!IsAdmin(authUser))
            {
                _logger.LogWarning("User {UserId} attempted {Operation} without admin role", authUserId, operation);
                throw new AuthorizationException("Insufficient permissions");
            }
            return authUser;
        }

        /// <summary>
        /// Returns all users for admin in the same society.
        /// </summary>
        public async Task<List<UserResponseDto>> GetUsersForAdminAsync(long authUserId)
        {
            var authUser = await ValidateAdminUserAsync(authUserId, "user list");
            var users = await GetBySocietyAsync(authUser.SocietyId);
            return users.ToList();
        }

        /// <summary>
        /// Returns user by public ID for admin.
        /// </summary>
        public async Task<UserResponseDto> GetUserByPublicIdForAdminAsync(Guid publicId, long authUserId)
        {
            var authUser = await ValidateAdminUserAsync(authUserId, "user get");
            var user = await GetByPublicIdAsync(publicId, authUser.SocietyId);
            if (user == null)
            {
                _logger.LogWarning("User {PublicId} not found in society {SocietyId}", publicId, authUser.SocietyId);
                throw new NotFoundException("User", publicId.ToString());
            }
            return user;
        }

        /// <summary>
        /// Creates a user for admin.
        /// </summary>
        public async Task<CreateUserResponseDto> CreateUserForAdminAsync(CreateUserDto dto, long authUserId)
        {
            var authUser = await ValidateAdminUserAsync(authUserId, "user create");
            var created = await CreateAsync(dto, authUser.SocietyId);
            _logger.LogInformation("User {Email} created in society {SocietyId} by {UserId}", dto.Email, authUser.SocietyId, authUserId);
            return created;
        }

        /// <summary>
        /// Updates a user for admin.
        /// </summary>
        public async Task<UserResponseDto> UpdateUserForAdminAsync(UpdateUserDto dto, long authUserId)
        {
            var authUser = await ValidateAdminUserAsync(authUserId, "user update");
            var updated = await UpdateAsync(dto, authUser.SocietyId);
            _logger.LogInformation("User {PublicId} updated in society {SocietyId} by {UserId}", dto.PublicId, authUser.SocietyId, authUserId);
            return updated;
        }

        /// <summary>
        /// Deletes a user for admin.
        /// </summary>
        public async Task<bool> DeleteUserForAdminAsync(Guid publicId, long authUserId)
        {
            var authUser = await ValidateAdminUserAsync(authUserId, "user delete");
            await DeleteByPublicIdAsync(publicId, authUser.SocietyId);
            _logger.LogInformation("User {PublicId} soft deleted in society {SocietyId} by {UserId}", publicId, authUser.SocietyId, authUserId);
            return true;
        }

        /// <summary>
        /// Returns all users for a society.
        /// </summary>
        public async Task<IEnumerable<UserResponseDto>> GetBySocietyAsync(long societyId)
        {
            var users = await _userRepo.GetBySocietyIdAsync(societyId);
            var roleIds = users.Select(u => u.RoleId).Distinct().ToList();
            
            // Fetch all roles in a single query to avoid N+1 problem
            var roles = await _roleRepo.GetByIdsAsync(roleIds);
            var roleDict = roles.ToDictionary(r => r.Id, r => r);
            
            return users.Select(user => {
                roleDict.TryGetValue(user.RoleId, out var role);
                return new UserResponseDto(
                    PublicId: user.PublicId,
                    Name: user.Name,
                    Email: user.Email,
                    Mobile: user.Mobile,
                    RoleDisplayName: role?.DisplayName ?? string.Empty,
                    IsActive: user.IsActive,
                    ForcePasswordChange: user.ForcePasswordChange,
                    LastLogin: user.LastLogin,
                    CreatedAt: user.CreatedAt,
                    UpdatedAt: user.UpdatedAt,
                    SocietyPublicId: user.SocietyPublicId,
                    Role: role?.Code,
                    Roles: role != null
                        ? new[] { new RoleDto { Id = role.Id, Code = role.Code, DisplayName = role.DisplayName } }
                        : null
                );
            });
        }

        /// <summary>
        /// Returns user by public ID for a society.
        /// </summary>
        public async Task<UserResponseDto> GetByPublicIdAsync(Guid publicId, long societyId)
        {
            var user = await _userRepo.GetByPublicIdAsync(publicId, societyId);
            if (user == null)
                throw new NotFoundException("User", publicId.ToString());
            var role = await _roleRepo.GetByIdAsync(user.RoleId);
            return new UserResponseDto(
                PublicId: user.PublicId,
                Name: user.Name,
                Email: user.Email,
                Mobile: user.Mobile,
                RoleDisplayName: role?.DisplayName ?? string.Empty,
                IsActive: user.IsActive,
                ForcePasswordChange: user.ForcePasswordChange,
                LastLogin: user.LastLogin,
                CreatedAt: user.CreatedAt,
                UpdatedAt: user.UpdatedAt,
                SocietyPublicId: user.SocietyPublicId,
                Role: role?.Code,
                Roles: role != null
                    ? new[] { new RoleDto { Id = role.Id, Code = role.Code, DisplayName = role.DisplayName } }
                    : null
            );
        }

        /// <summary>
        /// Creates a user for a society.
        /// </summary>
        public async Task<CreateUserResponseDto> CreateAsync(CreateUserDto dto, long societyId)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new ValidationException("Password is required.");


            // Check for duplicate email in the same society
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var existingEmail = await _userRepo.GetByEmailAndSocietyAsync(dto.Email, societyId);
                if (existingEmail != null)
                    throw new DuplicateException("user", "email");
            }

            // Check for duplicate username in the same society
            if (!string.IsNullOrWhiteSpace(dto.Username))
            {
                var existingUsername = await _userRepo.GetByUsernameAndSocietyAsync(dto.Username, societyId);
                if (existingUsername != null)
                    throw new DuplicateException("user", "username");
            }

            if (!string.IsNullOrEmpty(dto.Mobile))
            {
                var existingMobile = await _userRepo.GetByMobileAndSocietyAsync(dto.Mobile, societyId);
                if (existingMobile != null)
                    throw new DuplicateException("user", "mobile number");
            }

            var role = await _roleRepo.GetByCodeAsync(dto.RoleCode);
            if (role == null)
                throw new InvalidOperationException($"Role with code '{dto.RoleCode}' not found.");

            // Hash the admin-provided password
            var passwordHash = _hasher.Hash(dto.Password);
            var user = new User
            {
                PublicId = Guid.NewGuid(),
                Name = dto.Name,
                Email = dto.Email,
                Username = dto.Username,
                Mobile = dto.Mobile,
                RoleId = role.Id,
                SocietyId = societyId,
                PasswordHash = passwordHash,
                IsActive = true,
                ForcePasswordChange = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _userRepo.AddAsync(user);
            await _userRepo.SaveChangesAsync();

            _logger.LogInformation("User {Email} created in society {SocietyId}", user.Email, societyId);

            return new CreateUserResponseDto(
                PublicId: user.PublicId,
                Email: user.Email ?? string.Empty,
                Username: user.Username ?? string.Empty,
                Message: "User created successfully"
            );
        }

        /// <summary>
        /// Updates user details for a society.
        /// </summary>
        public async Task<UserResponseDto> UpdateAsync(UpdateUserDto dto, long societyId)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));

            var user = await _userRepo.GetByPublicIdAsync(dto.PublicId, societyId);

            if (user == null)
                throw new NotFoundException("User", dto.PublicId.ToString());

            // Check if new email conflicts with another user in the same society
            if (user.Email != dto.Email)
            {
                var conflictingUser = await _userRepo.GetByEmailAndSocietyAsync(dto.Email, societyId);
                if (conflictingUser != null)
                    throw new DuplicateException("user", "email");
            }

            // Check if new mobile conflicts with another user in the same society
            if (user.Mobile != dto.Mobile && !string.IsNullOrEmpty(dto.Mobile))
            {
                var conflictingUser = await _userRepo.GetByMobileAndSocietyAsync(dto.Mobile, societyId);
                if (conflictingUser != null)
                    throw new DuplicateException("user", "mobile number");
            }

            // Verify role exists and is valid
            var role = await _roleRepo.GetByCodeAsync(dto.RoleCode);
            if (role == null)
                throw new ValidationException($"Invalid role code '{dto.RoleCode}'. Must be 'society_admin' or 'viewer'.");

            // Update user
            user.Name = dto.Name;
            user.Email = dto.Email;
            user.Mobile = dto.Mobile;
            user.RoleId = role.Id;
            user.UpdatedAt = DateTime.UtcNow;

            await _userRepo.UpdateAsync(user);
            await _userRepo.SaveChangesAsync();

            _logger.LogInformation("User {PublicId} updated in society {SocietyId}", dto.PublicId, societyId);

            return new UserResponseDto(
                PublicId: user.PublicId,
                Name: user.Name,
                Email: user.Email,
                Mobile: user.Mobile,
                RoleDisplayName: role.DisplayName,
                IsActive: user.IsActive,
                ForcePasswordChange: user.ForcePasswordChange,
                LastLogin: user.LastLogin,
                CreatedAt: user.CreatedAt,
                UpdatedAt: user.UpdatedAt,
                SocietyPublicId: user.SocietyPublicId,
                Role: role.Code,
                Roles: new[] { new RoleDto { Id = role.Id, Code = role.Code, DisplayName = role.DisplayName } }
            );
        }

        /// <summary>
        /// Soft deletes a user (sets is_deleted = true).
        /// </summary>
        public async Task DeleteByPublicIdAsync(Guid publicId, long societyId)
        {
            var user = await _userRepo.GetByPublicIdAsync(publicId, societyId);

            if (user == null)
                throw new NotFoundException("User", publicId.ToString());

            // Block deletion if this is the last active admin in the society.
            var isAdmin = await _db.users
                .Include(u => u.role)
                .AnyAsync(u => u.public_id == publicId
                            && u.society_id == societyId
                            && !u.is_deleted
                            && u.role != null
                            && u.role.code == RoleCodes.SocietyAdmin);

            if (isAdmin)
            {
                var activeAdminCount = await _db.users
                    .Include(u => u.role)
                    .CountAsync(u => u.society_id == societyId
                                 && !u.is_deleted
                                 && u.is_active
                                 && u.role != null
                                 && u.role.code == RoleCodes.SocietyAdmin);

                if (activeAdminCount <= 1)
                    throw new ConflictException(
                        "Cannot delete the only admin user in the society. " +
                        "Assign another user as admin first.");
            }

            var deleted = await _userRepo.SoftDeleteByPublicIdAsync(publicId, societyId);
            if (deleted)
            {
                await _userRepo.SaveChangesAsync();
                _logger.LogInformation("User {PublicId} soft deleted in society {SocietyId}", publicId, societyId);
            }
        }

    }
}
