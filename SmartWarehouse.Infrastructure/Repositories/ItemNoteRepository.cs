using Microsoft.EntityFrameworkCore;
using SmartWarehouse.Application.Interfaces;
using SmartWarehouse.Domain.Entities;
using SmartWarehouse.Infrastructure.Data;

namespace SmartWarehouse.Infrastructure.Repositories;

public class ItemNoteRepository(WarehouseDbContext db) : IItemNoteRepository
{
    public Task<bool> ItemExistsAsync(int itemId, CancellationToken ct = default) =>
        db.Items.AnyAsync(x => x.Id == itemId, ct);

    public async Task<IReadOnlyList<ItemNote>> ListAsync(int itemId, CancellationToken ct = default) =>
        await db.ItemNotes.AsNoTracking()
            .Where(x => x.ItemId == itemId)
            .OrderByDescending(x => x.Pinned).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public Task<ItemNote?> GetAsync(int id, CancellationToken ct = default) =>
        db.ItemNotes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyDictionary<int, int>> CountByItemAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct = default) =>
        await db.ItemNotes.AsNoTracking()
            .Where(x => itemIds.Contains(x.ItemId))
            .GroupBy(x => x.ItemId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<IReadOnlyList<ItemNote>> FindAsync(IReadOnlyCollection<int> itemIds, string text, CancellationToken ct = default)
    {
        var pattern = $"%{text}%";
        return await db.ItemNotes.AsNoTracking()
            .Where(x => itemIds.Contains(x.ItemId) && EF.Functions.ILike(x.Text, pattern))
            .OrderByDescending(x => x.Pinned).ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public void Add(ItemNote note) => db.ItemNotes.Add(note);

    public void Remove(ItemNote note) => db.ItemNotes.Remove(note);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
