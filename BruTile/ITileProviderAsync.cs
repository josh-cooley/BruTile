using System.Threading.Tasks;

namespace BruTile
{
    public interface ITileProviderAsync : ITileProvider
    {
        Task<byte[]> GetTileAsync(TileInfo tileInfo);
    }
}
