using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TimeSheet.DTOs;
using TimeSheet.Models;
using static TimeSheet.DTOs.UserMappingExtensions;

namespace TimeSheet.Repository
{
    public interface IUserRepository
    {
        Task<IEnumerable<User>> GetAllAsync();
        Task<IEnumerable<UserDto>> GetAllUsersAsync();
        Task<User?> GetByIdAsync(int id);
        Task<User?> GetByUsernameAsync(string username);
        Task<User> AddUserAsync(CreateUserDto userDto);
        Task<User?> UpdateAsync(User user);
        Task<User?> UpdateUserAsync(UpdateUserDto user);
        Task<bool> DeleteAsync(int id);
        Task<bool> UpdatePasswordAsync(int userId, UpdatePasswordDto dto);
        Task<bool> ResetPasswordAsync(int userId, ResetPasswordDto dto);
        Task<User?> LoginAsync(LoginDto dto);
        Task AddUsersAsync(IEnumerable<User> users);
        Task ReplaceBackupsAsync(int userId, List<int> newBackupUserIds);
    }
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public UserRepository(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        public async Task<IEnumerable<User>> GetAllAsync()
        {
            return await _context.Users.ToListAsync();
        }
        public async Task<IEnumerable<UserDto>> GetAllUsersAsync()
        {
            //return await _context.Users.ToListAsync();
            //return await (from u in _context.Users
            //              join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
            //              from aa in approverGroup.DefaultIfEmpty()
            //              join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
            //              from aw in workflowGroup.DefaultIfEmpty()
            //              select new UserDto
            //              {
            //                  UserId = u.UserId,
            //                  Username = u.Username,
            //                  FullName = u.FullName,
            //                  Email = u.Email,
            //                  Role = u.Role,
            //                  IsActive = u.IsActive,
            //                  LevelNo = aw != null ? aw.LevelNo : (int?)null,
            //                  WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
            //                  LevelName = aw != null ? aw.ApproverRole : null,
            //                  BackupUserId = u.Backups.FirstOrDefault().BackupUserId
            //              }).ToListAsync();

            //return await (from u in _context.Users
            //              join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
            //              from aa in approverGroup.DefaultIfEmpty()
            //              join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
            //              from aw in workflowGroup.DefaultIfEmpty()
            //              select new UserDto
            //              {
            //                  UserId = u.UserId,
            //                  Username = u.Username,
            //                  FullName = u.FullName,
            //                  Email = u.Email,
            //                  Role = u.Role,
            //                  IsActive = u.IsActive,
            //                  LevelNo = aw != null ? aw.LevelNo : (int?)null,
            //                  WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
            //                  LevelName = aw != null ? aw.ApproverRole : null,

            //                  BackupUserId = _context.UserBackups
            //                      .Where(b => b.UserId == u.UserId)
            //                      .Select(b => (int?)b.BackupUserId)
            //                      .FirstOrDefault().GetValueOrDefault(0), // returns 0 if no backup user found
            //                  BackupUserName = _context.UserBackups
            //                      .Where(b => b.UserId == u.UserId)
            //                      .Select(b => b.BackupUser.Username)
            //                       // returns 0 if no backup user found
            //              }).ToListAsync();

            return await (from u in _context.Users
                          join aa in _context.ApprovalApprovers on u.UserId equals aa.UserId into approverGroup
                          from aa in approverGroup.DefaultIfEmpty()
                          join aw in _context.ApprovalWorkflows on aa.WorkflowId equals aw.WorkflowId into workflowGroup
                          from aw in workflowGroup.DefaultIfEmpty()
                          select new UserDto
                          {
                              UserId = u.UserId,
                              Username = u.Username,
                              FullName = u.FullName,
                              Email = u.Email,
                              Role = u.Role,
                              IsActive = u.IsActive,
                              LevelNo = aw != null ? aw.LevelNo : (int?)null,
                              WorkFlowId = aw != null ? aw.WorkflowId : (int?)null,
                              LevelName = aw != null ? aw.ApproverRole : null,

                              BackupUserId = _context.UserBackups
                                  .Where(b => b.UserId == u.UserId)
                                  .Select(b => (int?)b.BackupUserId)
                                  .FirstOrDefault()
                                  .GetValueOrDefault(0),

                              BackupUserName = _context.UserBackups
                                  .Where(b => b.UserId == u.UserId)
                                  .Select(b => b.BackupUser.Username + " (" +b.BackupUser.FullName + ")")
                                  .FirstOrDefault()
                          }).ToListAsync();

        }
        //public async Task<User?> GetByIdAsync(int id)
        //{
        //    return await _context.Users.FindAsync(id);
        //}

        public async Task<User?> GetByIdAsync(int id)
        {
            return await _context.Users
                .Include(u => u.Backups)
                    .ThenInclude(b => b.BackupUser) // load backup user details
                .FirstOrDefaultAsync(u => u.UserId == id);
        }

        public async Task<User?> GetByUsernameAsync(string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task<User> AddUserAsync(CreateUserDto userDto)
        {

            if (userDto == null)
                throw new ArgumentNullException(nameof(userDto));
            if (await _context.Users.AnyAsync(u => u.Username == userDto.Username))
                throw new ArgumentException("Username already exists.");
            User user = new User()
            {
                Username = userDto.Username,
                FullName = userDto.FullName,
                Email = userDto.Email,
                PasswordHash = userDto.Password,
                Role = userDto.Role,
                IsActive = false,
                FirstLogin = true
            };

            user.CreatedAt = DateTime.UtcNow;
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            //if (user.Role.ToLower() == "user")
            {
                using (var conn = new NpgsqlConnection(_context.Database.GetConnectionString()))
                {
                    await conn.OpenAsync();

                    string sql = @"
                    INSERT INTO public.approval_approver (workflow_id, user_id, is_active)
                    VALUES (@workflow_id, @user_id, @is_active);";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@workflow_id", userDto.WorkFlowId);
                        cmd.Parameters.AddWithValue("@user_id", user.UserId);
                        cmd.Parameters.AddWithValue("@is_active", true);

                        await cmd.ExecuteNonQueryAsync();
                    }
                }

            }
            ReplaceBackupsAsync(user.UserId, new List<int>() { userDto.BackupUserId }).Wait();
            return user;
        }

