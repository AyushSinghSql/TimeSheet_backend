using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using TimeSheet.Models;

namespace TimeSheet.Repository
{

    public interface IConfigValueRepository
    {
        Task<IEnumerable<ConfigValue>> GetAllAsync();
        Task<ConfigValue?> GetByNameAsync(string name);
        Task AddAsync(ConfigValue configValue);
        Task UpdateAsync(List<ConfigValue> configValue);
        Task DeleteAsync(string name);
        Task SaveAsync();
    }
    public class ConfigValueRepository : IConfigValueRepository
    {
        private readonly AppDbContext _context;

        public ConfigValueRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ConfigValue>> GetAllAsync() =>
            await _context.ConfigValues.ToListAsync();

        public async Task<ConfigValue?> GetByNameAsync(string name) =>
            await _context.ConfigValues.FindAsync(name);

        public async Task AddAsync(ConfigValue configValue)
        {
            _context.ConfigValues.Add(configValue);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(List<ConfigValue> configValues)
        {
            _context.ConfigValues.UpdateRange(configValues);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(string name)
        {
            var entity = await _context.ConfigValues.FindAsync(name);
            if (entity != null)
            {
                _context.ConfigValues.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task SaveAsync() => await _context.SaveChangesAsync();
    }
}