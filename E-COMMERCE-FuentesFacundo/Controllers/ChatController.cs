using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using E_COMMERCE_FuentesFacundo.Models.Identity;
using E_COMMERCE_FuentesFacundo.Models.ViewModels;
using E_COMMERCE_FuentesFacundo.Services;

namespace E_COMMERCE_FuentesFacundo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class ChatController : ControllerBase
    {
        private readonly GeminiChatService _chatService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ChatController> _logger;

        public ChatController(
            GeminiChatService chatService,
            UserManager<ApplicationUser> userManager,
            ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpPost]
        [EnableRateLimiting("chat")]
        public async Task<IActionResult> Post([FromBody] ChatRequest? request, CancellationToken cancellationToken)
        {
            if (request?.Messages is null || request.Messages.Count == 0)
            {
                return BadRequest(new { error = "Envía al menos un mensaje." });
            }

            string? userId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                userId = user?.Id;
            }

            try
            {
                var reply = await _chatService.ResponderAsync(request.Messages, userId, cancellationToken);
                return Ok(reply);
            }
            catch (GeminiChatException ex)
            {
                return StatusCode(ex.StatusCode, new { error = ex.UserMessage });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status408RequestTimeout, new { error = "La consulta fue cancelada o tardó demasiado." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en el chat.");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "No se pudo procesar el chat." });
            }
        }
    }
}
