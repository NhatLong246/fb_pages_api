using Microsoft.AspNetCore.Mvc;
using BackendApi.Models;
using BackendApi.Services;
using System.Net;

namespace BackendApi.Controllers
{
    [ApiController]
    [Route("api/page")]
    [Produces("application/json")]
    public class PageController : ControllerBase
    {
        private readonly IFacebookService _facebookService;

        public PageController(IFacebookService facebookService)
        {
            _facebookService = facebookService;
        }

        private IActionResult HandleFacebookException(FacebookApiException ex)
        {
            var fb = ex.FacebookError;
            var details = fb is null
                ? ex.Message
                : $"{fb.Type} (code {fb.Code}, subcode {fb.ErrorSubcode}, trace {fb.FbtraceId}): {fb.Message}";

            // Facebook OAuth errors (code 190) => token invalid/expired
            if (fb?.Code == 190)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, new ErrorResponse
                {
                    Error = $"Facebook access token không hợp lệ hoặc đã hết hạn. Hãy cập nhật cấu hình Facebook:PageAccessToken. {details}"
                });
            }

            // Upstream 5xx => 502 for our API
            if ((int)ex.UpstreamStatusCode >= 500)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new ErrorResponse { Error = details });
            }

            // Default: mirror upstream (usually 400) as a client error.
            var status = ex.UpstreamStatusCode == 0 ? HttpStatusCode.BadRequest : ex.UpstreamStatusCode;
            return StatusCode((int)status, new ErrorResponse { Error = details });
        }

        [HttpGet("{pageId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPageInfo(string pageId)
        {
            try
            {
                var result = await _facebookService.GetPageInfoAsync(pageId);
                return Ok(result);
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
        }

        [HttpGet("{pageId}/posts")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPosts(string pageId)
        {
            try
            {
                var result = await _facebookService.GetPostsAsync(pageId);
                return Ok(result);
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
        }

        [HttpPost("{pageId}/posts")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CreatePost(string pageId, [FromBody] CreatePostRequest request)
        {
            try
            {
                var result = await _facebookService.CreatePostAsync(pageId, request);
                return Ok(result);
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
        }

        [HttpDelete("post/{postId}")]
        [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> DeletePost(string postId)
        {
            try
            {
                var success = await _facebookService.DeletePostAsync(postId);
                if (success) return Ok(new SuccessResponse { Message = "Post deleted successfully" });
                return BadRequest(new ErrorResponse { Error = "Failed to delete post" });
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
        }

        [HttpGet("post/{postId}/comments")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetComments(string postId)
        {
            try
            {
                var result = await _facebookService.GetCommentsAsync(postId);
                return Ok(result);
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
        }

        [HttpGet("post/{postId}/likes")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetLikes(string postId)
        {
            try
            {
                var result = await _facebookService.GetLikesAsync(postId);
                return Ok(result);
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
        }

        [HttpGet("{pageId}/insights")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetInsights(string pageId)
        {
            try
            {
                var result = await _facebookService.GetInsightsAsync(pageId);
                return Ok(result);
            }
            catch (FacebookApiException ex)
            {
                return HandleFacebookException(ex);
            }
            catch (Exception ex)
            {
                return BadRequest(new ErrorResponse { Error = ex.Message });
            }
        }
    }
}