        public async Task<User?> UpdateAsync(User user)
        {
            var existing = await _context.Users.FindAsync(user.UserId);
            if (existing == null) return null;

            existing.Username = user.Username;
            existing.FullName = user.FullName;
            existing.Email = user.Email;
            existing.Role = user.Role;
            existing.IsActive = user.IsActive;
            existing.FirstLogin = user.FirstLogin;
            // Do not overwrite CreatedAt
            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<User?> UpdateUserAsync(UpdateUserDto user)
        {
            var existing = await _context.Users.FindAsync(user.UserId);
            if (existing == null) return null;

            existing.FullName = user.FullName;
            existing.Email = user.Email;
            existing.Role = user.Role;
            existing.IsActive = user.IsActive;
            existing.FirstLogin = user.FirstLogin;
            // Do not overwrite CreatedAt

            var existingWorkFlow = await _context.ApprovalApprovers.FirstOrDefaultAsync(p => p.UserId == user.UserId);
            if (existingWorkFlow == null)
            {
                _context.ApprovalApprovers.Add(new ApprovalApprover() { IsActive = true, UserId = user.UserId, WorkflowId = user.WorkFlowId });
            }
            else
            {
                if (user.WorkFlowId != 0)
                    existingWorkFlow.WorkflowId = user.WorkFlowId;
            }
            await _context.SaveChangesAsync();
            ReplaceBackupsAsync(user.UserId, new List<int>() { user.BackupUserId }).Wait();


            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return false;

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();



            //if (user.Role.ToLower() == "user")
            {
                using (var conn = new NpgsqlConnection(_context.Database.GetConnectionString()))
                {
                    await conn.OpenAsync();

                    string sql = @"
                    DELETE FROM public.approval_approver
                    WHERE user_id = @user_id;
                ";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@user_id", user.UserId);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

            }

            return true;
        }

        public async Task<bool> UpdatePasswordAsync(int userId, UpdatePasswordDto dto)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                throw new UnauthorizedAccessException("Current password is incorrect.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ResetPasswordAsync(int userId, ResetPasswordDto dto)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return false;

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();
            return true;
        }

        //public async Task<string?> LoginAsync(LoginDto dto)
        //{
        //    var user = await _context.Users
        //        .FirstOrDefaultAsync(u => u.Username == dto.Username && u.IsActive);

        //    if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        //        return null;

        //    return GenerateJwtToken(user);
        //}

        public async Task<User?> LoginAsync(LoginDto dto)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == dto.Username && u.IsActive);

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return null;

