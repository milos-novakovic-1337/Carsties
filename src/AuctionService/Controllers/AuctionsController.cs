using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService;

[ApiController]
[Route("api/auctions")]
public class AuctionsController : ControllerBase
{
    private readonly AuctionDbContext _context;
    private readonly IMapper _mapper;
    private readonly IPublishEndpoint _endpoint;

    public AuctionsController(AuctionDbContext context, IMapper mapper, IPublishEndpoint endpoint)
    {
        _endpoint = endpoint;
        _context = context;
        _mapper = mapper;
    }

    [HttpGet]
    public async Task<ActionResult<List<AuctionDto>>> GetAllAuctions(string date)
    {
        var query = _context.Auctions.OrderBy(x => x.Item.Make).AsQueryable();
        if (!string.IsNullOrEmpty(date))
            query = query.Where(x => x.UpdatedAt.CompareTo(DateTime.Parse(date).ToUniversalTime()) > 0);
        return await query.ProjectTo<AuctionDto>(_mapper.ConfigurationProvider).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionDto>> GetAuctionById(Guid id)
    {
        var auction = await _context.Auctions.Include(x => x.Item).FirstOrDefaultAsync(x => x.Id == id);
        if (auction == null) return NotFound();
        return _mapper.Map<AuctionDto>(auction);
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<AuctionDto>> CreateAuction(CreateAuctionDto auctionDto)
    {
        var auction = _mapper.Map<Auction>(auctionDto);
        // TODO: add current user as seller
        auction.Seller = User.Identity.Name;

        _context.Auctions.Add(auction);

        var newAuction = _mapper.Map<AuctionDto>(auction);

        await _endpoint.Publish(_mapper.Map<AuctionCreated>(newAuction));

        var result = await _context.SaveChangesAsync() > 0;

        if (!result) return BadRequest("Could not save changes to the DB");

        return CreatedAtAction(nameof(GetAuctionById),
            new { auction.Id }, newAuction);
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateAuction(Guid id, UpdateAuctionDto updateAuctionDto)
    {
        var updatedAuction = await _context.Auctions.Include(x => x.Item).FirstOrDefaultAsync(x => x.Id == id);
        if (updatedAuction == null) return NotFound();
        if (updatedAuction.Seller != User.Identity.Name) return Forbid();

        updatedAuction.Item.Make = updateAuctionDto.Make ?? updatedAuction.Item.Make;
        updatedAuction.Item.Model = updateAuctionDto.Model ?? updatedAuction.Item.Model;
        updatedAuction.Item.Color = updateAuctionDto.Color ?? updatedAuction.Item.Color;
        updatedAuction.Item.Mileage = updateAuctionDto.Mileage ?? updatedAuction.Item.Mileage;
        updatedAuction.Item.Year = updateAuctionDto.Year ?? updatedAuction.Item.Year;

        await _endpoint.Publish(_mapper.Map<AuctionUpdated>(updatedAuction));


        var result = await _context.SaveChangesAsync() > 0;
        if (result) return Ok();
        return BadRequest("Problem saving changes");
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAuction(Guid id)
    {
        var deletedAuction = await _context.Auctions.FindAsync(id);

        if (deletedAuction == null) return NotFound();
        if (deletedAuction.Seller != User.Identity.Name) return Forbid();
        //if (deletedAuction.Seller != User.Identity.Name) return Forbid();

        _context.Auctions.Remove(deletedAuction);

        await _endpoint.Publish<AuctionDeleted>(new { Id = deletedAuction.Id.ToString() });

        var result = await _context.SaveChangesAsync() > 0;

        if (!result) return BadRequest("Could not update DB");

        return Ok();
    }
}
