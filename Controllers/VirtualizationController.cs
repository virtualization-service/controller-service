using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Parser.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VirtualizationController : ControllerBase
    {
        private readonly ILogger<VirtualizationController> _logger;

        public VirtualizationController(ILogger<VirtualizationController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public ActionResult Get()
        {
            return Ok("Running");
        }
    }
}