            return user;
        }
        public async Task AddUsersAsync(IEnumerable<User> users)
        {
            var usernames = users.Select(u => u.Username).ToList();
            var emails = users.Select(u => u.Email).ToList();

            // Get existing users (by username or email)
            var existingUsers = await _context.Users
                .Where(u => usernames.Contains(u.Username) || emails.Contains(u.Email))
                .Select(u => new { u.Username, u.Email })
                .ToListAsync();

            var existingUsernames = existingUsers.Select(u => u.Username).ToHashSet();
            var existingEmails = existingUsers.Select(u => u.Email).ToHashSet();

            var newUsers = users
                .Where(u => !existingUsernames.Contains(u.Username) && !existingEmails.Contains(u.Email))
                .ToList();

            if (newUsers.Any())
            {
                await _context.BulkInsertAsync(newUsers, options => options.SetOutputIdentity = true);
                //await _context.SaveChangesAsync();


                using (var conn = new NpgsqlConnection(_context.Database.GetConnectionString()))
                {
                    await conn.OpenAsync();

                    using (var writer = conn.BeginBinaryImport(
                        "COPY public.approval_approver (workflow_id, user_id, is_active) FROM STDIN (FORMAT BINARY)"))
                    {
                        foreach (var user in newUsers)
                        {
                            await writer.StartRowAsync();
                            await writer.WriteAsync(1, NpgsqlTypes.NpgsqlDbType.Integer); // workflow_id
                            await writer.WriteAsync(user.UserId, NpgsqlTypes.NpgsqlDbType.Integer);
                            await writer.WriteAsync(true, NpgsqlTypes.NpgsqlDbType.Boolean);
                        }

                        await writer.CompleteAsync();
                    }
                }

            }

            foreach (var user in users)
            {
                ReplaceBackupsAsync(user.UserId, user.Backups.Select(b => b.BackupUserId).ToList()).Wait();
            }
        }

        public async Task ReplaceBackupsAsync(int userId, List<int> newBackupUserIds)
        {
            // Get existing backups
            var existingBackups = await _context.UserBackups
                .Where(ub => ub.UserId == userId)
                .ToListAsync();

            // Remove existing backups
            _context.UserBackups.RemoveRange(existingBackups);

            // Add new backups
            var newBackups = newBackupUserIds
            .Where(bid => bid > 0)          // removes 0 and negatives
            .Distinct()                     // avoids duplicate entries
            .Select(bid => new UserBackup
            {
                UserId = userId,
                BackupUserId = bid
            })
            .ToList();
            //var newBackups = newBackupUserIds.Select(bid => new UserBackup
            //{
            //    UserId = userId,
            //    BackupUserId = bid
            //}).ToList();

            _context.UserBackups.AddRange(newBackups);

            // Save changes
            await _context.SaveChangesAsync();
        }
        private string GenerateJwtToken(User user)
        {
            var claims = new[]
            {
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim("userId", user.UserId.ToString()),
            new Claim(ClaimTypes.Role, user.Role)
        };

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(2),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }


}
