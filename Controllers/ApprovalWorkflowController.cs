using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimeSheet.Models;

namespace TimeSheet.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ApprovalWorkflowController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ApprovalWorkflowController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/ApprovalWorkflow
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ApprovalWorkflow>>> GetAll()
        {
            return await _context.ApprovalWorkflows
                                 .OrderBy(w => w.RequestType)
                                 .ThenBy(w => w.LevelNo)
                                 .ToListAsync();
        }

        // GET: api/ApprovalWorkflow/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<ApprovalWorkflow>> GetById(int id)
        {
            var workflow = await _context.ApprovalWorkflows.FindAsync(id);
            if (workflow == null)
                return NotFound("Workflow not found.");

            return workflow;
        }

        // POST: api/ApprovalWorkflow
        [HttpPost]
        public async Task<ActionResult<ApprovalWorkflow>> Create(ApprovalWorkflow workflow)
        {
            // Check unique constraint: (request_type, level_no)
            bool exists = await _context.ApprovalWorkflows
                .AnyAsync(w => w.RequestType == workflow.RequestType && w.LevelNo == workflow.LevelNo);

            if (exists)
                return Conflict("Workflow with this request type and level already exists.");

            _context.ApprovalWorkflows.Add(workflow);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = workflow.WorkflowId }, workflow);
        }

        // PUT: api/ApprovalWorkflow/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, ApprovalWorkflow workflow)
        {
            if (id != workflow.WorkflowId)
                return BadRequest("Mismatched Workflow ID.");

            var existing = await _context.ApprovalWorkflows.FindAsync(id);
            if (existing == null)
                return NotFound("Workflow not found.");

            existing.RequestType = workflow.RequestType;
            existing.LevelNo = workflow.LevelNo;
            existing.ApproverRole = workflow.ApproverRole;
            existing.IsMandetory = workflow.IsMandetory;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                if (ex.InnerException?.Message.Contains("uq_workflow") == true)
                    return Conflict("Workflow with this request type and level already exists.");

                throw;
            }

            return NoContent();
        }

        // DELETE: api/ApprovalWorkflow/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var workflow = await _context.ApprovalWorkflows.FindAsync(id);
            if (workflow == null)
                return NotFound("Workflow not found.");

            _context.ApprovalWorkflows.Remove(workflow);
            await _context.SaveChangesAsync();

            return Ok("Workflow deleted successfully.");
        }
    }
}

