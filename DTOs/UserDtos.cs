using TimeSheet.Models;

namespace TimeSheet.DTOs
{
    public class UserDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string LevelName { get; set; } = null!;
        public int? LevelNo { get; set; } = 0;
        public int? WorkFlowId { get; set; } = 0;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int BackupUserId { get; set; }
        public string BackupUserName { get; set; }
    }

    public class CreateUserDto
    {
        public string Username { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!; // plain password
        public string Role { get; set; } = null!;
        public int WorkFlowId { get; set; } = 0;
        public int BackupUserId { get; set; } = 0;
    }

    public class UpdateUserDto
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;
        public bool IsActive { get; set; }
        public bool FirstLogin { get; set; }
        public int WorkFlowId { get; set; } = 0;
        public int BackupUserId { get; set; } = 0;


    }

    public static class UserMappingExtensions
    {
        public static UserDto ToDto(this User user)
        {
            return new UserDto
            {
                UserId = user.UserId,
                Username = user.Username,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                BackupUserId = user.Backups != null && user.Backups.Count > 0 ? user.Backups.First().BackupUserId : 0
            };
        }

        public static User ToEntity(this CreateUserDto dto, string passwordHash)
        {
            return new User
            {
                Username = dto.Username,
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = passwordHash,
                Role = dto.Role,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
        }

        public class UpdatePasswordDto
        {
            public string CurrentPassword { get; set; } = null!;
            public string NewPassword { get; set; } = null!;
        }

        public class ResetPasswordDto
        {
            public string NewPassword { get; set; } = null!;
        }

        public class LoginDto
        {
            public string Username { get; set; } = null!;
            public string Password { get; set; } = null!;
        }

        public class LoginResponseDto
        {
            public string Token { get; set; } = null!;
            public string Username { get; set; } = null!;
            public string Role { get; set; } = null!;
            public string FullName { get; set; } = null!;
            public string Email { get; set; } = null!;
            public int UserId { get; set; }
            public bool FirstLogin { get; set; }

        }

        public class LevelDetailsDto
        {
            public int UserId { get; set; }
            public string Username { get; set; }
            public string FullName { get; set; }
            public string Email { get; set; }
            public string Role { get; set; }
            public bool IsActive { get; set; }

            public int? WorkFlowId { get; set; }
            public int? LevelNo { get; set; }
            public string LevelName { get; set; }

            // Optional - For Level 1 workflow (PM info)
            public string? ProjectId { get; set; }
            public string ProjectManagerId { get; set; }
            public string ProjectName { get; set; }
            public string ProjectManagerName { get; set; }
            public string ProjectManagerEmail { get; set; }
        }


    }


}
