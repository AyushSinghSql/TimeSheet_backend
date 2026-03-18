using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using TimeSheet.Models;
using TimeSheet.Repository;

[Route("api/[controller]")]
[ApiController]
public class ConfigValuesController : ControllerBase
{
    private readonly IConfigValueRepository _repository;

    public ConfigValuesController(IConfigValueRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConfigValue>>> GetAll()
    {
        var configs = await _repository.GetAllAsync();
        return Ok(configs);
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<ConfigValue>> GetByName(string name)
    {
        var config = await _repository.GetByNameAsync(name);
        if (config == null)
            return NotFound();
        return Ok(config);
    }

    [HttpPost]
    public async Task<ActionResult> Create(ConfigValue configValue)
    {
        await _repository.AddAsync(configValue);
        return CreatedAtAction(nameof(GetByName), new { name = configValue.Name }, configValue);
    }

    [HttpPut]
    public async Task<ActionResult> Update(List<ConfigValue> configValues)
    {
        await _repository.UpdateAsync(configValues);
        return NoContent();
    }

    [HttpDelete("{name}")]
    public async Task<ActionResult> Delete(string name)
    {
        await _repository.DeleteAsync(name);
        return NoContent();
    }
}