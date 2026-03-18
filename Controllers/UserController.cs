using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.Formula.Functions;
using Org.BouncyCastle.Crypto.Generators;
using TimeSheet.DTOs;
using TimeSheet.Models;
using TimeSheet.Repository;
using static TimeSheet.DTOs.UserMappingExtensions;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserRepository _repository;

    public UserController(IUserRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _repository.GetAllUsersAsync();
        return Ok(users);
    }

    [HttpGet("GetAllUsers")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _repository.GetAllAsync();


        return Ok(users.Select(u => u.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _repository.GetByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(user.ToDto());
    }

    [HttpGet("by-username/{username}")]
    public async Task<IActionResult> GetByUsername(string username)
    {
        var user = await _repository.GetByUsernameAsync(username);
        if (user == null) return NotFound();
        return Ok(user.ToDto());
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserDto dto)
    {
        // 🔐 Hash password before saving
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var newUser = dto.ToEntity(passwordHash);
        dto.Password = passwordHash; // Update DTO with hashed password
        var created = await _repository.AddUserAsync(dto);


        return CreatedAtAction(nameof(GetById), new { id = created.UserId }, created.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateUserDto dto)
    {
        if (id != dto.UserId) return BadRequest();

        //var existing = await _repository.GetByIdAsync(id);
        //if (existing == null) return NotFound();

        //// update fields
        //existing.FullName = dto.FullName;
        //existing.Email = dto.Email;
        //existing.Role = dto.Role;
        //existing.IsActive = dto.IsActive;
        //existing.FirstLogin = dto.FirstLogin;
        //existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);


        var updated = await _repository.UpdateUserAsync(dto);
        

        return Ok(updated!.ToDto());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _repository.DeleteAsync(id);
        if (!deleted) return NotFound();

        return NoContent();
    }


    
    //[HttpPost("login")]
    //public async Task<IActionResult> Login([FromBody] LoginDto dto)
    //{
    //    var token = await _repository.LoginAsync(dto);
    //    if (token == null) return Unauthorized("Invalid credentials");

    //    return Ok(new LoginResponseDto
    //    {
    //        Token = token,
    //        Username = dto.Username,
    //        Role = (await _repository.GetAllAsync())
    //                    .First(u => u.Username == dto.Username).Role
    //    });
    //}

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var token = await _repository.LoginAsync(dto);
        if (token == null) return Unauthorized("Invalid credentials");
        //return Ok(token);
        return Ok(new LoginResponseDto
        {
            Username = dto.Username,
            Role = token.Role,
            FullName = token.FullName,
            UserId = token.UserId,
            FirstLogin = token.FirstLogin,
            Email = token.Email

        });
    }

    [HttpPut("{id}/update-password")]
    public async Task<IActionResult> UpdatePassword(int id, [FromBody] UpdatePasswordDto dto)
    {
        try
        {
            var success = await _repository.UpdatePasswordAsync(id, dto);
            if (!success) return NotFound("User not found");
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(ex.Message);
        }
    }

    [HttpPut("{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] ResetPasswordDto dto)
    {
        var success = await _repository.ResetPasswordAsync(id, dto);
        if (!success) return NotFound("User not found");
        return NoContent();
    }
    [HttpPut("{userId}/backups")]
    public async Task<IActionResult> ReplaceUserBackups(int userId, [FromBody] List<int> backupUserIds)
    {
        if (backupUserIds == null)
            return BadRequest("Backup user IDs are required.");

        // Optional: prevent user from being their own backup
        if (backupUserIds.Contains(userId))
            return BadRequest("User cannot be their own backup.");

        await _repository.ReplaceBackupsAsync(userId, backupUserIds);

        return Ok(new
        {
            Message = "Backups updated successfully.",
            UserId = userId,
            BackupUserIds = backupUserIds
        });
    }
}
