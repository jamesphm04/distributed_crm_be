using System.ComponentModel.DataAnnotations;
using DistributedCrm.Application.Contracts;
using DistributedCrm.Application.Exceptions;
using DistributedCrm.Application.UseCases.ReturnRequests;
using DistributedCrm.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistributedCrm.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/return-requests")]
public class ReturnRequestsController : ControllerBase
{
    private readonly CreateReturnRequestHandler _createHandler;
    private readonly GetReturnRequestByIdHandler _getByIdHandler;
    private readonly ListReturnRequestsHandler _listHandler;
    private readonly CancelReturnRequestHandler _cancelHandler;

    public ReturnRequestsController(
        CreateReturnRequestHandler createHandler,
        GetReturnRequestByIdHandler getByIdHandler,
        ListReturnRequestsHandler listHandler,
        CancelReturnRequestHandler cancelHandler)
    {
        _createHandler = createHandler;
        _getByIdHandler = getByIdHandler;
        _listHandler = listHandler;
        _cancelHandler = cancelHandler;
    }
    
    [HttpPost]
    public async Task<ActionResult<ReturnRequestResponse>> Create(
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        [FromBody] CreateReturnRequestRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest("Idempotency-Key header is required.");
        }

        try
        {
            var response = await _createHandler.HandleAsync(idempotencyKey, request, ct);

            return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
        }
        catch (IdempotencyConflictException ex)
        {
            return Conflict(ex.Message);
        }
    }
    
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReturnRequestResponse>> GetById(Guid id, CancellationToken ct)
    {
        var response = await _getByIdHandler.HandleAsync(id, ct);
        return response is null ? NotFound() : Ok(response);
    }
    
    [HttpGet]
    public async Task<ActionResult<PagedResult<ReturnRequestResponse>>> List(
        [FromQuery][Range(1, int.MaxValue)] int page = 1,
        [FromQuery][Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
    {
        var response = await _listHandler.HandleAsync(page, pageSize, ct);
        return Ok(response);
    }
    
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<ReturnRequestResponse>> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var response = await _cancelHandler.HandleAsync(id, ct);
            return response is null ? NotFound() : Ok(response);
        }
        catch (InvalidStatusTransitionException ex)
        {
            return Conflict(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("The return request was modified by another request. Please retry.");
        }
    }
}